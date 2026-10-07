using System.Text.Json;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.History;

/// <summary>The listening history as stored on disk, newest first.</summary>
public sealed class HistoryFile
{
    public List<PlayRecord> Plays { get; set; } = [];
}

/// <summary>
/// The songs played on the account, kept on this computer for about 90
/// days. Spotify only shares the last 50 songs played (and only counts a
/// song once it played for 30 seconds), so Resonate saves them whenever it
/// looks, and Home's stats and mixes can reach further back than that.
/// Holds song names and picture links only, never tokens. Reading or
/// writing the file may fail; the history is then kept for this session.
/// </summary>
public sealed class ListeningHistory : IDisposable
{
    /// <summary>Plays older than this are dropped.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

    /// <summary>A sync within this long of the last one is skipped, so opening Home often costs nothing.</summary>
    public static readonly TimeSpan MinSyncInterval = TimeSpan.FromMinutes(1);

    /// <summary>A safety limit on the file's size (about 200 songs a day for 90 days).</summary>
    public const int MaxPlays = 20_000;

    /// <summary>Spotify's largest page of recently played songs.</summary>
    public const int PageLimit = 50;

    private const int MaxPagesPerSync = 4;

    private readonly ISpotifyWebApi _api;
    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Lock _gate = new();
    private IReadOnlyList<PlayRecord> _plays = [];
    private bool _loaded;
    private int _generation;
    private DateTimeOffset _lastSync = DateTimeOffset.MinValue;

    /// <param name="path">The file to keep the history in; null keeps it in memory only (demo mode, tests).</param>
    public ListeningHistory(ISpotifyWebApi api, string? path, TimeProvider? time = null)
    {
        _api = api;
        _path = path;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on any thread when plays were added (or the history was cleared).</summary>
    public event EventHandler? Changed;

    /// <summary>Every known play, newest first. Empty until <see cref="Load"/> ran.</summary>
    public IReadOnlyList<PlayRecord> Plays => Volatile.Read(ref _plays);

    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    /// <summary>Reads the stored history once. Call it off the interface thread.</summary>
    public void Load()
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            Volatile.Write(ref _plays, Merge(Read(), [], _time.GetUtcNow()));
            _loaded = true;
        }
    }

    /// <summary>
    /// Adds the songs played since the newest known play, from Spotify's
    /// recently played list. Returns how many plays were new.
    /// </summary>
    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Load();
            var now = _time.GetUtcNow();
            if (now - _lastSync < MinSyncInterval)
            {
                return 0;
            }

            // Counted before asking, so a failing request is not repeated at once either.
            _lastSync = now;
            int generation;
            lock (_gate)
            {
                generation = _generation;
            }

            var before = Plays;
            var incoming = await FetchSinceAsync(before.Count > 0 ? before[0].PlayedAt : null, cancellationToken).ConfigureAwait(false);
            var known = before.Select(Key).ToHashSet();
            var added = incoming.Select(Key).Distinct().Count(k => !known.Contains(k));
            if (added == 0)
            {
                return 0;
            }

            var merged = Merge(before, incoming, now);
            lock (_gate)
            {
                if (generation != _generation)
                {
                    // Signed out meanwhile: these plays belong to the old account.
                    return 0;
                }

                Volatile.Write(ref _plays, merged);
                Save(merged);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return added;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>Forgets every play, in memory and on disk (for example on signing out).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _loaded = true;
            _lastSync = DateTimeOffset.MinValue;
            Volatile.Write(ref _plays, []);
            Delete();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _syncLock.Dispose();

    /// <summary>
    /// Both lists together, newest first: each play once (the same song at
    /// the same moment is the same play), none older than <see cref="KeepFor"/>.
    /// </summary>
    internal static List<PlayRecord> Merge(IReadOnlyList<PlayRecord> existing, IEnumerable<PlayRecord> incoming, DateTimeOffset now)
    {
        var cutoff = now - KeepFor;
        var seen = new HashSet<(long, string)>();
        var all = new List<PlayRecord>(existing.Count + 50);
        foreach (var play in incoming.Concat(existing))
        {
            if (play.PlayedAt >= cutoff && seen.Add(Key(play)))
            {
                all.Add(play);
            }
        }

        all.Sort((a, b) => b.PlayedAt.CompareTo(a.PlayedAt));
        if (all.Count > MaxPlays)
        {
            all.RemoveRange(MaxPlays, all.Count - MaxPlays);
        }

        return all;
    }

    private static (long, string) Key(PlayRecord play) => (play.PlayedAt.ToUnixTimeMilliseconds(), play.Uri);

    private async Task<List<PlayRecord>> FetchSinceAsync(DateTimeOffset? after, CancellationToken cancellationToken)
    {
        var incoming = new List<PlayRecord>();
        for (var round = 0; round < MaxPagesPerSync; round++)
        {
            var page = await _api.GetRecentlyPlayedAsync(PageLimit, after, cancellationToken).ConfigureAwait(false);
            var plays = page.Items.Select(PlayRecord.From).OfType<PlayRecord>().ToList();
            incoming.AddRange(plays);

            // Without a starting point Spotify answers with the newest songs,
            // which is all it keeps. With one, a full page may have more after it.
            if (after is null || page.Items.Count < PageLimit || plays.Count == 0)
            {
                break;
            }

            var newest = plays.Max(p => p.PlayedAt);
            if (newest <= after)
            {
                break;
            }

            after = newest;
        }

        return incoming;
    }

    private List<PlayRecord> Read()
    {
        if (_path is null)
        {
            return [];
        }

        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.HistoryFile)?.Plays ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save(List<PlayRecord> plays)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, new HistoryFile { Plays = plays }, SpotifyJsonContext.Default.HistoryFile);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this session.
        }
    }

    private void Delete()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

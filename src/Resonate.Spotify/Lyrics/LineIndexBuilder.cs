using System.Text.Json;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Lyrics;

/// <summary>How far Line Finder's index is: songs looked up, of the songs to look up.</summary>
public readonly record struct LineIndexProgress(int Done, int Total)
{
    /// <summary>Songs are still being looked up.</summary>
    public bool IsBuilding => Done < Total;
}

/// <summary>
/// Fills Line Finder's index in the background, slowly: one lyrics lookup
/// at a time with a pause between them (LRCLIB is free and asks apps to go
/// easy), longer and longer pauses while LRCLIB cannot be reached, and
/// lyrics already kept on disk taken at once. It asks Spotify nothing: the
/// songs come from lists Resonate already keeps. Once every song is known
/// it waits until the library changes (see <see cref="Refresh"/>).
/// </summary>
public sealed class LineIndexBuilder
{
    /// <summary>The pause between lyrics lookups.</summary>
    public static readonly TimeSpan DefaultPace = TimeSpan.FromSeconds(1.5);

    /// <summary>The first pause after a failed lookup; it doubles with each failure in a row, up to <see cref="LongestWait"/>.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(15);

    /// <summary>The songs are read again at most this often, however often the library changes.</summary>
    public static readonly TimeSpan RescanAtMost = TimeSpan.FromMinutes(10);

    /// <summary>Without any change, the songs are read again after this long (songs added elsewhere).</summary>
    public static readonly TimeSpan RescanAnyway = TimeSpan.FromHours(1);

    private readonly LineIndex _index;
    private readonly ILyricsSource _source;
    private readonly LyricsCache? _cache;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LineIndexProgress _progress;

    /// <param name="cache">Lyrics the lyrics pane already kept, used without asking again; null for none.</param>
    public LineIndexBuilder(LineIndex index, ILyricsSource source, LyricsCache? cache, TimeProvider? time = null)
    {
        _index = index;
        _source = source;
        _cache = cache;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The pause between lookups (none in the demo and in tests).</summary>
    public TimeSpan Pace { get; init; } = DefaultPace;

    /// <summary>Raised on any thread when <see cref="Progress"/> changed.</summary>
    public event EventHandler<LineIndexProgress>? ProgressChanged;

    public LineIndexProgress Progress
    {
        get
        {
            lock (_gate)
            {
                return _progress;
            }
        }
    }

    /// <summary>The library changed: once every song known now is looked up, the songs are read again.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _changed.TrySetResult();
        }
    }

    /// <summary>
    /// The songs Line Finder covers, in the order to look them up: songs
    /// Spotify can play by their address, each once, in the order given.
    /// </summary>
    public static IReadOnlyList<TrackInfo> Covered(IEnumerable<TrackInfo> songs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var covered = new List<TrackInfo>();
        foreach (var song in songs)
        {
            if (song is { IsLocal: false, FilePath: null, Uri: { } uri }
                && uri.StartsWith("spotify:track:", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(song.Title)
                && !string.IsNullOrWhiteSpace(song.Artists)
                && seen.Add(uri))
            {
                covered.Add(song);
            }
        }

        return covered;
    }

    /// <summary>
    /// Looks up every song <paramref name="songs"/> gives that the index does
    /// not know, then waits for a change (or, with songs left after failed
    /// lookups, a while) and starts over, until cancelled.
    /// </summary>
    /// <param name="songs">Reads the songs to cover (from lists kept on disk; never a request to Spotify).</param>
    public async Task RunAsync(Func<CancellationToken, Task<IReadOnlyList<TrackInfo>>> songs, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_changed.Task.IsCompleted)
                {
                    _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                changed = _changed.Task;
            }

            var scanned = _time.GetUtcNow();
            var covered = Covered(await songs(cancellationToken).ConfigureAwait(false));
            failures = await PassAsync(covered, failures, cancellationToken).ConfigureAwait(false);

            // Every song known: wait for the library to change (or an hour). Songs left
            // after failed lookups are tried on the next pass. Either way the songs are
            // read no more often than RescanAtMost.
            if (!Progress.IsBuilding)
            {
                await Task.WhenAny(changed, Task.Delay(RescanAnyway, _time, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var next = scanned + RescanAtMost - _time.GetUtcNow();
            if (next > TimeSpan.Zero)
            {
                await Task.Delay(next, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>One pass over the songs. Returns the failed lookups in a row it ended with.</summary>
    private async Task<int> PassAsync(IReadOnlyList<TrackInfo> covered, int failures, CancellationToken cancellationToken)
    {
        await Task.Run(_index.Load, cancellationToken).ConfigureAwait(false);
        var missing = covered.Where(song => !_index.Knows(song.Uri!)).ToList();
        var done = covered.Count - missing.Count;
        Report(done, covered.Count);

        var lastLookup = DateTimeOffset.MinValue;
        foreach (var song in missing)
        {
            if (LyricsQuery.For(song.Title, song.Artists, song.Album, song.Duration) is not { } query)
            {
                _index.Add(song, null);
                Report(++done, covered.Count);
                continue;
            }

            if (_cache is not null && _cache.TryGet(query, out var kept))
            {
                _index.Add(song, kept);
                Report(++done, covered.Count);
                continue;
            }

            var wait = lastLookup + Pace - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }

            SongLyrics? lyrics;
            try
            {
                lastLookup = _time.GetUtcNow();
                lyrics = await _source.FindAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // An answer that cannot be read counts as none, so it is asked again in a month, not on every pass.
                lyrics = null;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException or TimeoutException)
            {
                // Offline, or LRCLIB is busy: the song is tried again on a later
                // pass, and lookups pause longer and longer while this goes on.
                failures++;
                await Task.Delay(WaitAfter(failures), _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Stopped meanwhile (signing out clears the index): this answer is not kept.
            cancellationToken.ThrowIfCancellationRequested();
            failures = 0;
            _index.Add(song, lyrics);
            Report(++done, covered.Count);
        }

        return failures;
    }

    /// <summary>The pause after <paramref name="failures"/> failed lookups in a row.</summary>
    public static TimeSpan WaitAfter(int failures)
    {
        var wait = FirstWait * Math.Pow(2, Math.Clamp(failures - 1, 0, 10));
        return wait < LongestWait ? wait : LongestWait;
    }

    private void Report(int done, int total)
    {
        var progress = new LineIndexProgress(done, total);
        lock (_gate)
        {
            if (_progress == progress)
            {
                return;
            }

            _progress = progress;
        }

        ProgressChanged?.Invoke(this, progress);
    }
}

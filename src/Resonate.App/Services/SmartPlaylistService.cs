using System.Security.Cryptography;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>A smart playlist changed: its rules (the songs must be worked out again), or only how it is kept on Spotify.</summary>
public sealed record SmartPlaylistChange(string Id, bool RulesChanged);

/// <summary>
/// The smart playlists plugin (Settings, Plugins): the user's smart
/// playlists, kept in the settings, and the playlists on Spotify that
/// follow them. Everything here runs on the interface thread except the
/// syncs themselves. While the plugin is off nothing runs.
/// </summary>
public sealed class SmartPlaylistService
{
    /// <summary>The daily refresh waits this long after start-up (or after the plugin is turned on), so it never slows the launch.</summary>
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);

    /// <summary>How often Resonate looks for a playlist whose daily refresh is due.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>Changes to the rules reach Spotify once the user has paused for this long.</summary>
    private static readonly TimeSpan EditDelay = TimeSpan.FromSeconds(5);

    private readonly AppServices _services;
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _syncing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _syncAgain = new(StringComparer.Ordinal);
    private CancellationTokenSource? _running;

    public SmartPlaylistService(AppServices services)
    {
        _services = services;
        Sync = new SmartPlaylistSync(services.Library);
    }

    /// <summary>Smart playlists were added, removed or renamed (the sidebar follows).</summary>
    public event EventHandler? ListChanged;

    /// <summary>One smart playlist changed.</summary>
    public event EventHandler<SmartPlaylistChange>? PlaylistChanged;

    /// <summary>A smart playlist was deleted (its page closes).</summary>
    public event EventHandler<string>? Deleted;

    /// <summary>Something to tell the user, such as a playlist deleted on Spotify.</summary>
    public event EventHandler<string>? Message;

    public SmartPlaylistSync Sync { get; }

    public bool IsOn => _services.BuiltIns.IsOn(BuiltInPlugins.SmartPlaylists);

    public IReadOnlyList<SmartPlaylist> All => _services.Settings.SmartPlaylists;

    public SmartPlaylist? Find(string id) => _services.Settings.SmartPlaylists.Find(p => p.Id == id);

    /// <summary>The playlist on Spotify is being brought up to date now.</summary>
    public bool IsSyncing(string id) => _syncing.Contains(id);

    /// <summary>The plugin was turned on (or Resonate started with it on): the daily refresh begins after a pause.</summary>
    public void Start()
    {
        if (_running is not null)
        {
            return;
        }

        _running = new CancellationTokenSource();
        _ = RefreshDailyAsync(_running.Token);
    }

    /// <summary>The plugin was turned off: nothing more is sent to Spotify.</summary>
    public void Stop()
    {
        foreach (var pending in _pending.Values)
        {
            pending.Cancel();
        }

        _pending.Clear();
        _syncAgain.Clear();
        // Not disposed: a sync on its way may still be watching the token.
        _running?.Cancel();
        _running = null;
    }

    /// <summary>A new smart playlist of all of Liked Songs, named so it stands apart from the others.</summary>
    public SmartPlaylist Create()
    {
        var name = "New smart playlist";
        for (var n = 2; All.Any(p => p.Name == name); n++)
        {
            name = $"New smart playlist {n}";
        }

        var playlist = new SmartPlaylist
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Name = name,
            Seed = RandomNumberGenerator.GetInt32(int.MaxValue),
        };
        _services.Settings.SmartPlaylists.Add(playlist);
        _services.SaveSettings();
        ListChanged?.Invoke(this, EventArgs.Empty);
        return playlist;
    }

    public void Delete(string id)
    {
        if (Find(id) is not { } playlist)
        {
            return;
        }

        if (_pending.Remove(id, out var pending))
        {
            pending.Cancel();
        }

        // The playlist on Spotify stays, as an ordinary playlist the user owns.
        _services.Settings.SmartPlaylists.Remove(playlist);
        _services.Settings.TrackSorts.Remove(Pages.Lists.SmartPlaylistSource.Prefix + id);
        _services.SaveSettings();
        ListChanged?.Invoke(this, EventArgs.Empty);
        Deleted?.Invoke(this, id);
    }

    public void Rename(string id, string name)
    {
        if (Find(id) is not { } playlist || string.IsNullOrWhiteSpace(name) || playlist.Name == name.Trim())
        {
            return;
        }

        playlist.Name = name.Trim();
        ListChanged?.Invoke(this, EventArgs.Empty);
        Edited(playlist, rulesChanged: false);
    }

    /// <summary>Turns a smart playlist into a ready-made one ("Long ones", "Fresh this month", "The 90s").</summary>
    public void ApplyStarter(string id, string starter)
    {
        if (Find(id) is not { } playlist)
        {
            return;
        }

        SmartStarters.Apply(playlist, starter);
        ListChanged?.Invoke(this, EventArgs.Empty);
        Edited(playlist);
    }

    /// <summary>
    /// Call after changing a smart playlist (its rules, order, limit, source
    /// or name): it is saved, its page follows, and its playlist on Spotify
    /// once the user pauses.
    /// </summary>
    public void Edited(SmartPlaylist playlist, bool rulesChanged = true)
    {
        _services.SaveSettings();
        PlaylistChanged?.Invoke(this, new SmartPlaylistChange(playlist.Id, rulesChanged));
        if (playlist.KeepOnSpotify && _running is { } running)
        {
            if (_pending.Remove(playlist.Id, out var earlier))
            {
                earlier.Cancel();
            }

            var later = CancellationTokenSource.CreateLinkedTokenSource(running.Token);
            _pending[playlist.Id] = later;
            _ = SyncLaterAsync(playlist.Id, later);
        }
    }

    /// <summary>"Keep on Spotify": on makes (or reuses) the playlist on Spotify and fills it at once.</summary>
    public void SetKeepOnSpotify(SmartPlaylist playlist, bool keep)
    {
        if (playlist.KeepOnSpotify == keep)
        {
            return;
        }

        playlist.KeepOnSpotify = keep;
        _services.SaveSettings();
        PlaylistChanged?.Invoke(this, new SmartPlaylistChange(playlist.Id, RulesChanged: false));
        if (keep && _running is { } running)
        {
            _ = SyncAsync(playlist.Id, full: true, userAsked: true, running.Token);
        }
    }

    private async Task SyncLaterAsync(string id, CancellationTokenSource later)
    {
        try
        {
            await Task.Delay(EditDelay, later.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_pending.TryGetValue(id, out var current) && current == later)
            {
                _pending.Remove(id);
            }

            later.Dispose();
        }

        if (_running is { } running)
        {
            await SyncAsync(id, full: false, userAsked: false, running.Token);
        }
    }

    /// <summary>Once a day per playlist, while Resonate runs: undoes edits made in Spotify and picks up newly liked songs.</summary>
    private async Task RefreshDailyAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FirstCheck, token);
            while (!token.IsCancellationRequested)
            {
                foreach (var playlist in All.Where(Sync.IsDue).ToList())
                {
                    await SyncAsync(playlist.Id, full: true, userAsked: false, token);
                }

                await Task.Delay(CheckInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Turned off, or closing.
        }
    }

    private async Task SyncAsync(string id, bool full, bool userAsked, CancellationToken token)
    {
        if (Find(id) is not { KeepOnSpotify: true } playlist)
        {
            return;
        }

        if (!_syncing.Add(id))
        {
            // One at a time per playlist; the newest rules follow when this one is done.
            _syncAgain.Add(id);
            return;
        }

        PlaylistChanged?.Invoke(this, new SmartPlaylistChange(id, RulesChanged: false));
        try
        {
            var copy = playlist.Clone();
            var result = await Task.Run(() => Sync.SyncAsync(copy, full, token), token);
            if (Find(id) is not { } live)
            {
                return;
            }

            if (!SmartPlaylistSync.Apply(live, result))
            {
                Message?.Invoke(this, $"“{live.Name}” was deleted on Spotify, so Resonate no longer keeps it there.");
            }
            else if (result.Outcome == SmartSyncOutcome.Failed && userAsked && result.Error is { } error)
            {
                Message?.Invoke(this, PlayerController.DescribeError(error));
            }

            _services.SaveSettings();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _syncing.Remove(id);
            PlaylistChanged?.Invoke(this, new SmartPlaylistChange(id, RulesChanged: false));
            if (_syncAgain.Remove(id) && !token.IsCancellationRequested)
            {
                _ = SyncAsync(id, full: false, userAsked: false, token);
            }
        }
    }
}

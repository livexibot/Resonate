using Microsoft.UI.Dispatching;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>
/// The equalizer: one setting for everything that plays. Spotify's songs go
/// through the Spotify app's own equalizer, which Resonate keeps in step
/// (<see cref="SpotifyEqualizerSync"/>); the user's own music files go
/// through the local files player. Lives on the interface thread: a change
/// shows and reaches the local files player at once, while saving it and
/// writing Spotify's settings happen in the background once the sliders
/// settle.
/// </summary>
public sealed class EqualizerService : IDisposable
{
    /// <summary>Dragging a slider sends many changes; save once it settles.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Spotify may write its settings file in several steps; read it once they are done.</summary>
    private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>Once Resonate is up, it looks at Spotify's equalizer once, quietly.</summary>
    private static readonly TimeSpan StartupReadDelay = TimeSpan.FromSeconds(5);

    private readonly AppServices _services;
    private readonly SpotifyEqualizerSync _sync;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _readTimer;
    private IDisposable? _watch;
    private int _watchGeneration;

    /// <summary>Counts the user's changes, so a slow background answer never undoes a newer one.</summary>
    private int _version;

    /// <param name="services">Settings, the players and the Spotify app launcher.</param>
    /// <param name="spotifyFolders">Where Spotify keeps its settings (none in demo mode).</param>
    /// <param name="restarter">Restarts Spotify; null in demo mode.</param>
    public EqualizerService(AppServices services, IReadOnlyList<string> spotifyFolders, ISpotifyAppRestarter? restarter)
    {
        _services = services;
        Current = services.IsDemo
            ? EqualizerSettings.Flat.WithPreset(EqualizerPresets.All.Single(p => p.Name == "Rock")) with { Enabled = true }
            : services.Settings.Equalizer;

        _sync = new SpotifyEqualizerSync(
            spotifyFolders,
            services.Launcher,
            restarter,
            services.Player.Spotify,
            services.Settings.EqualizerPendingForSpotify ? Current : null);
        _sync.PendingChanged += OnPendingChanged;
        if (restarter is not null)
        {
            // Every time Resonate starts Spotify, a waiting change goes in first.
            restarter.BeforeStart = _sync.ApplyPendingBeforeStart;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = SaveDelay;
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _ = ApplyAsync();
        _readTimer = _dispatcher.CreateTimer();
        _readTimer.IsRepeating = false;
        _readTimer.Tick += (_, _) => _ = RefreshAsync(fromWatcher: true);

        services.Player.Local.SetEqualizer(Current);

        // Picks up a change made in the Spotify app since last time, so local files sound the same.
        _readTimer.Interval = StartupReadDelay;
        _readTimer.Start();
    }

    /// <summary>Raised on the interface thread when anything below changed.</summary>
    public event EventHandler? Changed;

    public EqualizerSettings Current { get; private set; }

    /// <summary>A line saying where Spotify stands with the equalizer.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>False when the Spotify app is set below Lossless; null when Resonate can not tell.</summary>
    public bool? SpotifyLossless { get; private set; }

    /// <summary>A change waits for Spotify to restart, and Resonate can restart it now.</summary>
    public bool CanRestartSpotify { get; private set; }

    public bool IsRestarting { get; private set; }

    /// <summary>Shows and plays <paramref name="settings"/> at once; Spotify gets it once the sliders settle.</summary>
    public void Set(EqualizerSettings settings)
    {
        if (settings.Equals(Current))
        {
            return;
        }

        _version++;
        Current = settings;
        _services.Settings.Equalizer = settings;
        _services.Player.Local.SetEqualizer(settings);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Reads Spotify's equalizer and shows it, unless a change of the user's is still on its way to Spotify.</summary>
    public Task RefreshAsync() => RefreshAsync(fromWatcher: false);

    /// <summary>While the equalizer is on screen, notices changes made in the Spotify app.</summary>
    public async Task StartWatchingAsync()
    {
        StopWatching();
        var generation = _watchGeneration;
        var watch = await Task.Run(() => _sync.Watch(() => _dispatcher.TryEnqueue(RestartReadTimer)));
        if (generation != _watchGeneration)
        {
            // Stopped (or restarted) meanwhile.
            watch?.Dispose();
            return;
        }

        _watch = watch;
    }

    public void StopWatching()
    {
        _watchGeneration++;
        _watch?.Dispose();
        _watch = null;
        _readTimer.Stop();
    }

    /// <summary>
    /// Restarts the Spotify app so it uses the waiting change now, and puts
    /// the song that was playing back where it was.
    /// </summary>
    public async Task RestartSpotifyAsync()
    {
        if (IsRestarting)
        {
            return;
        }

        if (_saveTimer.IsRunning)
        {
            // The newest change first.
            _saveTimer.Stop();
            await ApplyAsync();
        }

        IsRestarting = true;
        CanRestartSpotify = false;
        Status = "Restarting Spotify…";
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var outcome = await Task.Run(() => _sync.RestartSpotifyAsync(CancellationToken.None));
            Status = SpotifyEqualizerSync.Describe(outcome);
            CanRestartSpotify = outcome.Restart == SpotifyRestartStatus.CouldNotClose && _sync.Pending is not null;
        }
        catch (Exception)
        {
            Status = "Spotify could not be restarted. Spotify will use this the next time Resonate starts it.";
        }
        finally
        {
            IsRestarting = false;
        }

        SaveSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        StopWatching();
        _sync.PendingChanged -= OnPendingChanged;
        if (_saveTimer.IsRunning)
        {
            // Closing right after a change: give it to Spotify (or keep it waiting) before leaving.
            _saveTimer.Stop();
            _sync.Apply(Current);
            SaveSettings();
        }
    }

    private async Task ApplyAsync()
    {
        var settings = Current;
        var version = _version;
        SaveSettings();

        var result = await Task.Run(() => _sync.Apply(settings));
        if (version != _version)
        {
            // A newer change follows with its own answer.
            return;
        }

        Status = SpotifyEqualizerSync.Describe(result);
        CanRestartSpotify = result == EqualizerApplyResult.Pending && _sync.CanRestart;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshAsync(bool fromWatcher)
    {
        var version = _version;
        var status = await Task.Run(_sync.Refresh);
        SpotifyLossless = status.Spotify?.IsLossless;
        if (version != _version || _saveTimer.IsRunning || IsRestarting)
        {
            // The user changed something meanwhile; that answer wins.
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var adopted = false;
        if (status.Pending is null && status.Spotify is { } spotify && !spotify.Equalizer.Equals(Current))
        {
            // Changed in the Spotify app: show Spotify's equalizer, and use it for local files too.
            Current = spotify.Equalizer;
            _services.Player.Local.SetEqualizer(Current);
            SaveSettings();
            adopted = true;
        }

        CanRestartSpotify = _sync.CanRestart && status.Spotify is not null && status.Pending is not null && status.SpotifyRunning;
        if (!fromWatcher || adopted)
        {
            Status = status.Spotify is null ? SpotifyEqualizerSync.NotFoundText
                : status.Pending is not null ? SpotifyEqualizerSync.Describe(EqualizerApplyResult.Pending)
                : fromWatcher ? "Updated from the Spotify app's own equalizer."
                : "The same as the Spotify app's own equalizer.";
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RestartReadTimer()
    {
        _readTimer.Stop();
        _readTimer.Interval = ReadDelay;
        _readTimer.Start();
    }

    /// <summary>On any thread: a waiting change reached Spotify (for example when Resonate started it), or a new one waits.</summary>
    private void OnPendingChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        SaveSettings();
        if (_sync.Pending is null && !IsRestarting && Status == SpotifyEqualizerSync.Describe(EqualizerApplyResult.Pending))
        {
            Status = SpotifyEqualizerSync.Describe(EqualizerApplyResult.Applied);
            CanRestartSpotify = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    });

    private void SaveSettings()
    {
        _services.Settings.Equalizer = Current;
        _services.Settings.EqualizerPendingForSpotify = _sync.Pending is not null;
        _services.SaveSettings();
    }
}

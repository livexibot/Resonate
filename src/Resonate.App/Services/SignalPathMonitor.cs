using Microsoft.UI.Dispatching;
using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Windows;

namespace Resonate.App.Services;

/// <summary>
/// Signal path, a built-in plugin: keeps a verdict on whether what plays
/// reaches the output losslessly (<see cref="SignalPath"/>). It reads the
/// Spotify app's settings file when it changes, the default output when
/// Windows says it changed, and a local file's header when it starts; all
/// of it in the background, reading only. With "Spotify Web API only" the
/// Spotify app's files are never opened. Lives on the interface thread
/// while the plugin is on; disposing it stops every watch.
/// </summary>
internal sealed class SignalPathMonitor : IDisposable
{
    /// <summary>Spotify may write its settings file in several steps; read it once they are done.</summary>
    private static readonly TimeSpan PrefsSettle = TimeSpan.FromMilliseconds(700);

    /// <summary>Windows sends a burst of notifications when the output changes; read once it is over.</summary>
    private static readonly TimeSpan OutputSettle = TimeSpan.FromMilliseconds(300);

    private readonly AppServices _services;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _prefsTimer;
    private readonly DispatcherQueueTimer _outputTimer;
    private readonly IReadOnlyList<string> _spotifyFolders;

    private IDisposable? _prefsWatch;
    private IDisposable? _outputWatch;
    private int _prefsWatchGeneration;
    private int _prefsReads;
    private int _outputReads;
    private int _evaluateQueued;
    private bool _disposed;

    private bool _prefsKnown;
    private SpotifyAudioPrefs? _prefs;
    private bool _outputKnown;
    private AudioOutputInfo? _output;
    private string? _formatPath;
    private bool _formatKnown;
    private LocalAudioFormat? _format;

    public SignalPathMonitor(AppServices services)
    {
        _services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _spotifyFolders = services.IsDemo ? [] : SpotifyAppLauncher.SettingsFolders();

        _prefsTimer = _dispatcher.CreateTimer();
        _prefsTimer.Interval = PrefsSettle;
        _prefsTimer.IsRepeating = false;
        _prefsTimer.Tick += (_, _) => _ = ReadPrefsAsync();
        _outputTimer = _dispatcher.CreateTimer();
        _outputTimer.Interval = OutputSettle;
        _outputTimer.IsRepeating = false;
        _outputTimer.Tick += (_, _) => _ = ReadOutputAsync();

        services.Player.StateChanged += OnPlayerStateChanged;
        services.ControlChannelChanged += OnChannelChanged;
        services.Equalizer.Changed += OnEqualizerChanged;

        if (services.IsDemo)
        {
            // Made up, so CI's screenshots show the pill without a Spotify app or a sound card.
            _prefs = new SpotifyAudioPrefs("demo", EqualizerSettings.Flat, IsLossless: true) { Quality = SpotifyPrefs.LosslessQuality, Normalize = false };
            _prefsKnown = true;
            _output = new AudioOutputInfo("Speakers (USB DAC)", SignalPath.SpotifyRate, 24, IsBluetooth: false);
            _outputKnown = true;
        }
        else
        {
            _ = WatchPrefsAsync();
            _ = ReadPrefsAsync();
            _ = WatchOutputAsync();
            _ = ReadOutputAsync();
        }

        Evaluate();
    }

    /// <summary>Raised on the interface thread when <see cref="Report"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The verdict for what plays now; null while nothing plays or it is still being worked out.</summary>
    public SignalReport? Report { get; private set; }

    /// <summary>Reads Spotify's settings and the output again, for when the user opens the chain.</summary>
    public void Refresh()
    {
        if (!_services.IsDemo && !_disposed)
        {
            _ = ReadPrefsAsync();
            _ = ReadOutputAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Player.StateChanged -= OnPlayerStateChanged;
        _services.ControlChannelChanged -= OnChannelChanged;
        _services.Equalizer.Changed -= OnEqualizerChanged;
        _prefsTimer.Stop();
        _outputTimer.Stop();
        var watches = new[] { _prefsWatch, _outputWatch };
        _prefsWatch = null;
        _outputWatch = null;

        // Unregistering from Windows' audio service is a COM call: off the interface thread.
        _ = Task.Run(() =>
        {
            foreach (var watch in watches)
            {
                watch?.Dispose();
            }
        });
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        // Arrives on background threads, often; work it out once for the newest state.
        if (Interlocked.Exchange(ref _evaluateQueued, 1) == 0)
        {
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _evaluateQueued, 0);
                Evaluate();
            });
        }
    }

    private void OnChannelChanged(object? sender, EventArgs e)
    {
        if (_services.IsDemo)
        {
            Evaluate();
            return;
        }

        // "Spotify Web API only" stops looking at the Spotify app's settings; switching back starts
        // again. What shows stays until the new read is in.
        _prefsKnown = false;
        _ = WatchPrefsAsync();
        _ = ReadPrefsAsync();
    }

    private void OnEqualizerChanged(object? sender, EventArgs e) => Evaluate();

    private void Evaluate()
    {
        if (_disposed)
        {
            return;
        }

        var state = _services.Player.State;
        SignalReport? report;
        if (!state.HasTrack)
        {
            report = null;
        }
        else if (state.Source == PlaybackSource.LocalFiles)
        {
            var path = LocalPath(state.TrackUri);
            if (!string.Equals(path, _formatPath, StringComparison.OrdinalIgnoreCase))
            {
                _formatPath = path;
                _formatKnown = false;
                _ = ReadFormatAsync(path);
            }

            if (!_formatKnown || !_outputKnown)
            {
                // Keeps what it showed until the new song is worked out, so the pill does not flicker.
                return;
            }

            report = SignalPath.ForLocalFile(new LocalSignal
            {
                Format = _format,
                EqualizerOn = _services.Equalizer.Current.Enabled,
                Volume = state.Volume,
                Output = _output,
            });
        }
        else
        {
            var usesApp = _services.UsesSpotifyApp;
            if (usesApp && (!_prefsKnown || !_outputKnown))
            {
                return;
            }

            report = SignalPath.ForSpotify(new SpotifySignal
            {
                UsesSpotifyApp = usesApp,
                OtherDevice = _services.IsDemo ? null : SignalPath.OtherDevice(state.DeviceName, Environment.MachineName),
                Prefs = usesApp ? _prefs : null,
                Volume = _services.IsDemo ? 1 : state.Volume,
                Output = _output,
            });
        }

        if (report is null ? Report is null : report.SameAs(Report))
        {
            return;
        }

        Report = report;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task WatchPrefsAsync()
    {
        var generation = ++_prefsWatchGeneration;
        var old = _prefsWatch;
        _prefsWatch = null;
        old?.Dispose();
        if (!_services.UsesSpotifyApp)
        {
            return;
        }

        var folders = _spotifyFolders;
        var watch = await Task.Run(() => SpotifyPrefsFile.FindNewest(folders) is { } path
            ? SpotifyPrefsFile.Watch(path, () => _dispatcher.TryEnqueue(RestartPrefsTimer))
            : null);
        if (_disposed || generation != _prefsWatchGeneration)
        {
            watch?.Dispose();
            return;
        }

        _prefsWatch = watch;
    }

    private void RestartPrefsTimer()
    {
        if (!_disposed)
        {
            _prefsTimer.Stop();
            _prefsTimer.Start();
        }
    }

    private async Task ReadPrefsAsync()
    {
        var read = ++_prefsReads;
        SpotifyAudioPrefs? prefs = null;
        if (_services.UsesSpotifyApp)
        {
            var folders = _spotifyFolders;
            prefs = await Task.Run(() => SpotifyPrefsFile.FindNewest(folders) is { } path ? SpotifyPrefsFile.TryRead(path) : null);
        }

        if (_disposed || read != _prefsReads)
        {
            return;
        }

        _prefs = prefs;
        _prefsKnown = true;
        Evaluate();
    }

    private async Task WatchOutputAsync()
    {
        var watch = await Task.Run(() => DefaultAudioOutput.Watch(() => _dispatcher.TryEnqueue(RestartOutputTimer)));
        if (_disposed)
        {
            _ = Task.Run(() => watch?.Dispose());
            return;
        }

        _outputWatch = watch;
    }

    private void RestartOutputTimer()
    {
        if (!_disposed)
        {
            _outputTimer.Stop();
            _outputTimer.Start();
        }
    }

    private async Task ReadOutputAsync()
    {
        var read = ++_outputReads;
        var output = await Task.Run(DefaultAudioOutput.TryRead);
        if (_disposed || read != _outputReads)
        {
            return;
        }

        _output = output;
        _outputKnown = true;
        Evaluate();
    }

    private async Task ReadFormatAsync(string? path)
    {
        var format = path is null ? null : await Task.Run(() => LocalAudioFormat.Read(path));
        if (_disposed || !string.Equals(path, _formatPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _format = format;
        _formatKnown = true;
        Evaluate();
    }

    /// <summary>The file a local song's file: address points at.</summary>
    private static string? LocalPath(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;
}

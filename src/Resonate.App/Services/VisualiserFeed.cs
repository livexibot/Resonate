using Microsoft.UI.Dispatching;
using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>
/// The visualisers' data: the classic player's and the now-playing stage's.
/// The local files player feeds both (Resonate's own audio graph, the
/// user's music files). For Spotify songs the stage alone may also hear the
/// program that plays them on this PC (<see cref="SpotifySoundListener"/>,
/// the owner's choice of 8 October 2026; the classic player stays at rest);
/// that sound is only turned into bar heights, never kept.
/// </summary>
public sealed class VisualiserFeed : ILocalAudioSink, IDisposable
{
    private readonly PlayerRouter _player;
    private readonly LocalAudioListener _listener;
    private readonly SpotifySoundListener? _heard;
    private readonly DispatcherQueue _queue;
    private readonly HashSet<object> _viewers = [];
    private readonly HashSet<object> _stageViewers = [];
    private volatile bool _classicWanted;
    private volatile bool _stageWanted;
    private int _updateQueued;

    /// <summary>Call on the interface thread.</summary>
    /// <param name="player">The player whose music is shown.</param>
    /// <param name="capture">Hears a program's sound for the stage (Windows' process loopback); null in demo mode.</param>
    /// <param name="findSpotifySound">The process that plays Spotify's sound on this PC, or null; called off the interface thread.</param>
    public VisualiserFeed(PlayerRouter player, IAppSoundCapture? capture = null, Func<int?>? findSpotifySound = null)
    {
        _player = player;
        _queue = DispatcherQueue.GetForCurrentThread();
        _listener = new LocalAudioListener(player, this);
        if (capture is not null && findSpotifySound is not null)
        {
            _heard = new SpotifySoundListener(player, capture, findSpotifySound, new StageInput(Stage));
            _heard.HearingChanged += OnStateChanged;
        }

        player.StateChanged += OnStateChanged;
        IsLive = HasSound;
    }

    /// <summary>Winamp's classic look by default: 19 bars that move in whole rows, 60 steps a second of music.</summary>
    public SpectrumAnalyser Analyser { get; } = new() { Bars = VisualiserBars.Classic, Motion = VisualiserMotion.Stepped };

    /// <summary>The now-playing stage's: all 75 bands, moving smoothly.</summary>
    public SpectrumAnalyser Stage { get; } = new() { Bars = VisualiserBars.Wide, Motion = VisualiserMotion.Smooth };

    /// <summary>
    /// Some visualiser is showing and wants data. While false, the local files
    /// engine detaches its tap and nothing is analysed.
    /// </summary>
    public bool Wanted => _listener.Wanted;

    /// <summary>
    /// Says whether <paramref name="viewer"/> (a classic player in the window
    /// or the mini player, or with <paramref name="stage"/> a now-playing
    /// stage; several can exist at once) shows a visualiser. The feed is
    /// wanted while any viewer wants it, and each analyser only works while
    /// a viewer of its own wants it. Call on the interface thread.
    /// </summary>
    public void SetWanted(object viewer, bool wanted, bool stage = false)
    {
        var viewers = stage ? _stageViewers : _viewers;
        if (wanted)
        {
            viewers.Add(viewer);
        }
        else
        {
            viewers.Remove(viewer);
        }

        // An analyser that rested while unwanted starts again from nothing.
        var classic = _viewers.Count > 0;
        if (classic && !_classicWanted)
        {
            Analyser.Reset();
        }

        var staged = _stageViewers.Count > 0;
        if (staged && !_stageWanted)
        {
            Stage.Reset();
        }

        _classicWanted = classic;
        _stageWanted = staged;
        _listener.Wanted = classic || staged;
        if (_heard is not null)
        {
            _heard.Wanted = staged;
        }
    }

    /// <summary>
    /// The stage's bars follow Spotify's sound (Settings, Plugins, Home stage,
    /// "Listen to Spotify"; on at first). Any thread.
    /// </summary>
    public bool ListensToSpotify
    {
        get => _heard?.Enabled ?? false;
        set
        {
            if (_heard is not null)
            {
                _heard.Enabled = value;
            }
        }
    }

    /// <summary>
    /// The stage's analyser has sound to show: a local file plays, or the
    /// program playing a Spotify song is heard. False otherwise (the stage's
    /// bars then sway on their own).
    /// </summary>
    public bool IsLive { get; private set; }

    /// <summary>Raised on the interface thread when <see cref="IsLive"/> changes.</summary>
    public event EventHandler? LiveChanged;

    // The engine, from a background thread, when it connects to a graph.
    void ILocalAudioSink.Start(int latencySamples)
    {
        Analyser.LagSamples = latencySamples;
        Analyser.Reset();
        Stage.LagSamples = latencySamples;
        Stage.Reset();
    }

    // The audio thread, once per quantum: no locks, no allocation.
    void ILocalAudioSink.Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (_classicWanted)
        {
            Analyser.Process(interleaved, channels, sampleRate);
        }

        if (_stageWanted)
        {
            Stage.Process(interleaved, channels, sampleRate);
        }
    }

    /// <summary>The program that plays Spotify's sound may have changed (the control mode switched, the own player started or stopped).</summary>
    public void LookAgain() => _heard?.LookAgain();

    public void Dispose()
    {
        _player.StateChanged -= OnStateChanged;
        _listener.Dispose();
        if (_heard is not null)
        {
            _heard.HearingChanged -= OnStateChanged;
            _heard.Dispose();
        }
    }

    private bool HasSound =>
        _listener.IsLive || (_heard is { HearsSound: true } && _player.ActiveSource == PlaybackSource.Spotify);

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; look at the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            _queue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                SetLive(HasSound);
            });
        }
    }

    private void SetLive(bool live)
    {
        if (IsLive != live)
        {
            IsLive = live;
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Spotify's heard sound goes to the stage's analyser only. It is heard as
    /// Windows mixes it, so there is no graph latency to look back over.
    /// </summary>
    private sealed class StageInput(SpectrumAnalyser stage) : ISoundSink
    {
        public void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
        {
            if (stage.LagSamples != 0)
            {
                stage.LagSamples = 0;
            }

            stage.Process(interleaved, channels, sampleRate);
        }
    }
}

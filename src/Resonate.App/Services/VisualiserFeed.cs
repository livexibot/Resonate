using Microsoft.UI.Dispatching;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>
/// The visualisers' data: the classic player's and the now-playing stage's.
/// Only the local files player feeds it (Resonate's own audio graph, the
/// user's music files); Spotify's sound is never heard or analysed, so for
/// Spotify songs it stays at rest.
/// </summary>
public sealed class VisualiserFeed : ILocalAudioSink, IDisposable
{
    private readonly PlayerRouter _player;
    private readonly LocalAudioListener _listener;
    private readonly DispatcherQueue _queue;
    private readonly HashSet<object> _viewers = [];
    private readonly HashSet<object> _stageViewers = [];
    private volatile bool _classicWanted;
    private volatile bool _stageWanted;
    private int _updateQueued;

    /// <summary>Call on the interface thread.</summary>
    public VisualiserFeed(PlayerRouter player)
    {
        _player = player;
        _queue = DispatcherQueue.GetForCurrentThread();
        _listener = new LocalAudioListener(player, this);
        player.StateChanged += OnStateChanged;
        IsLive = _listener.IsLive;
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
    }

    /// <summary>A local file is playing, so the analyser has sound to show; false for Spotify songs.</summary>
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

    public void Dispose()
    {
        _player.StateChanged -= OnStateChanged;
        _listener.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; look at the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            _queue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                SetLive(_listener.IsLive);
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
}

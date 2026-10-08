using Microsoft.UI.Dispatching;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>
/// The classic player's visualiser data. Only the local files player feeds
/// it (Resonate's own audio graph, the user's music files); Spotify's sound
/// is never heard or analysed, so for Spotify songs it stays at rest.
/// </summary>
public sealed class VisualiserFeed : ILocalAudioSink, IDisposable
{
    private readonly PlayerRouter _player;
    private readonly LocalAudioListener _listener;
    private readonly DispatcherQueue _queue;
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

    /// <summary>
    /// A visualiser is showing and wants data. While false, the local files
    /// engine detaches its tap and nothing is analysed.
    /// </summary>
    public bool Wanted
    {
        get => _listener.Wanted;
        set => _listener.Wanted = value;
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
    }

    // The audio thread, once per quantum: no locks, no allocation.
    void ILocalAudioSink.Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate) =>
        Analyser.Process(interleaved, channels, sampleRate);

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

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Keeps a local audio engine's visualiser tap in step with its sound graph.
/// The sink the player asked for lasts as long as the engine; a tap lasts
/// only as long as one graph. So a graph rebuilt after a device change gets
/// a new tap at once, and no tap outlives its graph. Not thread-safe: the
/// engine calls it under its own lock. Nothing here throws, because a tap
/// that cannot be made means no visualiser, never music that stops.
/// </summary>
public sealed class LocalAudioTapSlot
{
    private Func<ILocalAudioSink, IDisposable?>? _attach;
    private IDisposable? _tap;

    /// <summary>The sink to feed, or null while no visualiser shows.</summary>
    public ILocalAudioSink? Sink { get; private set; }

    /// <summary>A tap on the current graph feeds <see cref="Sink"/>.</summary>
    public bool IsAttached => _tap is not null;

    /// <summary>The sink to feed from now on (null for none). The current graph's tap is taken down and, for a new sink, made again.</summary>
    public void SetSink(ILocalAudioSink? sink)
    {
        if (ReferenceEquals(Sink, sink))
        {
            return;
        }

        Detach();
        Sink = sink;
        Attach();
    }

    /// <summary>
    /// A graph is ready (the first, or a new one after the old one failed).
    /// <paramref name="attach"/> hangs a tap for a sink on it and returns
    /// it, or returns null when it cannot.
    /// </summary>
    public void GraphReady(Func<ILocalAudioSink, IDisposable?> attach)
    {
        Detach();
        _attach = attach;
        Attach();
    }

    /// <summary>The graph is going away: its tap goes first.</summary>
    public void GraphGone()
    {
        Detach();
        _attach = null;
    }

    private void Attach()
    {
        if (_tap is not null || Sink is not { } sink || _attach is not { } attach)
        {
            return;
        }

        try
        {
            _tap = attach(sink);
        }
        catch (Exception)
        {
            // No visualiser; the music plays on. The next graph or sink tries again.
            _tap = null;
        }
    }

    private void Detach()
    {
        var tap = _tap;
        _tap = null;
        try
        {
            tap?.Dispose();
        }
        catch (Exception)
        {
            // A tap on a graph that is failing; the graph goes anyway.
        }
    }
}

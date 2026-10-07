using System.Runtime.InteropServices;
using Resonate.Spotify.LocalFiles;
using Resonate.Windows.Interop;
using Windows.Foundation;
using Windows.Media.Audio;

namespace Resonate.Windows.LocalAudio;

/// <summary>
/// Feeds the visualiser from the local files graph while it shows: a frame
/// output node hangs off the EQ bus next to the sound device, so it hears
/// the same sound (after the equalizer and limiter, before the volume), and
/// each quantum is read on the audio thread as it starts. One tap per graph:
/// a new graph (after a device change) gets a new tap. Only the user's own
/// files ever reach the bus, never Spotify's audio.
/// </summary>
internal sealed partial class AudioGraphTap : IDisposable
{
    private readonly AudioGraph _graph;
    private readonly AudioSubmixNode _bus;
    private readonly AudioFrameOutputNode _node;
    private readonly ILocalAudioSink _sink;
    private readonly TypedEventHandler<AudioGraph, object> _onQuantumStarted;

    // The node's IAudioFrameOutputNode for the audio thread. _users counts the
    // tap itself and a quantum being read; whoever brings it to zero releases
    // the pointer, so a quantum still in flight while the tap is taken down
    // never reads a released node. (The projection's own wrapper holds the
    // node too, so that release is never the node's last.)
    private readonly nint _source;
    private int _users = 1;
    private int _disposed;
    private volatile bool _closed;
    private volatile bool _sinkFailed;
    private int _channels;
    private int _sampleRate;

    private AudioGraphTap(AudioGraph graph, AudioSubmixNode bus, AudioFrameOutputNode node, nint source, ILocalAudioSink sink)
    {
        _graph = graph;
        _bus = bus;
        _node = node;
        _source = source;
        _sink = sink;
        _onQuantumStarted = OnQuantumStarted;
    }

    /// <summary>
    /// Hangs a tap on <paramref name="bus"/> that feeds <paramref name="sink"/>,
    /// or returns null when the node will not give its frames. Call off the
    /// audio thread, never from a quantum handler (the graph refuses changes there).
    /// </summary>
    public static AudioGraphTap? Attach(AudioGraph graph, AudioSubmixNode bus, ILocalAudioSink sink)
    {
        var node = graph.CreateFrameOutputNode();
        if (!AudioFrameSamples.TryGetSource(node, out var source))
        {
            node.Dispose();
            return null;
        }

        var tap = new AudioGraphTap(graph, bus, node, source, sink);
        try
        {
            // The graph's own format: always 32-bit float, interleaved, at the graph's rate and the device's channel count.
            var format = node.EncodingProperties;
            tap._channels = (int)format.ChannelCount;
            tap._sampleRate = (int)format.SampleRate;

            sink.Start(graph.LatencyInSamples);
            bus.AddOutgoingConnection(node);
            graph.QuantumStarted += tap._onQuantumStarted;
            node.Start();
        }
        catch
        {
            tap.Dispose();
            throw;
        }

        return tap;
    }

    /// <summary>Unhooks and closes the node. Safe while a quantum is in flight: neither side waits for the other.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _closed = true;
        Try(() => _graph.QuantumStarted -= _onQuantumStarted);
        Try(() => _bus.RemoveOutgoingConnection(_node));
        Try(_node.Stop);
        Try(_node.Dispose);
        Leave();
    }

    /// <summary>
    /// The audio thread, at the start of every quantum (about 10 ms): reads
    /// what the node gathered since the last call. Never locks, waits,
    /// allocates, raises an event, throws or touches the graph's structure.
    /// </summary>
    private void OnQuantumStarted(AudioGraph sender, object args)
    {
        if (_closed || !TryEnter())
        {
            return;
        }

        try
        {
            // Taken every quantum even when the sink has failed: a node that is not
            // read keeps everything it gathers, so its memory would only grow.
            using var frame = AudioFrameSamples.Take(_source);
            if (!_sinkFailed && !frame.Samples.IsEmpty)
            {
                _sink.Write(frame.Samples, _channels, _sampleRate);
            }
        }
        catch (Exception)
        {
            // A mistake in the analysis: the visualiser stops, the music never does.
            _sinkFailed = true;
        }
        finally
        {
            Leave();
        }
    }

    private bool TryEnter()
    {
        var users = Volatile.Read(ref _users);
        while (users > 0)
        {
            var seen = Interlocked.CompareExchange(ref _users, users + 1, users);
            if (seen == users)
            {
                return true;
            }

            users = seen;
        }

        return false;
    }

    private void Leave()
    {
        if (Interlocked.Decrement(ref _users) == 0)
        {
            Marshal.Release(_source);
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            // A graph that is going away.
        }
    }
}

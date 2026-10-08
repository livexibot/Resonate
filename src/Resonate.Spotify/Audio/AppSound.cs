namespace Resonate.Spotify.Audio;

/// <summary>
/// Receives sound on a capture thread, for the Home stage's visualizer.
/// <see cref="Write"/> must not block, lock or throw.
/// </summary>
public interface ISoundSink
{
    /// <summary>
    /// About 10 ms of sound: interleaved 32-bit float samples,
    /// <paramref name="channels"/> per frame. Empty when nothing arrived for
    /// a while (the program played nothing), so the sink can tell silence
    /// from a capture that stopped.
    /// </summary>
    void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate);
}

/// <summary>
/// Hears what one program on this PC plays (and the programs it started),
/// for the visualizer only: Windows' process loopback, which copies the
/// program's sound as Windows mixes it. Playback is not touched: the
/// program plays exactly as before, Lossless included. Nothing is kept,
/// recorded or sent anywhere; the sound is turned into bar heights and
/// dropped.
/// </summary>
public interface IAppSoundCapture
{
    /// <summary>This PC's Windows can do it (Windows 10 build 20348, Windows 11, or later).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Raised on any thread when the capture <see cref="Start"/> began could
    /// not start or stopped by itself (Windows refused, or the program went away).
    /// </summary>
    event EventHandler? Failed;

    /// <summary>
    /// Starts hearing <paramref name="processId"/> and the processes it
    /// started, handing the sound to <paramref name="sink"/> on a capture
    /// thread, and stops whatever was heard before. Returns at once.
    /// </summary>
    void Start(int processId, ISoundSink sink);

    /// <summary>Stops hearing. Returns at once; the sink may get one more write.</summary>
    void Stop();
}

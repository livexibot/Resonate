namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Hears the local files player's sound while a visualiser shows. Only
/// Resonate's own audio graph (the user's music files) ever feeds it;
/// Spotify's audio never reaches it. <see cref="Start"/> comes from a
/// background thread when the engine connects the sink to a graph;
/// <see cref="Write"/> comes on the audio thread once per quantum (about
/// 10 ms) and must not block, lock, allocate or throw.
/// </summary>
public interface ILocalAudioSink
{
    /// <summary>A new stream of sound begins (the first song, or a graph rebuilt for another sound device).</summary>
    /// <param name="latencySamples">About how far the speakers are behind what <see cref="Write"/> receives, in samples.</param>
    void Start(int latencySamples);

    /// <summary>One quantum: interleaved 32-bit float samples, <paramref name="channels"/> per frame.</summary>
    void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate);
}

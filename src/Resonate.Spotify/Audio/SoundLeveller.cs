namespace Resonate.Spotify.Audio;

/// <summary>
/// Evens out how loud heard sound is, so the visualizer's bars move as much
/// at a low volume as at full volume (Windows may hand over a program's
/// sound after its volume in the mixer, which Resonate's slider sets). A
/// slow automatic gain of 1 to <see cref="MaxGain"/> times brings the
/// loudest recent samples towards <see cref="Target"/> of full scale. It
/// reacts at once to louder sound and lets go over seconds, and never makes
/// sound quieter. Works on a copy for the bars only; one thread calls it.
/// </summary>
public sealed class SoundLeveller
{
    /// <summary>Where the loudest recent sample is brought: about where music at full volume peaks.</summary>
    public const float Target = 0.8f;

    /// <summary>The most it lifts sound: 8 times (18 dB), so a volume of about 10 % still moves the bars fully.</summary>
    public const float MaxGain = 8f;

    /// <summary>How fast the remembered peak fades once the sound is quieter (time constant, in seconds).</summary>
    public const float ReleaseSeconds = 3f;

    private float _peak;

    /// <summary>The gain the last block got.</summary>
    public float Gain { get; private set; } = 1f;

    /// <summary>Forgets the sound so far (a new program, or after a pause).</summary>
    public void Reset()
    {
        _peak = 0;
        Gain = 1f;
    }

    /// <summary>
    /// Writes <paramref name="input"/>, levelled, to <paramref name="output"/>
    /// (at least as long). <paramref name="channels"/> and
    /// <paramref name="sampleRate"/> tell how much time the block covers.
    /// </summary>
    public void Apply(ReadOnlySpan<float> input, Span<float> output, int channels, int sampleRate)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output is shorter than the input.", nameof(output));
        }

        if (input.IsEmpty)
        {
            return;
        }

        var loudest = 0f;
        foreach (var sample in input)
        {
            var size = MathF.Abs(sample);
            if (size > loudest)
            {
                loudest = size;
            }
        }

        // Not a number (a broken sample) never sticks in the remembered peak.
        if (float.IsFinite(loudest))
        {
            var seconds = channels > 0 && sampleRate > 0 ? input.Length / channels / (float)sampleRate : 0.01f;
            _peak = Math.Max(loudest, _peak * MathF.Exp(-seconds / ReleaseSeconds));
        }

        Gain = _peak <= Target / MaxGain ? MaxGain : Math.Clamp(Target / _peak, 1f, MaxGain);
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = input[i] * Gain;
        }
    }
}

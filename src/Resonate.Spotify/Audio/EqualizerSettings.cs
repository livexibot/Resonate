namespace Resonate.Spotify.Audio;

/// <summary>
/// A ten-band graphic equalizer: one gain per octave band, plus a preamp to
/// make room for boosts. Immutable; every change is a new instance.
/// </summary>
public sealed record EqualizerSettings
{
    /// <summary>The centre of each band, in hertz (the usual ten octave bands).</summary>
    public static readonly IReadOnlyList<double> Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>How far a band can be raised or lowered, in decibels.</summary>
    public const double MaxGainDb = 12;

    public static readonly EqualizerSettings Flat = new();

    public bool Enabled { get; init; }

    /// <summary>The preset these gains came from ("Flat", "Bass booster"...), or null once the user moved a band.</summary>
    public string? PresetName { get; init; } = "Flat";

    /// <summary>Applied before the bands, in decibels (usually zero or negative).</summary>
    public double PreampDb { get; init; }

    /// <summary>One gain per <see cref="Frequencies"/> entry, in decibels.</summary>
    public IReadOnlyList<double> GainsDb { get; init; } = new double[10];

    /// <summary>Nothing would change the sound.</summary>
    public bool IsNeutral => !Enabled || (Math.Abs(PreampDb) < 0.01 && GainsDb.All(g => Math.Abs(g) < 0.01));

    /// <summary>A copy with one band changed (clamped to ±<see cref="MaxGainDb"/>); the preset name is dropped.</summary>
    public EqualizerSettings WithGain(int band, double gainDb)
    {
        var gains = GainsDb.ToArray();
        if (band < 0 || band >= gains.Length)
        {
            return this;
        }

        gains[band] = Math.Clamp(gainDb, -MaxGainDb, MaxGainDb);
        return this with { GainsDb = gains, PresetName = null };
    }

    public bool Equals(EqualizerSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && PresetName == other.PresetName
        && PreampDb.Equals(other.PreampDb)
        && GainsDb.SequenceEqual(other.GainsDb);

    public override int GetHashCode() => HashCode.Combine(Enabled, PresetName, PreampDb, GainsDb.Count > 0 ? GainsDb[0] : 0);
}

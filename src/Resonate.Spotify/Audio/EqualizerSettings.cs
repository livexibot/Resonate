namespace Resonate.Spotify.Audio;

/// <summary>The kind of filter a band uses.</summary>
public enum EqualizerFilter
{
    /// <summary>Raises or lowers everything below the frequency.</summary>
    LowShelf,

    /// <summary>Raises or lowers a bell around the frequency.</summary>
    Peak,

    /// <summary>Raises or lowers everything above the frequency.</summary>
    HighShelf,
}

/// <summary>One slider of the equalizer.</summary>
public sealed record EqualizerBand(double FrequencyHz, EqualizerFilter Filter)
{
    /// <summary>"60 Hz", "2.4 kHz".</summary>
    public string Label => FrequencyHz >= 1000
        ? (FrequencyHz / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz"
        : FrequencyHz.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " Hz";
}

/// <summary>
/// The equalizer, with the same six bands as the Spotify app's own (a low
/// shelf, four peaks and a high shelf, ±12 dB), so one set of sliders
/// means the same for Spotify's songs (applied by Spotify) and for local
/// files (applied by Resonate). Immutable; every change is a new instance.
/// </summary>
public sealed record EqualizerSettings
{
    public const double MaxGainDb = 12;

    /// <summary>Spotify's bands: low shelf 60 Hz, peaks at 150 Hz, 400 Hz, 1 kHz and 2.4 kHz, high shelf 15 kHz.</summary>
    public static readonly IReadOnlyList<EqualizerBand> Bands =
    [
        new(60, EqualizerFilter.LowShelf),
        new(150, EqualizerFilter.Peak),
        new(400, EqualizerFilter.Peak),
        new(1000, EqualizerFilter.Peak),
        new(2400, EqualizerFilter.Peak),
        new(15000, EqualizerFilter.HighShelf),
    ];

    public static readonly EqualizerSettings Flat = new();

    public bool Enabled { get; init; }

    /// <summary>One gain per <see cref="Bands"/> entry, in decibels.</summary>
    public IReadOnlyList<double> GainsDb { get; init; } = new double[6];

    /// <summary>Nothing would change the sound.</summary>
    public bool IsNeutral => !Enabled || GainsDb.All(g => Math.Abs(g) < 0.01);

    /// <summary>The preset these gains match, or null for the user's own setting.</summary>
    public EqualizerPreset? Preset => EqualizerPresets.Match(GainsDb);

    /// <summary>
    /// How much to lower the sound before the bands so boosts cannot clip:
    /// minus the largest boost, or zero when nothing is raised.
    /// </summary>
    public double PreampDb => -Math.Max(0, GainsDb.Count == 0 ? 0 : GainsDb.Max());

    /// <summary>A copy with one band changed (clamped to ±<see cref="MaxGainDb"/>).</summary>
    public EqualizerSettings WithGain(int band, double gainDb)
    {
        var gains = Normalized(GainsDb);
        if (band < 0 || band >= gains.Length)
        {
            return this;
        }

        gains[band] = Math.Clamp(gainDb, -MaxGainDb, MaxGainDb);
        return this with { GainsDb = gains };
    }

    /// <summary>A copy with a preset's gains.</summary>
    public EqualizerSettings WithPreset(EqualizerPreset preset) => this with { GainsDb = Normalized(preset.GainsDb) };

    public bool Equals(EqualizerSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && GainsDb.SequenceEqual(other.GainsDb);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        foreach (var gain in GainsDb)
        {
            hash.Add(gain);
        }

        return hash.ToHashCode();
    }

    /// <summary>Exactly one value per band, each within range.</summary>
    private static double[] Normalized(IReadOnlyList<double> gains)
    {
        var result = new double[Bands.Count];
        for (var i = 0; i < result.Length && i < gains.Count; i++)
        {
            result[i] = Math.Clamp(gains[i], -MaxGainDb, MaxGainDb);
        }

        return result;
    }
}

/// <summary>A named set of gains, in decibels for each of <see cref="EqualizerSettings.Bands"/>.</summary>
public sealed record EqualizerPreset(string Name, IReadOnlyList<double> GainsDb);

/// <summary>The Spotify app's own presets, with its values, so a preset sounds the same in both.</summary>
public static class EqualizerPresets
{
    public static readonly IReadOnlyList<EqualizerPreset> All =
    [
        new("Flat", [0, 0, 0, 0, 0, 0]),
        new("Acoustic", [4.9, 3.95, 2.15, 1.75, 3.5, 2.15]),
        new("Bass booster", [4.25, 3.5, 1.25, 0, 0, 0]),
        new("Bass reducer", [-4.25, -3.5, -1.25, 0, 0, 0]),
        new("Classical", [3.75, 3, -1.5, -1.5, 0, 3.75]),
        new("Dance", [6.55, 4.99, 1.92, 3.65, 5.15, 0]),
        new("Electronic", [3.8, 1.2, -2.15, 2.25, 0.85, 4.8]),
        new("Hip hop", [4.25, 1.5, -1, -1, 1.5, 3]),
        new("Jazz", [3, 1.5, -1.5, -1.5, 0, 3.75]),
        new("Latin", [3, 0, -1.5, -1.5, -1.5, 4.5]),
        new("Loudness", [4, 0, -2, 0, -1, 1]),
        new("Lounge", [-1.5, -0.5, 4, 2.5, 0, 1]),
        new("Piano", [2, 0, 3, 1.5, 3.5, 3.5]),
        new("Pop", [-1, 0, 4, 4, 2, -1.5]),
        new("R&B", [6.92, 5.65, -2.19, -1.5, 2.32, 3.75]),
        new("Rock", [4, 3, -0.5, -1, 0.5, 4.5]),
        new("Small speakers", [4.25, 3.5, 1.25, 0, -1.25, -4.25]),
        new("Spoken word", [-0.47, 0, 3.46, 4.61, 4.84, 0]),
        new("Treble booster", [0, 0, 0, 1.25, 2.5, 5.5]),
        new("Treble reducer", [0, 0, 0, -1.25, -2.5, -5.5]),
        new("Vocal booster", [-3, -3, 3.75, 3.75, 3, -1.5]),
    ];

    /// <summary>The preset with these gains (within a twentieth of a decibel, as Spotify stores them), or null.</summary>
    public static EqualizerPreset? Match(IReadOnlyList<double> gainsDb) =>
        All.FirstOrDefault(p => p.GainsDb.Count == gainsDb.Count && p.GainsDb.Zip(gainsDb).All(x => Math.Abs(x.First - x.Second) < 0.05));
}

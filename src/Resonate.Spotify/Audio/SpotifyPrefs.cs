using System.Globalization;

namespace Resonate.Spotify.Audio;

/// <summary>
/// The few settings Resonate reads from (and, for the equalizer, writes to)
/// the Spotify app's per-account "prefs" file: plain "key=value" lines.
/// Only these keys are ever looked at; the rest of the file is kept as it is.
/// Spotify reads the file when it starts and rewrites it when it quits, so a
/// change only counts if it is written while Spotify is closed.
/// </summary>
public static class SpotifyPrefs
{
    public const string EqualizerEnabledKey = "audio.equalizer_v2";

    /// <summary>"5" means Lossless (4 is Very high), as measured on Spotify's Windows app in 2026.</summary>
    public const string StreamingQualityKey = "audio.play_bitrate_enumeration";

    public const int LosslessQuality = 5;

    /// <summary>One key per band of <see cref="EqualizerSettings.Bands"/>, in the same order.</summary>
    public static readonly IReadOnlyList<string> EqualizerGainKeys =
    [
        "audio.equalizer.low_shelf_gain_v2",
        "audio.equalizer.low_peak_gain_v2",
        "audio.equalizer.low_mid_peak_gain_v2",
        "audio.equalizer.high_mid_peak_gain_v2",
        "audio.equalizer.high_peak_gain_v2",
        "audio.equalizer.high_shelf_gain_v2",
    ];

    /// <summary>Spotify stores a gain as a whole number where int.MaxValue is +12 dB.</summary>
    private const double Scale = int.MaxValue / EqualizerSettings.MaxGainDb;

    /// <summary>Reads "key=value" lines; values keep their quotes, if any.</summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return values;
    }

    /// <summary>Spotify's equalizer as the file has it (a missing key means off, or 0 dB).</summary>
    public static EqualizerSettings ReadEqualizer(IReadOnlyDictionary<string, string> prefs)
    {
        var gains = new double[EqualizerGainKeys.Count];
        for (var i = 0; i < gains.Length; i++)
        {
            if (prefs.TryGetValue(EqualizerGainKeys[i], out var raw)
                && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                gains[i] = Math.Round(Math.Clamp(value / Scale, -EqualizerSettings.MaxGainDb, EqualizerSettings.MaxGainDb), 2);
            }
        }

        var enabled = prefs.TryGetValue(EqualizerEnabledKey, out var on) && on == "true";
        return new EqualizerSettings { Enabled = enabled, GainsDb = gains };
    }

    /// <summary>
    /// Whether Spotify streams in Lossless: true or false when the file says,
    /// null when it does not (Spotify leaves out settings still at their default).
    /// </summary>
    public static bool? IsLossless(IReadOnlyDictionary<string, string> prefs) =>
        prefs.TryGetValue(StreamingQualityKey, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality)
            ? quality == LosslessQuality
            : null;

    /// <summary>
    /// The file with the equalizer's seven keys set to <paramref name="settings"/>
    /// and every other line unchanged (keys that were missing are added at the end).
    /// </summary>
    public static string WriteEqualizer(string text, EqualizerSettings settings)
    {
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EqualizerEnabledKey] = settings.Enabled ? "true" : "false",
        };
        for (var i = 0; i < EqualizerGainKeys.Count; i++)
        {
            var gain = i < settings.GainsDb.Count ? settings.GainsDb[i] : 0;
            wanted[EqualizerGainKeys[i]] = ToStored(gain).ToString(CultureInfo.InvariantCulture);
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var separator = lines[i].IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && wanted.Remove(lines[i][..separator].Trim(), out var value))
            {
                lines[i] = lines[i][..separator] + "=" + value;
            }
        }

        foreach (var (key, value) in wanted)
        {
            lines.Add(key + "=" + value);
        }

        return string.Join(newline, lines) + newline;
    }

    internal static long ToStored(double gainDb) =>
        (long)Math.Round(Math.Clamp(gainDb, -EqualizerSettings.MaxGainDb, EqualizerSettings.MaxGainDb) * Scale);
}

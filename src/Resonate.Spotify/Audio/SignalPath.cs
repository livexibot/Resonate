using System.Globalization;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Audio;

/// <summary>What Signal path concludes about the sound on its way to the user's ears.</summary>
public enum SignalVerdict
{
    /// <summary>Resonate can not see enough to say (grey).</summary>
    CantTell,

    /// <summary>Lossless all the way to the output (green).</summary>
    Lossless,

    /// <summary>A lossless stream that is changed on the way: volume, equalizer or a rate conversion (amber).</summary>
    Adjusted,

    /// <summary>A lossy setting, file or output somewhere on the way (red).</summary>
    NotLossless,
}

/// <summary>How one step of the chain stands.</summary>
public enum SignalStepState
{
    Good,
    Adjusted,
    Problem,
    Unknown,
}

/// <summary>Where a step's fix is made.</summary>
public enum SignalFix
{
    None,

    /// <summary>The Spotify app's own settings (its audio quality and normalisation).</summary>
    SpotifySettings,

    /// <summary>Resonate's equalizer in Settings, which is also Spotify's.</summary>
    Equalizer,

    /// <summary>Windows' sound settings (the output and its format).</summary>
    SoundSettings,
}

/// <summary>One step from the music to the output, with a short tip and where to fix it when it needs one.</summary>
public sealed record SignalStep(string Name, string Value, SignalStepState State, string? Tip = null, SignalFix Fix = SignalFix.None);

/// <summary>The default sound output as Windows reports it.</summary>
/// <param name="Name">Its name, such as "Speakers (Realtek(R) Audio)".</param>
/// <param name="SampleRate">The shared-mode format's rate in Hz (what Windows mixes at); 0 when unknown.</param>
/// <param name="BitsPerSample">The shared-mode format's depth; 0 when unknown.</param>
/// <param name="IsBluetooth">Whether it is a Bluetooth device, which compresses the sound again.</param>
public sealed record AudioOutputInfo(string Name, int SampleRate, int BitsPerSample, bool IsBluetooth);

/// <summary>A verdict, a sentence about it, and the chain that led to it.</summary>
public sealed record SignalReport(SignalVerdict Verdict, string Summary, IReadOnlyList<SignalStep> Steps)
{
    /// <summary>The pill's word: short, in capitals.</summary>
    public string Badge => Verdict switch
    {
        SignalVerdict.Lossless => "LOSSLESS",
        SignalVerdict.Adjusted => "ADJUSTED",
        SignalVerdict.NotLossless => "NOT LOSSLESS",
        _ => "CAN'T TELL",
    };

    /// <summary>The verdict as a heading.</summary>
    public string Title => Verdict switch
    {
        SignalVerdict.Lossless => "Lossless",
        SignalVerdict.Adjusted => "Lossless, adjusted",
        SignalVerdict.NotLossless => "Not lossless",
        _ => "Can't tell",
    };

    /// <summary>Whether <paramref name="other"/> says exactly the same, so nothing needs redrawing.</summary>
    public bool SameAs(SignalReport? other) =>
        other is not null && other.Verdict == Verdict && other.Summary == Summary && other.Steps.SequenceEqual(Steps);
}

/// <summary>What Signal path knows about Spotify's side.</summary>
public sealed record SpotifySignal
{
    /// <summary>False with "Spotify Web API only": the Spotify app's settings and the mixer are left alone, so nothing can be checked.</summary>
    public bool UsesSpotifyApp { get; init; } = true;

    /// <summary>The Spotify Connect device that plays, when it is not this computer.</summary>
    public string? OtherDevice { get; init; }

    /// <summary>Spotify's per-account settings file, or null when it was not found or read.</summary>
    public SpotifyAudioPrefs? Prefs { get; init; }

    /// <summary>Spotify's volume as Resonate's slider shows it (its volume in the Windows mixer), 0 to 1.</summary>
    public double Volume { get; init; } = 1;

    /// <summary>The default output, or null when Windows did not say.</summary>
    public AudioOutputInfo? Output { get; init; }
}

/// <summary>What Signal path knows about a music file that Resonate's own player plays.</summary>
public sealed record LocalSignal
{
    public LocalAudioFormat? Format { get; init; }

    /// <summary>Resonate's equalizer, which the local files player applies.</summary>
    public bool EqualizerOn { get; init; }

    /// <summary>Resonate's volume for local files, 0 to 1.</summary>
    public double Volume { get; init; } = 1;

    public AudioOutputInfo? Output { get; init; }
}

/// <summary>
/// Signal path, a built-in plugin: whether what plays reaches the output
/// losslessly, and what to change if not. A best estimate from Spotify's
/// settings file and Windows; Resonate never sees Spotify's stream (the
/// Home stage's visualizer only hears it after Windows mixed it), so it
/// can not confirm it. Spotify Lossless is up to 24-bit, 44.1 kHz FLAC.
/// </summary>
public static class SignalPath
{
    /// <summary>Spotify Lossless's sample rate.</summary>
    public const int SpotifyRate = 44_100;

    /// <summary>The local files player's highest rate (its equalizer works up to 48 kHz; see AudioGraphEngine).</summary>
    public const int LocalPlayerMaxRate = 48_000;

    private const string QualityTip = "In Spotify, Settings, Audio quality: choose Lossless.";
    private const string VolumeTip = "Below 100 %, the sound is turned down digitally. Set it to 100 % and use Windows' or your speakers' volume instead.";
    private const string BluetoothTip = "Bluetooth compresses the sound again on the way. Use a wired or USB output to hear lossless.";

    /// <summary>The device that plays when it is another one than this computer, else null.</summary>
    public static string? OtherDevice(string? deviceName, string machineName) =>
        deviceName is { Length: > 0 } name && !LocalDeviceResolver.NameMatches(name, machineName) ? name : null;

    /// <summary>Spotify's songs: the Spotify app's settings, its volume, the Windows mixer and the output.</summary>
    public static SignalReport ForSpotify(SpotifySignal input)
    {
        if (!input.UsesSpotifyApp)
        {
            return new(SignalVerdict.CantTell, "With Spotify Web API only, Resonate leaves the Spotify app alone, so it can't check its settings.", []);
        }

        if (input.OtherDevice is { } device)
        {
            return new(SignalVerdict.CantTell, $"Spotify plays on {device}. Resonate can only check this computer.", []);
        }

        var prefs = input.Prefs;
        var quality = QualityStep(prefs);
        var equalizer = prefs is null
            ? new SignalStep("Spotify's equalizer", "Unknown", SignalStepState.Unknown)
            : prefs.Equalizer.Enabled
                ? new SignalStep("Spotify's equalizer", "On", SignalStepState.Adjusted, "The equalizer reshapes the sound. Turn it off to hear the stream untouched.", SignalFix.Equalizer)
                : new SignalStep("Spotify's equalizer", "Off", SignalStepState.Good);
        var normalize = prefs?.Normalize switch
        {
            true => new SignalStep("Normalize volume", "On", SignalStepState.Adjusted, "Spotify evens out loudness by turning songs down. In Spotify, Settings, Audio quality: turn off Normalize volume.", SignalFix.SpotifySettings),
            false => new SignalStep("Normalize volume", "Off", SignalStepState.Good),
            null => new SignalStep("Normalize volume", "Unknown", SignalStepState.Unknown),
        };
        var volume = VolumeStep("Spotify's volume", input.Volume);
        var mixer = MixerStep(input.Output, SpotifyRate, "Spotify Lossless is 44.1 kHz. In Windows' sound settings, set this output's format to 44.1 kHz.");
        var output = OutputStep(input.Output);
        SignalStep[] steps = [quality, equalizer, normalize, volume, mixer, output];

        if (quality.State == SignalStepState.Problem)
        {
            return new(SignalVerdict.NotLossless, "Spotify is set below Lossless.", steps);
        }

        if (output.State == SignalStepState.Problem)
        {
            return new(SignalVerdict.NotLossless, "Lossless from Spotify, but Bluetooth compresses it again.", steps);
        }

        if (quality.State == SignalStepState.Unknown)
        {
            return new(SignalVerdict.CantTell, prefs is null ? "Spotify's settings were not found on this computer." : "Spotify's settings don't say whether Lossless is on.", steps);
        }

        if (output.State == SignalStepState.Unknown)
        {
            return new(SignalVerdict.CantTell, "Windows did not say which output plays.", steps);
        }

        return Finish(steps, "Lossless from Spotify to your output.");
    }

    /// <summary>A music file played by Resonate's own player, through Windows' mixer to the output.</summary>
    public static SignalReport ForLocalFile(LocalSignal input)
    {
        var format = input.Format;
        var file = format switch
        {
            null => new SignalStep("File", "Unknown format", SignalStepState.Unknown),
            { IsLossless: true } => new SignalStep("File", Describe(format), SignalStepState.Good),
            { IsLossless: false } => new SignalStep("File", Describe(format), SignalStepState.Problem, "The file itself is compressed with loss. Nothing later can bring that back."),
            _ => new SignalStep("File", format.Codec + ", lossless or not", SignalStepState.Unknown),
        };

        // The player runs at the output's rate, pinned to 48 kHz when the output runs higher.
        var playerRate = input.Output is { SampleRate: > 0 } o ? Math.Min(o.SampleRate, LocalPlayerMaxRate) : 0;
        var player = playerRate == 0
            ? new SignalStep("Resonate's player", "Unknown rate", SignalStepState.Unknown)
            : format is { SampleRate: > 0 } && format.SampleRate != playerRate
                ? new SignalStep(
                    "Resonate's player",
                    $"Converts {Khz(format.SampleRate)} to {Khz(playerRate)} kHz",
                    SignalStepState.Adjusted,
                    "Resonate's player runs at the output's rate, at most 48 kHz, so it converts other rates.",
                    SignalFix.SoundSettings)
                : new SignalStep("Resonate's player", $"Plays at {Khz(playerRate)} kHz", SignalStepState.Good);
        var equalizer = input.EqualizerOn
            ? new SignalStep("Resonate's equalizer", "On", SignalStepState.Adjusted, "The equalizer reshapes the sound. Turn it off to hear the file untouched.", SignalFix.Equalizer)
            : new SignalStep("Resonate's equalizer", "Off", SignalStepState.Good);
        var volume = VolumeStep("Resonate's volume", input.Volume);
        var mixer = MixerStep(input.Output, playerRate, "Resonate's player stops at 48 kHz. In Windows' sound settings, set this output's format to 48 kHz or lower.");
        var output = OutputStep(input.Output);
        SignalStep[] steps = [file, player, equalizer, volume, mixer, output];

        if (file.State == SignalStepState.Problem)
        {
            return new(SignalVerdict.NotLossless, $"{format!.Codec} is a lossy format.", steps);
        }

        if (output.State == SignalStepState.Problem)
        {
            return new(SignalVerdict.NotLossless, "A lossless file, but Bluetooth compresses it again.", steps);
        }

        if (file.State == SignalStepState.Unknown)
        {
            return new(SignalVerdict.CantTell, "Resonate can't tell how this file is encoded.", steps);
        }

        if (output.State == SignalStepState.Unknown)
        {
            return new(SignalVerdict.CantTell, "Windows did not say which output plays.", steps);
        }

        return Finish(steps, "A lossless file, untouched to your output.");
    }

    /// <summary>"44.1" for 44,100 Hz, "48" for 48,000 Hz.</summary>
    public static string Khz(int hz) => (hz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>"FLAC, 24-bit 96 kHz", "MP3, 44.1 kHz" or just "WAV".</summary>
    public static string Describe(LocalAudioFormat format)
    {
        var bits = format.BitsPerSample > 0 ? $"{format.BitsPerSample}-bit " : string.Empty;
        return format.SampleRate > 0 ? $"{format.Codec}, {bits}{Khz(format.SampleRate)} kHz" : format.Codec;
    }

    private static SignalReport Finish(SignalStep[] steps, string lossless)
    {
        var adjusted = steps.Where(s => s.State == SignalStepState.Adjusted).ToList();
        if (adjusted.Count == 0)
        {
            return new(SignalVerdict.Lossless, lossless, steps);
        }

        var reasons = adjusted.Select(s => s.Name switch
        {
            "Spotify's volume" or "Resonate's volume" => "the volume is below 100 %",
            "Spotify's equalizer" or "Resonate's equalizer" => "the equalizer is on",
            "Normalize volume" => "normalization is on",
            "Resonate's player" => "Resonate " + LowerFirst(s.Value),
            _ => "Windows " + LowerFirst(s.Value),
        }).ToList();
        return new(SignalVerdict.Adjusted, "Lossless, but " + Join(reasons) + ".", steps);
    }

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string Join(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Join(", ", parts[..^1]) + " and " + parts[^1],
    };

    private static SignalStep QualityStep(SpotifyAudioPrefs? prefs) => prefs switch
    {
        null => new("Spotify quality", "Settings not found", SignalStepState.Unknown, "Resonate can't find the Spotify app's settings on this computer.", SignalFix.SpotifySettings),
        { Quality: null } => new("Spotify quality", "Not set", SignalStepState.Unknown, "Spotify's settings don't name a quality, which usually means its default. " + QualityTip, SignalFix.SpotifySettings),
        { Quality: SpotifyPrefs.LosslessQuality } => new("Spotify quality", "Lossless", SignalStepState.Good),
        { Quality: 4 } => new("Spotify quality", "Very high", SignalStepState.Problem, QualityTip, SignalFix.SpotifySettings),
        { Quality: >= 0 and < SpotifyPrefs.LosslessQuality } => new("Spotify quality", "Below Lossless", SignalStepState.Problem, QualityTip, SignalFix.SpotifySettings),
        _ => new("Spotify quality", "Unknown setting", SignalStepState.Unknown, "This Spotify version stores a quality Resonate doesn't know yet.", SignalFix.SpotifySettings),
    };

    private static SignalStep VolumeStep(string name, double volume)
    {
        var percent = (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);
        return percent >= 100
            ? new(name, "100 %", SignalStepState.Good)
            : new(name, $"{percent} %", SignalStepState.Adjusted, VolumeTip);
    }

    private static SignalStep MixerStep(AudioOutputInfo? output, int inputRate, string tip) =>
        output is not { SampleRate: > 0 } || inputRate <= 0
            ? new("Windows mixer", "Unknown rate", SignalStepState.Unknown)
            : output.SampleRate == inputRate
                ? new("Windows mixer", $"{Khz(inputRate)} kHz, no conversion", SignalStepState.Good)
                : new("Windows mixer", $"Converts {Khz(inputRate)} to {Khz(output.SampleRate)} kHz", SignalStepState.Adjusted, tip, SignalFix.SoundSettings);

    private static SignalStep OutputStep(AudioOutputInfo? output)
    {
        if (output is null)
        {
            return new("Output", "Not found", SignalStepState.Unknown, "Windows did not report a sound output.", SignalFix.SoundSettings);
        }

        if (output.IsBluetooth)
        {
            return new("Output", output.Name + ", Bluetooth", SignalStepState.Problem, BluetoothTip, SignalFix.SoundSettings);
        }

        var bits = output.BitsPerSample > 0 ? $"{output.BitsPerSample}-bit " : string.Empty;
        var rate = output.SampleRate > 0 ? $"{Khz(output.SampleRate)} kHz" : string.Empty;
        var format = (bits + rate).Trim();
        return new("Output", format.Length > 0 ? $"{output.Name}, {format}" : output.Name, SignalStepState.Good);
    }
}

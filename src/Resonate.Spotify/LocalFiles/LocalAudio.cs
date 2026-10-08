using Resonate.Spotify.Audio;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.LocalFiles;

/// <param name="Path">The file that played to its end.</param>
/// <param name="NextPath">The file the engine started at once, without a gap (the one given to <see cref="ILocalAudioEngine.SetNext"/>), or null when it stopped.</param>
public sealed record LocalTrackEnded(string Path, string? NextPath);

/// <summary>
/// Plays one music file at a time for <see cref="LocalPlayer"/>, which keeps
/// the queue and what the interface shows. On Windows this is AudioGraph
/// (Resonate.Windows); tests use a fake. Calls come one at a time from a
/// background thread; events may arrive on any thread.
/// </summary>
public interface ILocalAudioEngine : IDisposable
{
    /// <summary>A file played to its end.</summary>
    event EventHandler<LocalTrackEnded>? TrackEnded;

    /// <summary>Playback stopped and could not be restarted (the sound device is gone); a sentence for the user.</summary>
    event EventHandler<string>? Failed;

    /// <summary>Where the open file is now (zero when none is open).</summary>
    TimeSpan Position { get; }

    /// <summary>How long the open file is (zero when none is open, or unknown); after a song ended without a gap, the length of the one that followed.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Opens <paramref name="path"/> at <paramref name="position"/>, playing or
    /// paused, in place of what was open, and returns its length (zero when
    /// unknown). Throws <see cref="LocalAudioException"/> with a sentence for
    /// the user when the file can not be played; what was open is then
    /// closed all the same (and the next file forgotten), so nothing plays.
    /// </summary>
    Task<TimeSpan> OpenAsync(string path, TimeSpan position, bool play, CancellationToken cancellationToken);

    Task PlayAsync();

    Task PauseAsync();

    Task SeekAsync(TimeSpan position);

    /// <summary>The file to start the moment the open one ends (null for none); the engine prepares it shortly before.</summary>
    void SetNext(string? path);

    /// <summary>Plays the open file again and again, without a gap ("repeat one").</summary>
    void SetLooping(bool looping);

    /// <summary>From 0 to 1, as the volume slider shows it.</summary>
    void SetVolume(double volume);

    /// <summary>Null, or switched off, plays the files unchanged.</summary>
    void SetEqualizer(EqualizerSettings? settings);
}

/// <summary>A file the engine can not play, with a sentence that says why.</summary>
public sealed class LocalAudioException : Exception
{
    public LocalAudioException()
    {
    }

    public LocalAudioException(string message)
        : base(message)
    {
    }

    public LocalAudioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Nothing can play (no sound device), so skipping to the next file would not help.</summary>
    public bool IsDeviceProblem { get; init; }
}

public enum LocalControlButton
{
    Play,
    Pause,
    Stop,
    Next,
    Previous,
}

/// <summary>
/// Windows' media controls (the media keys, the volume flyout and the lock
/// screen) for the local files player. Events may arrive on any thread.
/// </summary>
public interface ILocalSystemControls : IDisposable
{
    event EventHandler<LocalControlButton>? ButtonPressed;

    event EventHandler<TimeSpan>? SeekRequested;

    event EventHandler<bool>? ShuffleRequested;

    event EventHandler<RepeatMode>? RepeatRequested;

    /// <summary>Shows <paramref name="state"/> (song, cover, playing or paused, timeline), or hides the controls when null.</summary>
    Task ShowAsync(PlayerState? state);
}

/// <summary>The volume slider's position as a gain: squared, so the slider's middle sounds about half as loud.</summary>
public static class LocalVolume
{
    public static double ToGain(double volume)
    {
        var v = Math.Clamp(volume, 0, 1);
        return v * v;
    }
}

/// <summary>One band of Windows' built-in equalizer effect: centre frequency, width in octaves and a linear gain.</summary>
public readonly record struct EqualizerEffectBand(double FrequencyHz, double Bandwidth, double Gain);

/// <summary>
/// Resonate's six equalizer bands (the Spotify app's: a 60 Hz low shelf,
/// peaks at 150 Hz, 400 Hz, 1 kHz and 2.4 kHz, a 15 kHz high shelf) as
/// settings for two of Windows' four-band equalizer effects (FXEQ). FXEQ only
/// has peaks, so the shelves become wide peaks, and the two spare bands sit
/// above 15 kHz at no gain (bands stay in rising order, which FXEQ wants).
/// </summary>
/// <param name="Bands">Eight bands: the first four for the first effect, the rest for the second.</param>
/// <param name="PreampGain">Applied before the bands, so boosts do not clip.</param>
/// <param name="IsActive">Anything would change the sound; when false the effects are switched off.</param>
/// <param name="NeedsLimiter">A band boosts, so a limiter guards against clipping.</param>
public sealed record LocalEqualizer(IReadOnlyList<EqualizerEffectBand> Bands, double PreampGain, bool IsActive, bool NeedsLimiter)
{
    /// <summary>FXEQ's gain range (about -18 to +18 dB).</summary>
    public const double MinGain = 0.126;

    public const double MaxGain = 7.94;

    /// <summary>The width of a peak with Q 1 (Spotify's), in octaves: 2·asinh(1/2Q)/ln 2.</summary>
    public static readonly double PeakBandwidth = 2 * Math.Asinh(0.5) / Math.Log(2);

    /// <summary>FXEQ's widest band, standing in for a shelf.</summary>
    public const double ShelfBandwidth = 2.0;

    public static readonly IReadOnlyList<double> SpareFrequencies = [17_000, 19_000];

    public static LocalEqualizer From(EqualizerSettings? settings)
    {
        var active = settings is { IsNeutral: false };
        var bands = new List<EqualizerEffectBand>(8);
        for (var i = 0; i < EqualizerSettings.Bands.Count; i++)
        {
            var band = EqualizerSettings.Bands[i];
            var gainDb = active && i < settings!.GainsDb.Count ? settings.GainsDb[i] : 0;
            var width = band.Filter == EqualizerFilter.Peak ? PeakBandwidth : ShelfBandwidth;
            bands.Add(new EqualizerEffectBand(band.FrequencyHz, width, ToGain(gainDb)));
        }

        foreach (var spare in SpareFrequencies)
        {
            bands.Add(new EqualizerEffectBand(spare, 1.0, 1.0));
        }

        var preamp = active ? Math.Pow(10, settings!.PreampDb / 20) : 1.0;
        var boosts = active && settings!.GainsDb.Any(g => g > 0.01);
        return new LocalEqualizer(bands, preamp, active, boosts);
    }

    /// <summary>Decibels as FXEQ's linear gain, kept within its range.</summary>
    public static double ToGain(double decibels) => Math.Clamp(Math.Pow(10, decibels / 20), MinGain, MaxGain);
}

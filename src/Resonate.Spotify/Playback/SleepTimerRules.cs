namespace Resonate.Spotify.Playback;

/// <summary>
/// The sleep timer's lengths, words and fade (a built-in plugin since
/// 10 October 2026, when the owner asked for every plugin in the app; it
/// was a downloaded JavaScript plugin before, and keeps its ID).
/// </summary>
public static class SleepTimerRules
{
    /// <summary>The volume falls over the last minute.</summary>
    public static readonly TimeSpan FadeLength = TimeSpan.FromSeconds(60);

    /// <summary>A fade step at most every two seconds, so the volume is not set every second.</summary>
    public static readonly TimeSpan FadeStep = TimeSpan.FromSeconds(2);

    /// <summary>The quietest the fade goes before the pause.</summary>
    public const double LowestFade = 0.05;

    /// <summary>The lengths always offered, in minutes.</summary>
    public static IReadOnlyList<int> Presets { get; } = [15, 30, 60];

    /// <summary>The lengths in the menu: the presets and the user's own, in order, once each.</summary>
    public static IReadOnlyList<int> Choices(int custom) =>
        custom <= 0 || Presets.Contains(custom) ? Presets : [.. Presets.Append(custom).Order()];

    /// <summary>"15 minutes", "1 hour", "2 hours".</summary>
    public static string MinutesText(int minutes) => minutes % 60 == 0
        ? minutes == 60 ? "1 hour" : $"{minutes / 60} hours"
        : $"{minutes} minutes";

    /// <summary>What is left as the menu shows it: whole minutes rounded up, then seconds ("0:09").</summary>
    public static string ClockText(TimeSpan left)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
        return seconds >= 60 ? $"{(seconds + 59) / 60} min" : $"0:{seconds:00}";
    }

    /// <summary>The share of the volume from before the fade, with <paramref name="left"/> to go; null while it is not fading yet.</summary>
    public static double? FadeShare(TimeSpan left) =>
        left > FadeLength ? null : Math.Max(LowestFade, left / FadeLength);
}

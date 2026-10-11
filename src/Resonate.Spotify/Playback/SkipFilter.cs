using System.Text.RegularExpressions;

namespace Resonate.Spotify.Playback;

/// <summary>What the Skip rules plugin skips: artists, versions (live, remix), songs and lengths.</summary>
public sealed record SkipFilterRules(
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Songs,
    int LongerThanMinutes,
    int ShorterThanSeconds);

/// <summary>
/// Skip rules (a built-in plugin since 10 October 2026; a downloaded
/// JavaScript plugin before, with the same ID and rules): which songs to
/// skip as soon as they start. Spotify does not tell apps in advance what
/// comes next, so a skipped song may be heard for a moment.
/// </summary>
public static partial class SkipFilter
{
    /// <summary>"Song (Live)", "Song [Remix]": what is in brackets.</summary>
    [GeneratedRegex(@"[(\[]([^)\]]*)[)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex Brackets();

    /// <summary>"Song - Sped Up": what follows a dash with spaces round it.</summary>
    [GeneratedRegex(@"\s[-–—]\s", RegexOptions.CultureInvariant)]
    private static partial Regex Dash();

    /// <summary>The artists of a song, as Spotify lists them ("A, B").</summary>
    public static IReadOnlyList<string> ArtistNames(string? artists) =>
        (artists ?? string.Empty).Split(", ").Select(name => name.Trim()).Where(name => name.Length > 0).ToList();

    /// <summary>How a song is kept in the list of songs to skip: "Title — Artists".</summary>
    public static string SongLine(string title, string? artists) => $"{title} — {artists}";

    /// <summary>The version parts of a title, lower case: what is in brackets or after a dash. "Live Forever" has none.</summary>
    public static IReadOnlyList<string> VersionParts(string title)
    {
        var parts = Brackets().Matches(title).Select(match => match.Groups[1].Value).ToList();
        var dash = Dash().Match(title);
        if (dash.Success)
        {
            parts.Add(title[(dash.Index + dash.Length)..]);
        }

        return parts.Select(Lower).ToList();
    }

    /// <summary>Whether <paramref name="word"/> stands on its own in <paramref name="text"/> ("live" in "live at wembley", not in "alive").</summary>
    public static bool HasWord(string text, string word)
    {
        if (word.Length == 0)
        {
            return false;
        }

        var pattern = @"(^|[^\p{L}\p{N}])" + Regex.Escape(word) + @"($|[^\p{L}\p{N}])";
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Why the song should be skipped ("artist", "song", "version: live", "longer than 10 min"), or null to let it play.</summary>
    public static string? ReasonToSkip(string? title, string? artists, TimeSpan duration, SkipFilterRules rules)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var names = ArtistNames(artists).Select(Lower).ToList();
        var all = Lower(artists);
        if (rules.Artists.Select(Lower).Any(blocked => blocked.Length > 0 && (names.Contains(blocked) || all == blocked)))
        {
            return "artist";
        }

        if (rules.Songs.Select(Lower).Contains(Lower(SongLine(title, artists))))
        {
            return "song";
        }

        var parts = VersionParts(title);
        if (rules.Versions.Select(Lower).FirstOrDefault(word => parts.Any(part => HasWord(part, word))) is { } version)
        {
            return $"version: {version}";
        }

        if (rules.LongerThanMinutes > 0 && duration > TimeSpan.FromMinutes(rules.LongerThanMinutes))
        {
            return $"longer than {rules.LongerThanMinutes} min";
        }

        if (rules.ShorterThanSeconds > 0 && duration > TimeSpan.Zero && duration < TimeSpan.FromSeconds(rules.ShorterThanSeconds))
        {
            return $"shorter than {rules.ShorterThanSeconds} s";
        }

        return null;
    }

    /// <summary>Adds <paramref name="value"/> to a list unless it is there already, whatever its case; true when added.</summary>
    public static bool AddTo(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || list.Any(item => Lower(item) == Lower(value)))
        {
            return false;
        }

        list.Add(value.Trim());
        return true;
    }

    private static string Lower(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>What the guard says about a song that matched a rule.</summary>
public enum SkipVerdict
{
    /// <summary>Skip it.</summary>
    Skip,

    /// <summary>Too many in a row: every song matches, so the rules rest for a minute (say so once).</summary>
    StartRest,

    /// <summary>The rules are resting: let it play.</summary>
    Resting,
}

/// <summary>
/// Keeps Skip rules from skipping for ever when every song matches (a
/// playlist of live versions): more than <see cref="Limit"/> skips within
/// <see cref="Window"/> of the first rests the rules for <see cref="Rest"/>.
/// </summary>
public sealed class SkipLoopGuard
{
    public const int Limit = 8;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(60);

    private DateTimeOffset _windowStart;
    private int _count;
    private DateTimeOffset? _restUntil;

    /// <summary>Whether the rules rest at <paramref name="now"/>.</summary>
    public bool IsResting(DateTimeOffset now)
    {
        if (_restUntil is { } until && now >= until)
        {
            _restUntil = null;
        }

        return _restUntil is not null;
    }

    /// <summary>A song matched at <paramref name="now"/>: skip it, or rest.</summary>
    public SkipVerdict Matched(DateTimeOffset now)
    {
        if (IsResting(now))
        {
            return SkipVerdict.Resting;
        }

        if (_count == 0 || now - _windowStart >= Window)
        {
            _windowStart = now;
            _count = 0;
        }

        _count++;
        if (_count > Limit)
        {
            _count = 0;
            _restUntil = now + Rest;
            return SkipVerdict.StartRest;
        }

        return SkipVerdict.Skip;
    }
}

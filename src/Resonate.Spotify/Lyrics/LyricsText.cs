using System.Globalization;
using System.Text;

namespace Resonate.Spotify.Lyrics;

/// <summary>
/// Names as a lyrics library files them, loose matching, and LRC parsing.
/// Ported from spotifast's lyrics module (src/lyrics.rs, MIT licence,
/// copyright Carmine Paolino), which the owner asked Resonate to follow.
/// </summary>
public static class LyricsText
{
    /// <summary>Words a title carries in brackets that a lyrics library does not.</summary>
    private static readonly string[] BracketNoise =
    [
        "remaster", "remastered", "remix", "live", "acoustic", "version", "edit", "mix", "mono", "stereo",
        "deluxe", "bonus", "expanded", "explicit", "anniversary", "feat", "featuring", "with",
    ];

    /// <summary>What a " - " suffix says when it is not part of the title.</summary>
    private static readonly string[] SuffixNoise =
    [
        "remaster", "remastered", "radio edit", "single version", "album version", "live", "mono", "stereo",
        "rerecorded", "re-recorded",
    ];

    private static readonly string[] FeaturingMarkers = ["featuring", "feat", "ft"];

    /// <summary>
    /// Strips what a player adds to a title and a library leaves out:
    /// "(Remastered 2011)", " - Live at Wembley", "feat. Someone". Never empty
    /// when the title was not.
    /// </summary>
    public static string CleanTitle(string title)
    {
        var original = WithoutInvisibles(title);
        var cleaned = new StringBuilder(original.Length);
        var rest = original;
        while (rest.IndexOfAny(['(', '[']) is var open and >= 0)
        {
            var close = rest.IndexOf(rest[open] == '(' ? ')' : ']', open);
            if (close < 0)
            {
                break;
            }

            var inner = rest[(open + 1)..close];
            if (BracketNoise.Any(word => HasPhrase(inner, word)))
            {
                cleaned.Append(rest[..open].TrimEnd());
            }
            else
            {
                cleaned.Append(rest, 0, close + 1);
            }

            rest = rest[(close + 1)..];
        }

        cleaned.Append(rest);
        var text = cleaned.ToString().Trim();

        // " - Remastered 2009" and the like, from the first dash whose tail is
        // noise; a dash inside a real title stays.
        var from = 0;
        while (text.IndexOf(" - ", from, StringComparison.Ordinal) is var dash and >= 0)
        {
            var tail = text[(dash + 3)..];
            if (SuffixNoise.Any(phrase => HasPhrase(tail, phrase)) || (StartsWithYear(tail) && HasPhrase(tail, "version")))
            {
                text = text[..dash];
                break;
            }

            from = dash + 3;
        }

        var result = StripFeaturing(text.Trim());
        return result.Length == 0 ? original.Trim() : result;
    }

    /// <summary>
    /// The artist a library files the song under: the first of several
    /// (Resonate joins them with ", ", LRCLIB sometimes stores "TOOL;Tool"),
    /// without a "feat." guest. Never empty when the name was not.
    /// </summary>
    public static string CleanArtist(string artist)
    {
        var original = WithoutInvisibles(artist);
        var cleaned = StripFeaturing(original);
        cleaned = cleaned.Split(';')[0];
        cleaned = cleaned.Split(", ")[0].Trim();
        return cleaned.Length == 0 ? original.Trim() : cleaned;
    }

    /// <summary>Whether two names are the same song or artist: case, accents and punctuation aside, or one inside the other.</summary>
    public static bool LooseMatch(string? left, string? right)
    {
        var a = Normalize(left ?? string.Empty);
        var b = Normalize(right ?? string.Empty);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses "[mm:ss.xx]" lines. A line may open with several stamps when
    /// the same words repeat; tags such as "[ar:...]" carry no time and are
    /// skipped. The result is sorted by time (lines at the same time keep their order).
    /// </summary>
    public static IReadOnlyList<LyricLine> ParseLrc(string text)
    {
        var lines = new List<LyricLine>();
        var times = new List<TimeSpan>();
        foreach (var raw in text.Split('\n'))
        {
            var rest = raw.TrimEnd('\r').TrimStart();
            times.Clear();
            while (LeadingStamp(rest) is { } stamp)
            {
                times.Add(stamp.At);
                rest = rest[stamp.Length..];
            }

            var body = rest.Trim();
            foreach (var at in times)
            {
                lines.Add(new LyricLine(at, body));
            }
        }

        // OrderBy is stable, so repeated words keep the order they were written in.
        return [.. lines.OrderBy(line => line.At)];
    }

    /// <summary>Lower case, common accents folded, punctuation gone, "&amp;" as "and", one space between words.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            var folded = Fold(c);
            if (folded is '\'' or '\u2019' or '`')
            {
                continue;
            }

            if (folded == '&')
            {
                foreach (var letter in " and ")
                {
                    Push(letter);
                }

                continue;
            }

            Push(folded);
        }

        return builder.ToString().Trim();

        void Push(char c)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }
    }

    /// <summary>The plain letter behind the Latin accents titles most often carry.</summary>
    private static char Fold(char c) => c switch
    {
        (>= '\u00C0' and <= '\u00C5') or (>= '\u00E0' and <= '\u00E5') => 'a',
        '\u00C7' or '\u00E7' => 'c',
        (>= '\u00C8' and <= '\u00CB') or (>= '\u00E8' and <= '\u00EB') => 'e',
        (>= '\u00CC' and <= '\u00CF') or (>= '\u00EC' and <= '\u00EF') => 'i',
        '\u00D1' or '\u00F1' => 'n',
        (>= '\u00D2' and <= '\u00D6') or '\u00D8' or (>= '\u00F2' and <= '\u00F6') or '\u00F8' => 'o',
        (>= '\u00D9' and <= '\u00DC') or (>= '\u00F9' and <= '\u00FC') => 'u',
        '\u00DD' or '\u00FD' or '\u00FF' => 'y',
        '\u00DF' => 's',
        _ => c,
    };

    /// <summary>
    /// Drops invisible characters that carry no meaning: a zero-width space,
    /// the word joiner and the invisible operators, and a byte order mark. Not
    /// the zero-width joiner or non-joiner, which hold emoji sequences and
    /// Indic and Persian words together.
    /// </summary>
    private static string WithoutInvisibles(string text) =>
        text.Any(IsDisposable) ? string.Concat(text.Where(c => !IsDisposable(c))) : text;

    private static bool IsDisposable(char c) => c is '\u200B' or (>= '\u2060' and <= '\u2064') or '\uFEFF';

    /// <summary>Whether <paramref name="text"/> contains <paramref name="phrase"/> as whole words, ignoring case.</summary>
    private static bool HasPhrase(string text, string phrase)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else
            {
                AddWord();
            }
        }

        AddWord();
        var wanted = phrase.Split(' ');
        for (var start = 0; start + wanted.Length <= words.Count; start++)
        {
            var i = 0;
            while (i < wanted.Length && words[start + i] == wanted[i])
            {
                i++;
            }

            if (i == wanted.Length)
            {
                return true;
            }
        }

        return false;

        void AddWord()
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString().Trim('-'));
                word.Clear();
            }
        }
    }

    private static bool StartsWithYear(string text)
    {
        var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 4 } && first.All(char.IsAsciiDigit);
    }

    /// <summary>Everything from a standalone "feat", "ft" or "featuring" on.</summary>
    private static string StripFeaturing(string text)
    {
        // Lower casing one UTF-16 unit at a time keeps every offset in the
        // copy pointing at the same place in the original (spotifast had to
        // map offsets because Rust's lower casing can change a string's length).
        var lower = string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.ToLowerInvariant(source[i]);
            }
        });

        var cut = -1;
        foreach (var marker in FeaturingMarkers)
        {
            var from = 0;
            while (lower.IndexOf(marker, from, StringComparison.Ordinal) is var start and >= 0)
            {
                var end = start + marker.Length;
                var before = lower[..start].TrimEnd('-', '(').TrimEnd();
                var preceded = before.Length < start && before.Length > 0;
                var after = lower.AsSpan(end);
                if (after.StartsWith("."))
                {
                    after = after[1..];
                }

                if (preceded && after.StartsWith(" "))
                {
                    cut = cut < 0 ? before.Length : Math.Min(cut, before.Length);
                    break;
                }

                from = end;
            }
        }

        return (cut < 0 ? text : text[..cut]).Trim();
    }

    /// <summary>A time stamp at the head of <paramref name="text"/>: its time and its length in characters.</summary>
    private static (TimeSpan At, int Length)? LeadingStamp(string text)
    {
        if (!text.StartsWith('['))
        {
            return null;
        }

        var close = text.IndexOf(']', 1);
        if (close < 0)
        {
            return null;
        }

        var stamp = text.AsSpan(1, close - 1);
        var colon = stamp.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var minutesText = stamp[..colon];
        var secondsText = stamp[(colon + 1)..];
        var fractionText = ReadOnlySpan<char>.Empty;
        var hasFraction = false;
        var dot = secondsText.IndexOfAny('.', ':');
        if (dot >= 0)
        {
            fractionText = secondsText[(dot + 1)..];
            secondsText = secondsText[..dot];
            hasFraction = true;
        }

        if (!uint.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !uint.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        long fractionMs = 0;
        if (hasFraction)
        {
            if (fractionText.IsEmpty || fractionText.ContainsAnyExceptInRange('0', '9'))
            {
                return null;
            }

            // ".5" is half a second, ".50" too, and ".123" a little over a tenth; past three digits is too fine to matter.
            var digits = fractionText[..Math.Min(3, fractionText.Length)];
            fractionMs = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * (digits.Length switch { 1 => 100, 2 => 10, _ => 1 });
        }

        var milliseconds = (minutes * 60_000L) + (seconds * 1_000L) + fractionMs;
        return (TimeSpan.FromMilliseconds(milliseconds), close + 1);
    }
}

using System.Globalization;
using System.Text;

namespace Resonate.Spotify.Lyrics;

/// <summary>Words typed, found in a line: where they start and how many characters they take.</summary>
public readonly record struct LineMark(int Start, int Length);

/// <summary>
/// How Line Finder reads words. Lyrics and what is typed are both folded
/// (lower case, no accents, no apostrophes, punctuation as spaces, one space
/// between words), so "dont" finds "Don't" and "cafe" finds "Café". A word
/// typed counts in full when the line has it, nearly in full when a word of
/// the line starts with it or with its stem ("name" for "names"), and in
/// part when it only sits inside another word. Scripts written without
/// spaces (Japanese, Chinese, Thai) match anywhere in the line.
/// </summary>
public static class LineText
{
    /// <summary>The most words of a query that count; a pasted verse is cut here.</summary>
    internal const int MaxWords = 12;

    /// <summary>Words so common that they barely tell lines apart.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "i", "im", "ive", "id", "ill", "you", "youre", "your", "me", "my", "we", "our", "us",
        "it", "its", "is", "am", "are", "was", "be", "to", "of", "in", "on", "at", "and", "or", "but", "so",
        "oh", "ah", "yeah", "la", "na", "do", "dont", "that", "this", "for", "with", "all", "just", "like",
        "he", "she", "they", "them", "his", "her", "if", "no", "not", "up", "go", "get", "got", "can",
    };

    /// <summary>
    /// Lower case without accents or apostrophes, every run of other
    /// characters that are not letters or digits as one space, trimmed.
    /// </summary>
    public static string Fold(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Broken text (a lone surrogate) cannot be decomposed; it is folded as it is.
            decomposed = text;
        }

        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (IsApostrophe(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

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

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Where the words typed are in <paramref name="line"/>, to show them in
    /// the accent colour: each word of the line that is a word typed, starts
    /// with one, or shares its stem; in scripts without spaces, the typed text itself.
    /// </summary>
    public static IReadOnlyList<LineMark> Marks(string line, string query)
    {
        var parsed = LineQuery.Parse(query);
        if (parsed is null)
        {
            return [];
        }

        var marks = new List<LineMark>();
        var i = 0;
        while (i < line.Length)
        {
            if (!IsWordChar(line[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < line.Length && (IsWordChar(line[i]) || (IsApostrophe(line[i]) && i + 1 < line.Length && IsWordChar(line[i + 1]))))
            {
                i++;
            }

            var word = Fold(line[start..i]);
            if (word.Length == 0)
            {
                continue;
            }

            var hit = false;
            foreach (var typed in parsed.Words)
            {
                if (typed.Unspaced)
                {
                    // Japanese and the like: the typed text inside the run, wherever it is.
                    var at = line.IndexOf(typed.Text, start, i - start, StringComparison.OrdinalIgnoreCase);
                    while (at >= 0)
                    {
                        marks.Add(new LineMark(at, typed.Text.Length));
                        var next = at + typed.Text.Length;
                        at = next < i ? line.IndexOf(typed.Text, next, i - next, StringComparison.OrdinalIgnoreCase) : -1;
                    }

                    continue;
                }

                if (!hit && WordCredit(Encoding.UTF8.GetBytes(word), typed) >= StrongCredit)
                {
                    marks.Add(new LineMark(start, i - start));
                    hit = true;
                }
            }
        }

        // In order, with overlapping ones (two words typed in one run of Japanese) joined.
        marks.Sort((a, b) => a.Start.CompareTo(b.Start));
        var joined = new List<LineMark>(marks.Count);
        foreach (var mark in marks)
        {
            if (joined.Count > 0 && joined[^1] is var last && mark.Start <= last.Start + last.Length)
            {
                var end = Math.Max(last.Start + last.Length, mark.Start + mark.Length);
                joined[^1] = new LineMark(last.Start, end - last.Start);
            }
            else
            {
                joined.Add(mark);
            }
        }

        return joined;
    }

    /// <summary>A word of the line counts this much toward a word typed when it is the word, starts with it, or shares its stem.</summary>
    internal const double StrongCredit = 0.8;

    /// <summary>
    /// How much the line's word <paramref name="word"/> (folded, UTF-8)
    /// counts for the typed word: 1 when it is that word, less when it
    /// starts with it or its stem, a little when it only holds it.
    /// </summary>
    internal static double WordCredit(ReadOnlySpan<byte> word, QueryWord typed)
    {
        var bytes = typed.Bytes;
        if (word.SequenceEqual(bytes))
        {
            return 1;
        }

        if (typed.Unspaced)
        {
            return word.IndexOf(bytes) >= 0 ? 1 : 0;
        }

        if (typed.Text.Length >= 3 && word.StartsWith(bytes))
        {
            return 0.85;
        }

        if (typed.Stem is { } stem && word.StartsWith(stem))
        {
            return StrongCredit;
        }

        return typed.Text.Length >= 3 && word.IndexOf(bytes) > 0 ? 0.45 : 0;
    }

    /// <summary>"names" → "name", "walked" → "walk", "running" → "runn": what is left once a common ending goes, or null.</summary>
    internal static string? Stem(string word)
    {
        if (!word.All(char.IsAsciiLetterLower))
        {
            return null;
        }

        var stem = word switch
        {
            { Length: >= 6 } when word.EndsWith("ing", StringComparison.Ordinal) => word[..^3],
            { Length: >= 5 } when word.EndsWith("ed", StringComparison.Ordinal) => word[..^2],
            { Length: >= 4 } when word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) => word[..^1],
            _ => null,
        };
        return stem is { Length: >= 3 } ? stem : null;
    }

    /// <summary>How much a word typed weighs: common little words count for less.</summary>
    internal static double Weight(string word) =>
        Common.Contains(word) ? 0.35
        : word.Length <= 2 ? 0.5
        : word.Length == 3 ? 0.8
        : 1;

    internal static bool IsCommon(string word) => Common.Contains(word);

    /// <summary>Scripts written without spaces between words, where a word typed is found anywhere in the line.</summary>
    internal static bool IsUnspaced(string word)
    {
        foreach (var c in word)
        {
            if (c is (>= '぀' and <= 'ヿ') or (>= '㐀' and <= '䶿') or (>= '一' and <= '鿿')
                or (>= '豈' and <= '﫿') or (>= 'ｦ' and <= 'ﾟ') or (>= '฀' and <= '໿')
                or (>= 'က' and <= '႟') or (>= 'ក' and <= '៿'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    private static bool IsApostrophe(char c) => c is '\'' or '’' or '‘' or 'ʼ' or '`' or '´';
}

/// <summary>One word typed, folded, with what it is searched by.</summary>
internal sealed class QueryWord
{
    public QueryWord(string text)
    {
        Text = text;
        Bytes = Encoding.UTF8.GetBytes(text);
        Unspaced = LineText.IsUnspaced(text);
        Stem = Unspaced ? null : LineText.Stem(text) is { } stem ? Encoding.UTF8.GetBytes(stem) : null;
        Weight = Unspaced ? 1 : LineText.Weight(text);
        IsKey = Unspaced || (text.Length >= 3 && !LineText.IsCommon(text));
    }

    public string Text { get; }

    public byte[] Bytes { get; }

    /// <summary>The word without a common ending, for a word of the line to start with; null when it has none.</summary>
    public byte[]? Stem { get; }

    public double Weight { get; }

    /// <summary>Written without spaces (Japanese, Chinese, Thai): found inside words.</summary>
    public bool Unspaced { get; }

    /// <summary>Telling enough to look for lines by: not a common little word.</summary>
    public bool IsKey { get; }

    /// <summary>What a line must hold to be worth scoring for this word: its stem, which the word itself starts with.</summary>
    public byte[] Needle => Stem ?? Bytes;
}

/// <summary>What was typed, as words to look for.</summary>
internal sealed class LineQuery
{
    private LineQuery(IReadOnlyList<QueryWord> words)
    {
        Words = words;
        var keys = words.Where(w => w.IsKey).ToList();
        if (keys.Count == 0)
        {
            // Only little words typed ("let it be"): every one is looked for.
            keys = [.. words.Where(w => w.Text.Length >= 2)];
        }

        Keys = keys;
        TotalWeight = words.Sum(w => w.Weight);
        MinCoverage = words.Count <= 2 ? 0.75 : 0.5;
    }

    public IReadOnlyList<QueryWord> Words { get; }

    /// <summary>The words that pick which lines are scored at all.</summary>
    public IReadOnlyList<QueryWord> Keys { get; }

    public double TotalWeight { get; }

    /// <summary>The share of what was typed a line must have: nearly all of one or two words, half of a longer line.</summary>
    public double MinCoverage { get; }

    /// <summary>The words typed, or null when nothing searchable was typed.</summary>
    public static LineQuery? Parse(string text)
    {
        var words = LineText.Fold(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(LineText.MaxWords)
            .Select(w => new QueryWord(w))
            .ToList();
        var query = new LineQuery(words);
        return query.Keys.Count == 0 ? null : query;
    }
}

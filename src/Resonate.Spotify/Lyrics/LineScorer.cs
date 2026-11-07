namespace Resonate.Spotify.Lyrics;

/// <summary>
/// Finds a song's line that fits a query best. Not thread-safe: one per search.
/// </summary>
internal sealed class LineScorer
{
    /// <summary>A line that only fits together with the next one counts for a little less.</summary>
    private const double TwoLineFactor = 0.92;
    private const double OrderBonus = 0.15;
    private const double PhraseBonus = 0.25;
    private const double ExtraWordPenalty = 0.015;
    private const int MaxPenalisedWords = 12;

    private readonly LineQuery _query;
    private readonly bool[] _isKey;
    private readonly int[] _positions;
    private readonly List<(int Start, int Length)> _tokens = [];
    private readonly HashSet<int> _candidates = [];
    private readonly HashSet<int> _matched = [];

    public LineScorer(LineQuery query)
    {
        _query = query;
        _isKey = [.. query.Words.Select(w => query.Keys.Contains(w))];
        _positions = new int[query.Words.Count];
    }

    /// <summary>The song's line that fits best, or null when none fits well enough.</summary>
    public (int Line, double Score)? Best(LineSong song)
    {
        var text = song.Folded.AsSpan();

        // The most the song could reach if every word typed that it holds counted in full; most songs stop here.
        var reachable = 0.0;
        for (var k = 0; k < _query.Words.Count; k++)
        {
            var word = _query.Words[k];
            if (!_isKey[k] || text.IndexOf(word.Needle) >= 0)
            {
                reachable += word.Weight;
            }
        }

        if (reachable / _query.TotalWeight < _query.MinCoverage)
        {
            return null;
        }

        // Only lines that hold a telling word typed are scored.
        _candidates.Clear();
        foreach (var key in _query.Keys)
        {
            var from = 0;
            while (from < text.Length && text[from..].IndexOf(key.Needle) is var at and >= 0)
            {
                var line = song.LineAt(from + at);
                _candidates.Add(line);
                from = song.EndOf(line) + 1;
            }
        }

        (int Line, double Score)? best = null;
        foreach (var line in _candidates)
        {
            Consider(line, line, 1);
            if (line + 1 < song.LineCount)
            {
                // A line typed across a line break.
                Consider(line, line + 1, TwoLineFactor);
            }

            if (line > 0 && !_candidates.Contains(line - 1))
            {
                Consider(line - 1, line, TwoLineFactor);
            }
        }

        return best;

        void Consider(int first, int last, double factor)
        {
            var span = song.Folded.AsSpan(song.Starts[first], song.EndOf(last) - song.Starts[first]);
            var (score, coverage, strong) = Score(span);
            score *= factor;
            if (strong && coverage >= _query.MinCoverage
                && (best is not { } current || score > current.Score || (score == current.Score && first < current.Line)))
            {
                best = (first, score);
            }
        }
    }

    /// <summary>
    /// How well a stretch of folded text fits: the share of the typed words
    /// it holds (weighted), more when they come in the order typed and most
    /// when it holds them as one phrase, a little less for every word it
    /// holds that was not typed.
    /// </summary>
    internal (double Score, double Coverage, bool Strong) Score(ReadOnlySpan<byte> span)
    {
        _tokens.Clear();
        var start = -1;
        for (var i = 0; i <= span.Length; i++)
        {
            var boundary = i == span.Length || span[i] is (byte)' ' or (byte)'\n';
            if (boundary && start >= 0)
            {
                _tokens.Add((start, i - start));
                start = -1;
            }
            else if (!boundary && start < 0)
            {
                start = i;
            }
        }

        var words = _query.Words;
        double got = 0;
        var strong = false;
        for (var k = 0; k < words.Count; k++)
        {
            var word = words[k];
            double credit = 0;
            var token = -1;
            if (word.Unspaced)
            {
                if (span.IndexOf(word.Bytes) is var at and >= 0)
                {
                    credit = 1;
                    token = TokenAt(at);
                }
            }
            else
            {
                for (var t = 0; t < _tokens.Count && credit < 1; t++)
                {
                    var c = LineText.WordCredit(span.Slice(_tokens[t].Start, _tokens[t].Length), word);
                    if (c > credit)
                    {
                        credit = c;
                        token = t;
                    }
                }
            }

            got += credit * word.Weight;
            strong |= _isKey[k] && credit >= LineText.StrongCredit;
            _positions[k] = credit > 0 ? token : -1;
        }

        // The words found, taken in the order typed: each next one later in the line counts, right after it counts twice.
        var coverage = got / _query.TotalWeight;
        var order = 0;
        var previous = -1;
        _matched.Clear();
        for (var k = 0; k < words.Count; k++)
        {
            if (_positions[k] < 0)
            {
                continue;
            }

            if (previous >= 0 && _positions[k] > previous)
            {
                order += _positions[k] == previous + 1 ? 2 : 1;
            }

            previous = _positions[k];
            _matched.Add(_positions[k]);
        }

        var score = coverage;
        if (words.Count > 1)
        {
            score += OrderBonus * order / (2.0 * (words.Count - 1));
            if (HasPhrase(span))
            {
                score += PhraseBonus;
            }
        }

        score -= ExtraWordPenalty * Math.Min(_tokens.Count - _matched.Count, MaxPenalisedWords);
        return (score, coverage, strong);

        int TokenAt(int position)
        {
            for (var t = 0; t < _tokens.Count; t++)
            {
                if (position < _tokens[t].Start + _tokens[t].Length)
                {
                    return t;
                }
            }

            return _tokens.Count - 1;
        }
    }

    /// <summary>The words typed, one after another, as whole words.</summary>
    private bool HasPhrase(ReadOnlySpan<byte> span)
    {
        var words = _query.Words;
        if (words.Any(w => w.Unspaced))
        {
            return false;
        }

        for (var first = 0; first + words.Count <= _tokens.Count; first++)
        {
            var k = 0;
            while (k < words.Count && span.Slice(_tokens[first + k].Start, _tokens[first + k].Length).SequenceEqual(words[k].Bytes))
            {
                k++;
            }

            if (k == words.Count)
            {
                return true;
            }
        }

        return false;
    }
}

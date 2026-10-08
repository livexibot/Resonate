namespace Resonate.Spotify.Lyrics;

/// <summary>What a lyrics lookup knows about the song that plays.</summary>
public sealed record LyricsQuery(string Artist, string Title, string Album, TimeSpan Duration)
{
    /// <summary>The query for a song, or null when it has no title or artist to look up (an untagged file).</summary>
    public static LyricsQuery? For(string? title, string? artists, string? album, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artists))
        {
            return null;
        }

        return new LyricsQuery(artists.Trim(), title.Trim(), album?.Trim() ?? string.Empty, duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
    }
}

/// <summary>One line of words; <see cref="At"/> is when it starts, null in lyrics that carry no timing.</summary>
public sealed record LyricLine(TimeSpan? At, string Text);

/// <summary>The words of a song: synced (every line has a time), plain, or none because it is instrumental.</summary>
public sealed class SongLyrics
{
    public SongLyrics(IReadOnlyList<LyricLine> lines, bool synced, bool instrumental = false)
    {
        Lines = lines;
        IsSynced = synced && lines.Count > 0 && lines.All(l => l.At is not null);
        IsInstrumental = instrumental;
    }

    /// <summary>The library knows the song has no words.</summary>
    public static SongLyrics Instrumental { get; } = new([], synced: false, instrumental: true);

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>Every line has a time, so the words can follow the song.</summary>
    public bool IsSynced { get; }

    public bool IsInstrumental { get; }

    /// <summary>
    /// The index of the line being sung at <paramref name="position"/>: the
    /// last one started. -1 before the first line and for lyrics without timing.
    /// </summary>
    public int ActiveLine(TimeSpan position)
    {
        if (!IsSynced)
        {
            return -1;
        }

        // Lines are sorted by time: find how many have started.
        int low = 0, high = Lines.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Lines[middle].At <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}

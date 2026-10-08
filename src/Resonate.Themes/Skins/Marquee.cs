using System.Globalization;

namespace Resonate.Themes.Skins;

/// <summary>The scrolling song line of the classic player.</summary>
public static class Marquee
{
    /// <summary>Character cells in the marquee (154 pixels at 5 each: 30 whole ones and the edge of one more).</summary>
    public const int Cells = 31;

    /// <summary>What separates the end of a scrolling line from its start again.</summary>
    public const string Gap = "  ***  ";

    /// <summary>How long the marquee waits between one-character steps.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(220);

    /// <summary>"Artist - Title (3:45)": the song as the marquee writes it.</summary>
    /// <remarks>
    /// A missing artist or title is left out with its dash, an unknown
    /// length with its brackets, and an hour or more reads "1:02:03".
    /// Nothing playing (no artist and no title) gives an empty line.
    /// </remarks>
    public static string Line(string? artist, string? title, TimeSpan duration)
    {
        artist = artist?.Trim();
        title = title?.Trim();
        var song = (string.IsNullOrEmpty(artist), string.IsNullOrEmpty(title)) switch
        {
            (true, true) => string.Empty,
            (true, false) => title!,
            (false, true) => artist!,
            (false, false) => $"{artist} - {title}",
        };

        // Whole seconds, cut down as Winamp did; under one second counts as unknown.
        var seconds = (long)Math.Floor(duration.TotalSeconds);
        if (song.Length == 0 || seconds < 1)
        {
            return song;
        }

        var length = seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        return $"{song} ({length})";
    }

    /// <summary>True when the line is too long to stand still.</summary>
    public static bool Scrolls(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // 31 characters would show all but the last pixel column, so Winamp scrolls from there.
        return line.Length >= Cells;
    }

    /// <summary>The <see cref="Cells"/> characters shown after scrolling <paramref name="offset"/> steps.</summary>
    /// <remarks>
    /// A line that fits is padded with spaces and never moves; a longer one
    /// runs on through <see cref="Gap"/> into its own start, so any offset,
    /// negative ones included, gives a full window.
    /// </remarks>
    public static string Window(string line, int offset)
    {
        ArgumentNullException.ThrowIfNull(line);
        return string.Create(Cells, (line, offset), static (window, state) =>
        {
            for (var i = 0; i < window.Length; i++)
            {
                window[i] = CharAt(state.line, state.offset, i);
            }
        });
    }

    /// <summary>Character <paramref name="index"/> of <see cref="Window"/>, without building the string (the renderer draws from this).</summary>
    internal static char CharAt(string line, int offset, int index)
    {
        if (!Scrolls(line))
        {
            return index < line.Length ? line[index] : ' ';
        }

        var period = line.Length + Gap.Length;
        var at = (int)((((long)offset + index) % period + period) % period);
        return at < line.Length ? line[at] : Gap[at - line.Length];
    }
}

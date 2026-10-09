using System.Globalization;
using System.Text;
using Resonate.Spotify.History;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Quiet hours, a built-in plugin: the volume stays at or below a cap during
/// the hours the user picks, which may run past midnight (22 to 7).
/// </summary>
public static class QuietHours
{
    /// <summary>Whether <paramref name="hour"/> (0 to 23) falls from <paramref name="from"/> up to, not including, <paramref name="to"/>; nothing when they are the same.</summary>
    public static bool IsQuiet(int from, int to, int hour)
    {
        from = Math.Clamp(from, 0, 23);
        to = Math.Clamp(to, 0, 23);
        return from < to ? hour >= from && hour < to : from > to && (hour >= from || hour < to);
    }

    /// <summary><paramref name="volume"/> (0 to 1) held at or under <paramref name="percent"/>.</summary>
    public static double Cap(double volume, int percent) => Math.Min(volume, Math.Clamp(percent, 0, 100) / 100.0);
}

/// <summary>
/// Now playing file, a built-in plugin: the playing song written as one line
/// of text for stream overlays (OBS and the like read a text file).
/// </summary>
public static class NowPlayingText
{
    /// <summary>
    /// <paramref name="format"/> with {title}, {artist} and {album} filled in;
    /// empty with nothing playing. One line: line breaks become spaces.
    /// </summary>
    public static string Format(string? format, string? title, string? artists, string? album)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var text = (string.IsNullOrWhiteSpace(format) ? "{artist} - {title}" : format)
            .Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", artists ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{album}", album ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    }
}

/// <summary>
/// Export history, a built-in plugin: the listening history as a CSV file
/// that spreadsheets open (UTF-8 with a byte order mark, so Excel reads
/// accents right), newest play first.
/// </summary>
public static class HistoryCsv
{
    public static string Write(IEnumerable<PlayRecord> plays)
    {
        var text = new StringBuilder();
        text.Append("Played at,Title,Artists,Album,Length (seconds),Spotify link\r\n");
        foreach (var play in plays.OrderByDescending(p => p.PlayedAt))
        {
            text.Append(Cell(play.PlayedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
                .Append(Cell(play.Title)).Append(',')
                .Append(Cell(string.Join(", ", play.Artists.Select(a => a.Name)))).Append(',')
                .Append(Cell(play.Album)).Append(',')
                .Append((play.DurationMs / 1000).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Cell(play.Uri)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>A cell, quoted when it needs to be; a leading =, +, - or @ is kept as text, so a spreadsheet never runs it as a formula.</summary>
    private static string Cell(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}

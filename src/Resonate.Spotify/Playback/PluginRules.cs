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

    /// <summary>Whether <paramref name="day"/> is one of the days picked: every day (0), weekdays (1) or weekends (2).</summary>
    public static bool OnDay(int days, DayOfWeek day) => days switch
    {
        1 => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        2 => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true,
    };
}

/// <summary>
/// Alarm, a built-in plugin: music at a time the user picks, on the days
/// they pick, rising from quiet to their volume. Rings once a day, also when
/// Resonate starts (or wakes) within its minute and a few after it.
/// </summary>
public static class AlarmRules
{
    /// <summary>How long after its minute an alarm still rings (Resonate busy or just started).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    /// <summary>Whether the alarm should ring at <paramref name="now"/> (local time) when it last rang on <paramref name="lastRang"/>.</summary>
    public static bool IsDue(int hour, int minute, int days, DateTime now, DateOnly? lastRang)
    {
        var today = DateOnly.FromDateTime(now);
        if (lastRang == today || !QuietHours.OnDay(days, now.DayOfWeek))
        {
            return false;
        }

        var at = now.Date + new TimeSpan(Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0);
        return now >= at && now - at < Grace;
    }

    /// <summary>The volume <paramref name="elapsed"/> into a fade of <paramref name="fade"/> up to <paramref name="target"/> (0 to 1), from a whisper.</summary>
    public static double FadeVolume(TimeSpan elapsed, TimeSpan fade, double target)
    {
        const double Start = 0.02;
        if (fade <= TimeSpan.Zero || elapsed >= fade)
        {
            return target;
        }

        var share = Math.Clamp(elapsed / fade, 0, 1);

        // Eased, so the first steps are gentle.
        return Start + ((target - Start) * share * share);
    }
}

/// <summary>Skip intros and outros, a built-in plugin: where a song is moved to at its start, and when it is left before its end.</summary>
public static class SkipRules
{
    /// <summary>Where to move a song that just started, or null to leave it: songs shorter than <paramref name="shortest"/> are left alone.</summary>
    public static TimeSpan? IntroSkip(TimeSpan position, TimeSpan duration, int introSeconds, int shortestSeconds)
    {
        var intro = TimeSpan.FromSeconds(Math.Clamp(introSeconds, 0, 120));
        if (intro <= TimeSpan.Zero || duration < TimeSpan.FromSeconds(Math.Max(shortestSeconds, 0)) || duration <= intro * 2 || position >= intro)
        {
            return null;
        }

        return intro;
    }

    /// <summary>Whether the song is in its last <paramref name="outroSeconds"/>, so the next one should start.</summary>
    public static bool OutroReached(TimeSpan position, TimeSpan duration, int outroSeconds, int shortestSeconds)
    {
        var outro = TimeSpan.FromSeconds(Math.Clamp(outroSeconds, 0, 120));
        return outro > TimeSpan.Zero
            && duration >= TimeSpan.FromSeconds(Math.Max(shortestSeconds, 0))
            && duration > outro * 2
            && position >= duration - outro;
    }
}

/// <summary>Focus timer, a built-in plugin: rounds of focus with breaks between them.</summary>
public static class FocusRules
{
    /// <summary>What comes after a focus or a break: a break after each focus but the last, then the next round; null when the rounds are done.</summary>
    public static (bool Focus, int Round)? Next(bool focus, int round, int rounds)
    {
        if (focus)
        {
            return round >= Math.Max(rounds, 1) ? null : (false, round);
        }

        return (true, round + 1);
    }
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

using System.Text.Json;
using System.Text.Json.Serialization;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.History;

/// <summary>The listening calendar as stored on disk, newest day first.</summary>
public sealed class CalendarFile
{
    public List<CalendarDay> Days { get; set; } = [];
}

/// <summary>A song on the calendar: enough to show its cover and play it again.</summary>
public sealed class CalendarSong
{
    public string Uri { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<ArtistRef> Artists { get; set; } = [];

    public string Album { get; set; } = string.Empty;

    public string? AlbumUri { get; set; }

    /// <summary>The album cover, about 300 pixels wide.</summary>
    public string? ImageUrl { get; set; }

    public int DurationMs { get; set; }

    /// <summary>How often it played that day.</summary>
    public int Plays { get; set; }

    /// <summary>False for local files, which Spotify can not start by address.</summary>
    [JsonIgnore]
    public bool CanPlay => !Uri.StartsWith("spotify:local:", StringComparison.Ordinal);

    internal static CalendarSong From(PlayRecord play) => new()
    {
        Uri = play.Uri,
        Title = play.Title,
        Artists = play.Artists,
        Album = play.Album,
        AlbumUri = play.AlbumUri,
        ImageUrl = play.ImageUrl,
        DurationMs = play.DurationMs,
    };

    /// <summary>The song as lists show it, so it can be played.</summary>
    public TrackInfo ToTrack() => new PlayRecord
    {
        Uri = Uri,
        Title = Title,
        Artists = Artists,
        Album = Album,
        AlbumUri = AlbumUri,
        ImageUrl = ImageUrl,
        DurationMs = DurationMs,
    }.ToTrack();
}

/// <summary>Songs played with no long pause between them.</summary>
public sealed class CalendarSession
{
    /// <summary>Minutes after the day's midnight (local time) when the first song started.</summary>
    public int Start { get; set; }

    /// <summary>Minutes after the day's midnight when the last song ended (1440 at most).</summary>
    public int End { get; set; }

    /// <summary>Where each song is in the day's <see cref="CalendarDay.Songs"/>, in the order they played.</summary>
    public List<int> Songs { get; set; } = [];
}

/// <summary>One day of listening on the calendar.</summary>
public sealed class CalendarDay
{
    public DateOnly Date { get; set; }

    /// <summary>How many songs were played (a song played twice counts twice).</summary>
    public int Plays { get; set; }

    /// <summary>
    /// The played songs' lengths added up, in minutes. Spotify counts a song
    /// once it played for 30 seconds, so this is approximate, as on Home.
    /// </summary>
    public int Minutes { get; set; }

    /// <summary>Each song played that day once, the most played first (ties: the one heard last), so the first is the day's cover.</summary>
    public List<CalendarSong> Songs { get; set; } = [];

    /// <summary>The day's listening sessions, earliest first.</summary>
    public List<CalendarSession> Sessions { get; set; } = [];

    /// <summary>The songs of a session in the order they played.</summary>
    public IEnumerable<CalendarSong> SongsOf(CalendarSession session) =>
        session.Songs.Where(i => i >= 0 && i < Songs.Count).Select(i => Songs[i]);

    /// <summary>The whole day in the order it played.</summary>
    public IEnumerable<CalendarSong> InOrder() => Sessions.SelectMany(SongsOf);
}

/// <summary>
/// A calendar of listening, a day at a time, that outlives the 90 days the
/// <see cref="ListeningHistory"/> keeps: whenever plays are added, the days
/// they cover are summed up (the most played songs, minutes, sessions) and
/// kept in a small file of their own. Costs no Spotify requests. Holds song
/// names and picture links only, never tokens. Reading or writing the file
/// may fail; the calendar is then kept for this session.
/// </summary>
public sealed class ListeningCalendar
{
    /// <summary>A pause longer than this starts a new session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <summary>About three years of days are kept.</summary>
    public const int MaxDays = 1100;

    private const int MinutesPerDay = 24 * 60;

    private readonly ListeningHistory _history;
    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<DateOnly, CalendarDay> _days = new Dictionary<DateOnly, CalendarDay>();
    private volatile bool _loaded;

    /// <param name="path">The file to keep the calendar in; null keeps it in memory only (demo mode, tests).</param>
    public ListeningCalendar(ListeningHistory history, string? path, TimeProvider? time = null)
    {
        _history = history;
        _path = path;
        _time = time ?? TimeProvider.System;
        _history.Changed += OnHistoryChanged;
        _history.Cleared += OnHistoryCleared;
    }

    /// <summary>Raised on any thread when days were added or changed (or the calendar was cleared).</summary>
    public event EventHandler? Changed;

    /// <summary>Every known day by its date. Empty until <see cref="Load"/> ran.</summary>
    public IReadOnlyDictionary<DateOnly, CalendarDay> Days => Volatile.Read(ref _days);

    public bool IsLoaded => _loaded;

    /// <summary>Today, in the user's time zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);

    /// <summary>Reads the stored days and adds the ones in the history. Call it off the interface thread.</summary>
    public void Load()
    {
        _history.Load();
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            UpdateLocked(Read(), save: true);
            _loaded = true;
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = UpdateLocked([.. Volatile.Read(ref _days).Values], save: true);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Signed out: the next account must not see this one's days.
    private void OnHistoryCleared(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            Volatile.Write(ref _days, new Dictionary<DateOnly, CalendarDay>());
            Delete();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool UpdateLocked(IReadOnlyCollection<CalendarDay> stored, bool save)
    {
        var built = Build(_history.Plays, _time.LocalTimeZone);
        var merged = Merge(stored, built);
        var before = Volatile.Read(ref _days);
        var changed = merged.Count != before.Count
            || merged.Any(d => !before.TryGetValue(d.Date, out var old) || !ReferenceEquals(old, d));
        Volatile.Write(ref _days, merged.ToDictionary(d => d.Date));
        if (changed && save && built.Count > 0)
        {
            Save(merged);
        }

        return changed;
    }

    /// <summary>
    /// The plays summed up by day in <paramref name="zone"/>, newest day
    /// first. Pure, so it is quick to test.
    /// </summary>
    public static List<CalendarDay> Build(IEnumerable<PlayRecord> plays, TimeZoneInfo zone)
    {
        var days = new List<CalendarDay>();
        foreach (var group in plays
            .Where(p => p.Uri.Length > 0)
            .Select(p => (Play: p, Local: TimeZoneInfo.ConvertTime(p.PlayedAt, zone)))
            .OrderBy(p => p.Play.PlayedAt)
            .GroupBy(p => DateOnly.FromDateTime(p.Local.DateTime)))
        {
            days.Add(BuildDay(group.Key, [.. group]));
        }

        days.Sort((a, b) => b.Date.CompareTo(a.Date));
        return days;
    }

    private static CalendarDay BuildDay(DateOnly date, List<(PlayRecord Play, DateTimeOffset Local)> plays)
    {
        // Each song once, the most played first; ties go to the one heard last.
        var tallies = new Dictionary<string, (CalendarSong Song, DateTimeOffset Last)>(StringComparer.Ordinal);
        foreach (var (play, _) in plays)
        {
            var song = tallies.TryGetValue(play.Uri, out var known) ? known.Song : CalendarSong.From(play);
            song.Plays++;
            tallies[play.Uri] = (song, play.PlayedAt);
        }

        var songs = tallies.Values
            .OrderByDescending(t => t.Song.Plays)
            .ThenByDescending(t => t.Last)
            .Select(t => t.Song)
            .ToList();
        var index = songs.Select((s, i) => (s.Uri, i)).ToDictionary(x => x.Uri, x => x.i, StringComparer.Ordinal);

        var day = new CalendarDay
        {
            Date = date,
            Plays = plays.Count,
            Minutes = (int)Math.Round(plays.Sum(p => (double)p.Play.DurationMs) / 60_000),
            Songs = songs,
        };

        // The history keeps when a song was counted, a moment after it started.
        CalendarSession? session = null;
        DateTimeOffset sessionEnd = default;
        foreach (var (play, local) in plays)
        {
            var ends = play.PlayedAt + TimeSpan.FromMilliseconds(Math.Max(0, play.DurationMs));
            if (session is null || play.PlayedAt - sessionEnd > SessionGap)
            {
                session = new CalendarSession { Start = MinuteOfDay(local) };
                day.Sessions.Add(session);
            }

            session.Songs.Add(index[play.Uri]);
            sessionEnd = ends > sessionEnd ? ends : sessionEnd;
            var endMinute = MinuteOfDay(local) + (int)Math.Ceiling(Math.Max(0, play.DurationMs) / 60_000.0);
            session.End = Math.Clamp(Math.Max(session.End, endMinute), session.Start + 1, MinutesPerDay);
        }

        return day;
    }

    private static int MinuteOfDay(DateTimeOffset local) => (local.Hour * 60) + local.Minute;

    /// <summary>
    /// The stored days with the ones just built from the history, newest
    /// first. A day the history now holds only part of (its oldest, as plays
    /// pass 90 days) keeps the fuller copy stored before.
    /// </summary>
    public static List<CalendarDay> Merge(IEnumerable<CalendarDay> stored, IEnumerable<CalendarDay> built)
    {
        var days = new Dictionary<DateOnly, CalendarDay>();
        foreach (var day in stored)
        {
            days[day.Date] = day;
        }

        foreach (var day in built)
        {
            if (!days.TryGetValue(day.Date, out var old) || old.Plays <= day.Plays)
            {
                days[day.Date] = old is not null && SameDay(old, day) ? old : day;
            }
        }

        var list = days.Values.ToList();
        list.Sort((a, b) => b.Date.CompareTo(a.Date));
        if (list.Count > MaxDays)
        {
            list.RemoveRange(MaxDays, list.Count - MaxDays);
        }

        return list;
    }

    // Kept as it was when nothing about it changed, so an unchanged history writes nothing.
    private static bool SameDay(CalendarDay a, CalendarDay b) =>
        a.Plays == b.Plays
        && a.Minutes == b.Minutes
        && a.Sessions.Count == b.Sessions.Count
        && a.Songs.Count == b.Songs.Count
        && a.Songs.Zip(b.Songs).All(p => p.First.Uri == p.Second.Uri && p.First.Plays == p.Second.Plays);

    private List<CalendarDay> Read()
    {
        if (_path is null)
        {
            return [];
        }

        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.CalendarFile)?.Days ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save(List<CalendarDay> days)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, new CalendarFile { Days = days }, SpotifyJsonContext.Default.CalendarFile);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this session.
        }
    }

    private void Delete()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>How a month of the calendar is laid out and shaded. Pure, so it is quick to test.</summary>
public static class CalendarMonth
{
    /// <summary>
    /// Every date shown for the month: whole weeks starting on
    /// <paramref name="firstDayOfWeek"/>, from the week holding the 1st to the
    /// week holding the last day (35 or 42 dates, 28 for a February that fits).
    /// </summary>
    public static List<DateOnly> Dates(int year, int month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var start = first.AddDays(-(((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
        var end = last.AddDays((((int)firstDayOfWeek + 6 - (int)last.DayOfWeek) % 7 + 7) % 7);
        var dates = new List<DateOnly>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            dates.Add(day);
        }

        return dates;
    }

    /// <summary>
    /// How bright a day's cover shows, 0 (a little listening) to 3 (the most
    /// that month), by its minutes against the month's busiest day.
    /// </summary>
    public static int Level(int minutes, int busiest)
    {
        if (busiest <= 0 || minutes <= 0)
        {
            return 0;
        }

        var share = (double)minutes / busiest;
        return share switch
        {
            >= 0.8 => 3,
            >= 0.5 => 2,
            >= 0.25 => 1,
            _ => 0,
        };
    }
}

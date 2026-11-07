using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.History;
using Resonate.Spotify.Tests.Fakes;

namespace Resonate.Spotify.Tests;

public sealed class CalendarTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    private readonly FakeWebApi _web = new();
    private readonly FakeTimeProvider _time = new(Music.Now);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-calendar-{Guid.NewGuid():N}");

    public CalendarTests() => _time.SetLocalTimeZone(Berlin);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Plays_go_to_the_day_they_were_played_on_in_the_users_time_zone()
    {
        // 23:30 UTC is already the next day at UTC+1.
        var late = Music.Play("late", "x", new DateTimeOffset(2026, 10, 16, 23, 30, 0, TimeSpan.Zero));
        var noon = Music.Play("noon", "x", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

        var days = ListeningCalendar.Build([late, noon], Berlin);

        Assert.Equal([new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 16)], days.Select(d => d.Date));
        Assert.Equal("spotify:track:late", days[0].Songs[0].Uri);
    }

    [Fact]
    public void The_days_cover_is_the_song_played_most_and_a_tie_goes_to_the_one_heard_last()
    {
        var at = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
        var plays = new[]
        {
            Music.Play("a", "x", at),
            Music.Play("b", "x", at.AddMinutes(4)),
            Music.Play("a", "x", at.AddMinutes(8)),
            Music.Play("c", "x", at.AddMinutes(12)),
            Music.Play("b", "x", at.AddMinutes(16)),
            Music.Play("d", "x", at.AddMinutes(20)),
        };

        var day = Assert.Single(ListeningCalendar.Build(plays, TimeZoneInfo.Utc));

        Assert.Equal(["spotify:track:b", "spotify:track:a", "spotify:track:d", "spotify:track:c"], day.Songs.Select(s => s.Uri));
        Assert.Equal([2, 2, 1, 1], day.Songs.Select(s => s.Plays));
        Assert.Equal(6, day.Plays);
        Assert.Equal(20, day.Minutes);
    }

    [Fact]
    public void A_pause_of_more_than_half_an_hour_starts_a_new_session()
    {
        var morning = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var plays = new[]
        {
            Music.Play("a", "x", morning, seconds: 240),
            Music.Play("b", "x", morning.AddMinutes(4), seconds: 240),
            Music.Play("c", "x", morning.AddMinutes(40), seconds: 180),
            Music.Play("a", "x", morning.AddHours(13), seconds: 240),
        };

        var day = Assert.Single(ListeningCalendar.Build(plays, TimeZoneInfo.Utc));

        Assert.Equal(3, day.Sessions.Count);
        Assert.Equal((480, 488), (day.Sessions[0].Start, day.Sessions[0].End));
        Assert.Equal(["spotify:track:a", "spotify:track:b"], day.SongsOf(day.Sessions[0]).Select(s => s.Uri));
        Assert.Equal((520, 523), (day.Sessions[1].Start, day.Sessions[1].End));
        Assert.Equal(["spotify:track:a", "spotify:track:b", "spotify:track:c", "spotify:track:a"], day.InOrder().Select(s => s.Uri));
    }

    [Fact]
    public void A_session_ending_after_midnight_stops_at_the_end_of_the_day()
    {
        var plays = new[] { Music.Play("a", "x", new DateTimeOffset(2026, 10, 17, 23, 58, 0, TimeSpan.Zero), seconds: 300) };

        var session = Assert.Single(Assert.Single(ListeningCalendar.Build(plays, TimeZoneInfo.Utc)).Sessions);

        Assert.Equal((1438, 1440), (session.Start, session.End));
    }

    [Fact]
    public void A_day_the_history_holds_only_part_of_keeps_the_fuller_copy()
    {
        var date = new DateOnly(2026, 7, 9);
        var full = new CalendarDay { Date = date, Plays = 30 };
        var trimmed = new CalendarDay { Date = date, Plays = 4 };
        var newer = new CalendarDay { Date = date.AddDays(1), Plays = 2 };

        var merged = ListeningCalendar.Merge([full], [newer, trimmed]);

        Assert.Equal([newer, full], merged);
    }

    [Fact]
    public async Task Days_are_kept_after_the_history_has_forgotten_them()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("old", "x"), Music.Now.AddDays(-89)));
        using (var history = History())
        {
            var calendar = Calendar(history);
            calendar.Load();
            await history.SyncAsync(TestContext.Current.CancellationToken);
            Assert.Single(calendar.Days);
        }

        // Three days later the history has dropped the play; the calendar has not.
        _time.Advance(TimeSpan.FromDays(3));
        _web.RecentlyPlayed.Clear();
        using var reopened = History();
        var later = Calendar(reopened);
        later.Load();

        Assert.Empty(reopened.Plays);
        var day = Assert.Single(later.Days.Values);
        Assert.Equal("spotify:track:old", day.Songs[0].Uri);
    }

    [Fact]
    public async Task Signing_out_forgets_the_calendar_too()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a", "x"), Music.Now.AddHours(-2)));
        using var history = History();
        var calendar = Calendar(history);
        calendar.Load();
        await history.SyncAsync(TestContext.Current.CancellationToken);
        var changes = 0;
        calendar.Changed += (_, _) => changes++;

        history.Clear();

        Assert.Empty(calendar.Days);
        Assert.Equal(1, changes);
        Assert.False(File.Exists(Path.Combine(_folder, "calendar.json")));
    }

    [Fact]
    public void A_month_is_shown_in_whole_weeks_from_the_first_day_of_the_week()
    {
        // October 2026 starts on a Thursday and ends on a Saturday.
        var monday = CalendarMonth.Dates(2026, 10, DayOfWeek.Monday);
        var sunday = CalendarMonth.Dates(2026, 10, DayOfWeek.Sunday);

        Assert.Equal(35, monday.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), monday[0]);
        Assert.Equal(new DateOnly(2026, 11, 1), monday[^1]);
        Assert.Equal(35, sunday.Count);
        Assert.Equal(new DateOnly(2026, 9, 27), sunday[0]);
        Assert.Equal(new DateOnly(2026, 10, 31), sunday[^1]);
    }

    [Theory]
    [InlineData(0, 200, 0)]
    [InlineData(40, 200, 0)]
    [InlineData(60, 200, 1)]
    [InlineData(110, 200, 2)]
    [InlineData(170, 200, 3)]
    [InlineData(200, 200, 3)]
    public void A_days_brightness_follows_its_minutes_against_the_busiest_day(int minutes, int busiest, int level) =>
        Assert.Equal(level, CalendarMonth.Level(minutes, busiest));

    private ListeningHistory History() => new(_web, Path.Combine(_folder, "history.json"), _time);

    private ListeningCalendar Calendar(ListeningHistory history) => new(history, Path.Combine(_folder, "calendar.json"), _time);
}

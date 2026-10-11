using Resonate.Spotify.Playback;
using Xunit;

namespace Resonate.Spotify.Tests;

public sealed class SleepTimerAndSkipRulesTests
{
    private static SkipFilterRules Rules(
        string[]? artists = null,
        string[]? versions = null,
        string[]? songs = null,
        int longerThan = 0,
        int shorterThan = 0) =>
        new(artists ?? [], versions ?? [], songs ?? [], longerThan, shorterThan);

    [Fact]
    public void Sleep_timer_offers_the_usual_lengths_and_the_users_own_in_order()
    {
        Assert.Equal([15, 30, 60], SleepTimerRules.Choices(30));
        Assert.Equal([15, 30, 60, 90], SleepTimerRules.Choices(90));
        Assert.Equal([5, 15, 30, 60], SleepTimerRules.Choices(5));
        Assert.Equal("15 minutes", SleepTimerRules.MinutesText(15));
        Assert.Equal("1 hour", SleepTimerRules.MinutesText(60));
        Assert.Equal("2 hours", SleepTimerRules.MinutesText(120));
    }

    [Fact]
    public void Sleep_timer_shows_minutes_then_seconds()
    {
        Assert.Equal("15 min", SleepTimerRules.ClockText(TimeSpan.FromMinutes(15)));
        Assert.Equal("2 min", SleepTimerRules.ClockText(TimeSpan.FromSeconds(61)));
        Assert.Equal("1 min", SleepTimerRules.ClockText(TimeSpan.FromSeconds(60)));
        Assert.Equal("0:09", SleepTimerRules.ClockText(TimeSpan.FromSeconds(8.2)));
        Assert.Equal("0:00", SleepTimerRules.ClockText(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void Sleep_timer_fades_over_the_last_minute_but_never_to_silence()
    {
        Assert.Null(SleepTimerRules.FadeShare(TimeSpan.FromSeconds(61)));
        Assert.Equal(1, SleepTimerRules.FadeShare(TimeSpan.FromSeconds(60)));
        Assert.Equal(0.5, SleepTimerRules.FadeShare(TimeSpan.FromSeconds(30)));
        Assert.Equal(SleepTimerRules.LowestFade, SleepTimerRules.FadeShare(TimeSpan.FromSeconds(1)));
        Assert.Equal(SleepTimerRules.LowestFade, SleepTimerRules.FadeShare(TimeSpan.Zero));
    }

    [Fact]
    public void Skips_blocked_artists_even_among_several()
    {
        var rules = Rules(artists: ["Band B"]);
        Assert.Equal("artist", SkipFilter.ReasonToSkip("Song", "Band A, Band B", TimeSpan.FromMinutes(3), rules));
        Assert.Equal("artist", SkipFilter.ReasonToSkip("Song", "band b", TimeSpan.FromMinutes(3), rules));
        Assert.Null(SkipFilter.ReasonToSkip("Song", "Band Bee", TimeSpan.FromMinutes(3), rules));
    }

    [Fact]
    public void Skips_versions_named_in_brackets_or_after_a_dash_only()
    {
        var rules = Rules(versions: ["live", "sped up"]);
        Assert.Equal("version: live", SkipFilter.ReasonToSkip("Song (Live)", "A", TimeSpan.FromMinutes(3), rules));
        Assert.Equal("version: live", SkipFilter.ReasonToSkip("Song [Live at Wembley]", "A", TimeSpan.FromMinutes(3), rules));
        Assert.Equal("version: sped up", SkipFilter.ReasonToSkip("Song - Sped Up", "A", TimeSpan.FromMinutes(3), rules));
        Assert.Null(SkipFilter.ReasonToSkip("Live Forever", "A", TimeSpan.FromMinutes(3), rules));
        Assert.Null(SkipFilter.ReasonToSkip("Song (Alive)", "A", TimeSpan.FromMinutes(3), rules));
        Assert.Equal(["live", "remastered"], SkipFilter.VersionParts("Song (Live) - Remastered"));
    }

    [Fact]
    public void Skips_by_length_and_saved_songs()
    {
        Assert.Equal("longer than 10 min", SkipFilter.ReasonToSkip("Epic", "A", TimeSpan.FromMinutes(11), Rules(longerThan: 10)));
        Assert.Null(SkipFilter.ReasonToSkip("Epic", "A", TimeSpan.FromMinutes(9), Rules(longerThan: 10)));
        Assert.Equal("shorter than 60 s", SkipFilter.ReasonToSkip("Intro", "A", TimeSpan.FromSeconds(30), Rules(shorterThan: 60)));
        Assert.Null(SkipFilter.ReasonToSkip("Intro", "A", TimeSpan.Zero, Rules(shorterThan: 60)));
        Assert.Equal("song", SkipFilter.ReasonToSkip("Hit", "A, B", TimeSpan.FromMinutes(3), Rules(songs: ["hit — a, b"])));
        Assert.Null(SkipFilter.ReasonToSkip(null, "A", TimeSpan.FromMinutes(3), Rules(artists: ["A"])));
    }

    [Fact]
    public void Adds_a_name_once_whatever_its_case()
    {
        List<string> list = ["Band A"];
        Assert.False(SkipFilter.AddTo(list, "band a "));
        Assert.True(SkipFilter.AddTo(list, "Band B"));
        Assert.False(SkipFilter.AddTo(list, " "));
        Assert.Equal(["Band A", "Band B"], list);
    }

    [Fact]
    public void Rests_for_a_minute_when_every_song_matches()
    {
        var guard = new SkipLoopGuard();
        var start = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < SkipLoopGuard.Limit; i++)
        {
            Assert.Equal(SkipVerdict.Skip, guard.Matched(start + TimeSpan.FromSeconds(i)));
        }

        Assert.Equal(SkipVerdict.StartRest, guard.Matched(start + TimeSpan.FromSeconds(9)));
        Assert.Equal(SkipVerdict.Resting, guard.Matched(start + TimeSpan.FromSeconds(30)));
        Assert.True(guard.IsResting(start + TimeSpan.FromSeconds(68)));
        Assert.Equal(SkipVerdict.Skip, guard.Matched(start + TimeSpan.FromSeconds(70)));
    }

    [Fact]
    public void Skips_spread_out_in_time_never_rest()
    {
        var guard = new SkipLoopGuard();
        var now = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(SkipVerdict.Skip, guard.Matched(now));
            now += TimeSpan.FromSeconds(5);
        }
    }
}

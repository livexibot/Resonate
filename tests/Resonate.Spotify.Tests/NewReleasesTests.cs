using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class NewReleasesBuilderTests
{
    // Music.Now is 7 October 2026, 12:00 UTC.
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void A_release_date_counts_to_the_day_or_the_month_but_not_the_year_alone()
    {
        Assert.Equal(new DateOnly(2026, 11, 6), NewReleasesBuilder.ParseDate("2026-11-06"));
        Assert.Equal(new DateOnly(2026, 11, 1), NewReleasesBuilder.ParseDate("2026-11"));
        Assert.Null(NewReleasesBuilder.ParseDate("2026"));
        Assert.Null(NewReleasesBuilder.ParseDate("soon"));
        Assert.Null(NewReleasesBuilder.ParseDate(null));
    }

    [Fact]
    public void New_means_the_last_eight_weeks_and_a_day_ahead_for_time_zones()
    {
        Assert.True(NewReleasesBuilder.IsNew(Today, Today));
        Assert.True(NewReleasesBuilder.IsNew(Today.AddDays(-NewReleasesBuilder.WindowDays), Today));
        Assert.False(NewReleasesBuilder.IsNew(Today.AddDays(-NewReleasesBuilder.WindowDays - 1), Today));
        Assert.True(NewReleasesBuilder.IsNew(Today.AddDays(1), Today));
        Assert.False(NewReleasesBuilder.IsNew(Today.AddDays(2), Today));
    }

    [Fact]
    public void Reading_an_artists_releases_keeps_the_new_ones_and_the_newest_date_of_all()
    {
        var compilation = Releases.Album("comp", "x", Today.AddDays(-1));
        compilation.AlbumType = "compilation";
        var (recent, latest) = NewReleasesBuilder.Read(
            [
                Releases.Album("old", "x", Today.AddYears(-2)),
                Releases.Single("fresh", "x", Today.AddDays(-3), tracks: 5),
                compilation,
                new SimplifiedAlbum { Name = "No ID", ReleaseDate = Day(Today) },
                Releases.Single("fresh", "x", Today.AddDays(-3)),
                null,
            ],
            Today);

        var release = Assert.Single(recent);
        Assert.Equal("fresh", release.Id);
        Assert.Equal("Single fresh", release.Name);
        Assert.Equal("Artist x", release.Artists);
        Assert.Equal("EP", release.Kind);
        Assert.Equal("cover-fresh", release.ImageUrl);
        Assert.Equal(Day(Today.AddDays(-3)), latest);
    }

    [Fact]
    public void Singles_need_their_own_request_only_behind_a_full_page_of_albums()
    {
        var albums = Enumerable.Range(0, 10).Select(i => (SimplifiedAlbum?)Releases.Album($"a{i}", "x", Today.AddYears(-i))).ToList();
        var full = new Page<SimplifiedAlbum> { Items = albums, Total = 14, Next = "more" };
        var withSingle = new Page<SimplifiedAlbum> { Items = [.. albums[..9], Releases.Single("s", "x", Today)], Total = 14, Next = "more" };
        var short_ = new Page<SimplifiedAlbum> { Items = albums[..4], Total = 4 };
        var allOfThem = new Page<SimplifiedAlbum> { Items = albums, Total = 10 };

        Assert.True(NewReleasesBuilder.NeedsSingles(full));
        Assert.False(NewReleasesBuilder.NeedsSingles(withSingle));
        Assert.False(NewReleasesBuilder.NeedsSingles(short_));
        Assert.False(NewReleasesBuilder.NeedsSingles(allOfThem));
    }

    [Fact]
    public void Artists_never_asked_about_come_first_then_the_longest_ago_and_quiet_ones_wait_longer()
    {
        var now = Music.Now;
        var artists = new List<FollowedArtist>
        {
            new() { Id = "active-fresh", CheckedAt = now.AddHours(-3), Latest = Day(Today.AddDays(-30)) },
            new() { Id = "active-due", CheckedAt = now.AddHours(-21), Latest = Day(Today.AddDays(-30)) },
            new() { Id = "never" },
            new() { Id = "quiet-waiting", CheckedAt = now.AddDays(-2), Latest = Day(Today.AddYears(-3)) },
            new() { Id = "quiet-due", CheckedAt = now.AddDays(-5), Latest = Day(Today.AddYears(-3)) },
            new() { Id = "never-2" },
        };

        var due = NewReleasesBuilder.Due(artists, now, Today);

        Assert.Equal(["never", "never-2", "quiet-due", "active-due"], due.Select(a => a.Id));
    }

    [Fact]
    public void Home_shows_each_new_release_once_newest_first_with_albums_before_singles_on_the_same_day()
    {
        // Spotify lists some releases twice (a clean and an explicit version): the same name, artists and day.
        var explicitVersion = Release("z-explicit", "z", Today.AddDays(-9));
        explicitVersion.Name = "Release z-clean";
        var artists = new List<FollowedArtist>
        {
            Followed("x", Release("x-single", "x", Today.AddDays(-2), "single"), Release("x-album", "x", Today.AddDays(-2), "album")),
            Followed("y", Release("y-new", "y", Today), Release("together", "x, y", Today.AddDays(-5)), Release("aged", "y", Today.AddDays(-60))),
            Followed("z", Release("together", "x, y", Today.AddDays(-5)), Release("z-clean", "z", Today.AddDays(-9)), explicitVersion),
        };

        var shown = NewReleasesBuilder.Pick(artists, Today);

        Assert.Equal(["y-new", "x-album", "x-single", "together", "z-clean"], shown.Select(r => r.Id));
    }

    [Fact]
    public void Home_shows_forty_releases_at_most()
    {
        var artist = Followed("x", [.. Enumerable.Range(0, 60).Select(i => Release($"r{i}", "x", Today.AddDays(-(i % 50))))]);

        Assert.Equal(NewReleasesBuilder.MaxShown, NewReleasesBuilder.Pick([artist], Today).Count);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Yesterday")]
    [InlineData(3, "3 days ago")]
    [InlineData(8, "Last week")]
    [InlineData(20, "2 weeks ago")]
    [InlineData(55, "7 weeks ago")]
    public void When_a_release_came_out_reads_as_days_then_weeks(int daysAgo, string expected) =>
        Assert.Equal(expected, NewReleasesBuilder.When(Today.AddDays(-daysAgo), Today));

    [Fact]
    public void The_caption_says_what_it_is_and_when()
    {
        Assert.Equal("Single · Yesterday", NewReleasesBuilder.Caption(Release("a", "x", Today.AddDays(-1), "single"), Today));
        Assert.Equal("EP · Today", NewReleasesBuilder.Caption(Release("b", "x", Today, "single", tracks: 4), Today));
        Assert.Equal("Album · 3 days ago", NewReleasesBuilder.Caption(Release("c", "x", Today.AddDays(-3), "album", tracks: 4), Today));
    }

    [Fact]
    public void A_new_read_of_the_followed_artists_keeps_what_was_known_and_drops_those_no_longer_followed()
    {
        var checkedAt = Music.Now.AddHours(-2);
        var known = new List<FollowedArtist>
        {
            Followed("a", Release("a1", "a", Today)),
            Followed("b", Release("b1", "b", Today)),
        };
        known[0].CheckedAt = checkedAt;

        var merged = NewReleasesBuilder.Merge(known, [Artist("c"), Artist("a", "A renamed"), Artist("c")], complete: true);
        var partly = NewReleasesBuilder.Merge(known, [Artist("c")], complete: false);

        Assert.Equal(["c", "a"], merged.Select(a => a.Id));
        Assert.Equal("A renamed", merged[1].Name);
        Assert.Equal(checkedAt, merged[1].CheckedAt);
        Assert.Equal("a1", Assert.Single(merged[1].Releases).Id);
        Assert.Equal(["c", "a", "b"], partly.Select(a => a.Id));
    }

    internal static FollowedArtist Followed(string id, params NewRelease[] releases) =>
        new() { Id = id, Name = "Artist " + id, Releases = [.. releases] };

    internal static NewRelease Release(string id, string artists, DateOnly released, string type = "single", int tracks = 1) => new()
    {
        Id = id,
        Name = "Release " + id,
        Uri = "spotify:album:" + id,
        AlbumType = type,
        TotalTracks = tracks,
        ReleaseDate = Day(released),
        Artists = artists,
    };

    private static Artist Artist(string id, string? name = null) =>
        new() { Id = id, Name = name ?? "Artist " + id, Uri = "spotify:artist:" + id };

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed class NewReleasesFeedTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private readonly FakeTimeProvider _time = new(Music.Now);
    private readonly FakeWebApi _api = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-new-releases-" + Guid.NewGuid().ToString("N"));
    private string? _userId = "me";
    private bool _mayReadFollows = true;

    public NewReleasesFeedTests() => _time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Reads_every_followed_artist_then_asks_each_for_their_newest_releases()
    {
        Follow(60);
        _api.ReleasesOf["a0"] = [Releases.Single("a0-new", "a0", Today.AddDays(-1)), Releases.Album("a0-old", "a0", Today.AddYears(-1))];
        _api.ReleasesOf["a7"] = [Releases.Album("a7-new", "a7", Today.AddDays(-10))];
        var feed = Feed();

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["", "a49"], _api.FollowedRequests);
        Assert.Equal(60, _api.ReleaseRequests.Count);
        Assert.Equal("a0 Albums, Singles", _api.ReleaseRequests[0]);
        Assert.Equal(["a0-new", "a7-new"], feed.View.Releases.Select(r => r.Id));
        Assert.False(feed.View.NeedsSignIn);
    }

    [Fact]
    public async Task Asks_nothing_again_while_everything_is_up_to_date()
    {
        Follow(3);
        var feed = Feed();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var asked = Asked();

        _time.Advance(TimeSpan.FromHours(5));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(asked, Asked());
    }

    [Fact]
    public async Task Next_day_asks_artists_with_a_release_this_year_again_and_quiet_ones_after_four_days()
    {
        Follow(2);
        _api.ReleasesOf["a0"] = [Releases.Album("a0-recent", "a0", Today.AddMonths(-3))];
        _api.ReleasesOf["a1"] = [Releases.Album("a1-old", "a1", Today.AddYears(-3))];
        var feed = Feed();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        _api.ReleaseRequests.Clear();

        _time.Advance(TimeSpan.FromDays(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a0 Albums, Singles"], _api.ReleaseRequests);
        Assert.Equal(2, _api.FollowedRequests.Count);

        _time.Advance(TimeSpan.FromDays(3));
        _api.ReleaseRequests.Clear();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a0 Albums, Singles", "a1 Albums, Singles"], _api.ReleaseRequests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Asks_for_singles_on_their_own_when_a_page_of_albums_hides_them()
    {
        Follow(2);
        _api.ReleasesOf["a0"] = [.. Enumerable.Range(0, 12).Select(i => Releases.Album($"a0-album{i}", "a0", Today.AddYears(-1 - i))), Releases.Single("a0-single", "a0", Today.AddDays(-2))];
        _api.ReleasesOf["a1"] = [Releases.Album("a1-album", "a1", Today.AddYears(-1)), Releases.Single("a1-single", "a1", Today.AddDays(-4))];
        var feed = Feed();

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a0 Albums, Singles", "a0 Singles", "a1 Albums, Singles"], _api.ReleaseRequests);
        Assert.Equal(["a0-single", "a1-single"], feed.View.Releases.Select(r => r.Id));
    }

    [Fact]
    public async Task Sends_no_more_than_the_days_allowance_and_carries_on_the_next_day()
    {
        Follow(200);
        var feed = Feed();

        // Four pages of followed artists, then one request per artist while two more still fit (an artist may need two).
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, _api.FollowedRequests.Count);
        Assert.Equal(NewReleasesBuilder.MaxRequestsPerDay - 1, Asked());
        Assert.Equal("a144 Albums, Singles", _api.ReleaseRequests[^1]);

        _time.Advance(TimeSpan.FromHours(5));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NewReleasesBuilder.MaxRequestsPerDay - 1, Asked());

        // After midnight: the artists not reached yet.
        _api.ReleaseRequests.Clear();
        _time.Advance(TimeSpan.FromHours(12));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(55, _api.ReleaseRequests.Count);
        Assert.Equal("a145 Albums, Singles", _api.ReleaseRequests[0]);
        Assert.Equal("a199 Albums, Singles", _api.ReleaseRequests[^1]);
    }

    [Fact]
    public async Task A_sign_in_without_the_permission_asks_nothing_and_shows_sign_in_again()
    {
        Follow(3);
        _mayReadFollows = false;
        var feed = Feed();
        var changed = 0;
        feed.Changed += (_, _) => changed++;

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, Asked());
        Assert.True(feed.View.NeedsSignIn);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Spotify_refusing_for_want_of_the_permission_shows_sign_in_again_until_the_user_does()
    {
        Follow(3);
        _api.FollowedFailure = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Insufficient client scope");
        var feed = Feed();

        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(feed.View.NeedsSignIn);

        _time.Advance(TimeSpan.FromHours(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Single(_api.FollowedRequests);

        _api.FollowedFailure = null;
        feed.SignedInAgain();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(feed.View.NeedsSignIn);
        Assert.Equal(3, _api.ReleaseRequests.Count);
    }

    [Fact]
    public async Task A_failure_ends_the_round_keeps_what_was_found_and_waits_half_an_hour()
    {
        Follow(3);
        _api.ReleasesOf["a0"] = [Releases.Single("a0-new", "a0", Today)];
        _api.ReleaseFailures["a1"] = new HttpRequestException("offline");
        var feed = Feed();

        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a0 Albums, Singles", "a1 Albums, Singles"], _api.ReleaseRequests);
        Assert.Equal("a0-new", Assert.Single(feed.View.Releases).Id);

        _time.Advance(TimeSpan.FromMinutes(20));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _api.ReleaseRequests.Count);

        _api.ReleaseFailures.Clear();
        _time.Advance(TimeSpan.FromMinutes(15));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["a0 Albums, Singles", "a1 Albums, Singles", "a1 Albums, Singles", "a2 Albums, Singles"], _api.ReleaseRequests);
    }

    [Fact]
    public async Task Spotify_asking_to_wait_longer_than_half_an_hour_is_heeded()
    {
        Follow(2);
        _api.ReleaseFailures["a0"] = new SpotifyApiException(HttpStatusCode.TooManyRequests, "QUOTA_EXCEEDED", "Quota", TimeSpan.FromHours(3));
        var feed = Feed();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        _api.ReleaseFailures.Clear();

        _time.Advance(TimeSpan.FromHours(2));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Single(_api.ReleaseRequests);

        _time.Advance(TimeSpan.FromHours(1.5));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, _api.ReleaseRequests.Count);
    }

    [Fact]
    public async Task An_artist_gone_from_Spotify_does_not_end_the_round()
    {
        Follow(2);
        _api.ReleaseFailures["a0"] = new SpotifyApiException(HttpStatusCode.NotFound, null, "Not found");
        _api.ReleasesOf["a1"] = [Releases.Single("a1-new", "a1", Today)];
        var feed = Feed();

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _api.ReleaseRequests.Count);
        Assert.Equal("a1-new", Assert.Single(feed.View.Releases).Id);
    }

    [Fact]
    public async Task Releases_of_an_artist_no_longer_followed_go_when_the_list_is_read_again()
    {
        Follow(2);
        _api.ReleasesOf["a0"] = [Releases.Single("a0-new", "a0", Today)];
        _api.ReleasesOf["a1"] = [Releases.Single("a1-new", "a1", Today)];
        var feed = Feed();
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, feed.View.Releases.Count);

        _api.Followed.RemoveAt(0);
        _time.Advance(TimeSpan.FromDays(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a1-new", Assert.Single(feed.View.Releases).Id);
    }

    [Fact]
    public async Task What_was_found_is_kept_on_disk_for_next_time()
    {
        Follow(2);
        _api.ReleasesOf["a1"] = [Releases.Single("a1-new", "a1", Today)];
        var path = Path.Combine(_folder, "new-releases.json");
        await Feed(path).RefreshAsync(TestContext.Current.CancellationToken);
        var asked = Asked();

        var later = Feed(path);
        _time.Advance(TimeSpan.FromHours(2));
        await later.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a1-new", Assert.Single(later.View.Releases).Id);
        Assert.Equal(asked, Asked());
    }

    [Fact]
    public async Task Another_account_starts_afresh()
    {
        Follow(2);
        _api.ReleasesOf["a1"] = [Releases.Single("a1-new", "a1", Today)];
        var path = Path.Combine(_folder, "new-releases.json");
        await Feed(path).RefreshAsync(TestContext.Current.CancellationToken);

        _userId = "someone-else";
        _api.Followed.Clear();
        var other = Feed(path);
        await other.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Empty(other.View.Releases);
        Assert.Equal(2, _api.FollowedRequests.Count);
    }

    [Fact]
    public async Task Signing_out_forgets_the_releases_and_the_file()
    {
        Follow(1);
        _api.ReleasesOf["a0"] = [Releases.Single("a0-new", "a0", Today)];
        var path = Path.Combine(_folder, "new-releases.json");
        var feed = Feed(path);
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path));

        feed.Forget();

        Assert.Empty(feed.View.Releases);
        Assert.False(File.Exists(path));
    }

    private NewReleasesFeed Feed(string? path = null) =>
        new(_api, () => _userId, () => _mayReadFollows, path, TimeSpan.Zero, _time, TimeZoneInfo.Utc);

    private int Asked() => _api.FollowedRequests.Count + _api.ReleaseRequests.Count;

    private void Follow(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _api.Followed.Add(new Artist { Id = $"a{i}", Name = $"Artist a{i}", Uri = $"spotify:artist:a{i}" });
        }
    }
}

/// <summary>Releases as Spotify lists them under an artist.</summary>
internal static class Releases
{
    public static SimplifiedAlbum Album(string id, string artistId, DateOnly released) => Make(id, artistId, released, "album", 10);

    public static SimplifiedAlbum Single(string id, string artistId, DateOnly released, int tracks = 1) => Make(id, artistId, released, "single", tracks);

    private static SimplifiedAlbum Make(string id, string artistId, DateOnly released, string type, int tracks) => new()
    {
        Id = id,
        Name = (type == "album" ? "Album " : "Single ") + id,
        Uri = "spotify:album:" + id,
        AlbumType = type,
        TotalTracks = tracks,
        ReleaseDate = released.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Artists = [new SimplifiedArtist { Id = artistId, Name = "Artist " + artistId }],
        Images = [new SpotifyImage { Url = "cover-" + id, Width = 300 }],
    };
}

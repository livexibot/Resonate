using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class RediscoverBuilderTests
{
    // Music.Now is 7 October 2026, 12:00 UTC.
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void A_song_liked_years_ago_this_week_comes_back_on_this_day()
    {
        var liked = new[]
        {
            Liked("today", "x", Music.Now.AddYears(-3)),
            Liked("soon", "y", Music.Now.AddYears(-1).AddDays(2)),
            Liked("recent", "z", Music.Now.AddMonths(-10)),
            Liked("far", "w", Music.Now.AddYears(-2).AddDays(20)),
        };

        var cards = Build(liked).Cards.Where(c => c.Kind == RediscoverKind.OnThisDay).ToList();

        Assert.Equal(["Song today", "Song soon"], cards.Select(c => c.Title));
        Assert.Equal("Liked 3 years ago today", cards[0].Note);
        Assert.Equal("Liked a year ago this week", cards[1].Note);
        Assert.Equal("spotify:track:today", Assert.Single(cards[0].Tracks).Uri);
    }

    [Fact]
    public void The_29th_of_February_comes_round_on_the_28th()
    {
        Assert.Equal((2, 0), RediscoverBuilder.Anniversary(new DateOnly(2024, 2, 29), new DateOnly(2026, 2, 28), 0));
        Assert.Equal((1, -2), RediscoverBuilder.Anniversary(new DateOnly(2025, 12, 30), new DateOnly(2027, 1, 1), 3));
        Assert.Null(RediscoverBuilder.Anniversary(new DateOnly(2026, 10, 5), Today, 3));
    }

    [Fact]
    public void An_album_released_on_this_day_has_a_birthday_when_its_date_is_exact()
    {
        var exact = Liked("a1", "x", Music.Now.AddDays(-5)) with { AlbumId = "exact", Album = "Exact", ReleaseDate = "2001-10-07" };
        var yearOnly = Liked("b1", "y", Music.Now.AddDays(-5)) with { AlbumId = "year", Album = "Year", ReleaseDate = "2001" };
        var otherDay = Liked("c1", "z", Music.Now.AddDays(-5)) with { AlbumId = "other", Album = "Other", ReleaseDate = "2001-10-08" };

        var card = Assert.Single(Build([exact, yearOnly, otherDay]).Cards, c => c.Kind == RediscoverKind.AlbumBirthday);

        Assert.Equal("Exact", card.Title);
        Assert.Equal("Turns 25 today", card.Note);
        Assert.Equal("exact", card.AlbumId);
        Assert.Null(card.Track);
    }

    [Fact]
    public void Gathering_dust_waits_for_a_few_weeks_of_history()
    {
        var liked = new[] { Liked("old", "x", Music.Now.AddDays(-400)) };
        var history = new List<PlayRecord> { Music.Play("p", "y", Music.Now.AddDays(-10)) };

        var picks = Build(liked, history);

        Assert.False(picks.DustReady);
        Assert.DoesNotContain(picks.Cards, c => c.Kind == RediscoverKind.GatheringDust);
    }

    [Fact]
    public void Gathering_dust_is_old_likes_not_played_and_not_in_the_top_songs_while_the_history_is_short()
    {
        var liked = new[]
        {
            Liked("old", "x", Music.Now.AddDays(-400)),
            Liked("played", "x", Music.Now.AddDays(-400)),
            Liked("top", "x", Music.Now.AddDays(-400)),
            Liked("young", "x", Music.Now.AddDays(-100)),
        };
        var history = new List<PlayRecord>
        {
            Music.Play("played", "x", Music.Now.AddDays(-3)),
            Music.Play("other", "y", Music.Now.AddDays(-30)),
        };

        var shortHistory = Build(liked, history, top: ["spotify:track:top"]);
        var longHistory = Build(liked, [.. history, Music.Play("older", "y", Music.Now.AddDays(-70))], top: ["spotify:track:top"]);

        Assert.True(shortHistory.DustReady);
        Assert.Equal(["Song old"], Dust(shortHistory).Select(c => c.Title));
        Assert.Equal(["Song old", "Song top"], Dust(longHistory).Select(c => c.Title).Order());
        Assert.StartsWith("Liked ", Dust(shortHistory)[0].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dusty_card_plays_the_dusty_songs_from_itself_and_the_day_keeps_its_order()
    {
        var liked = Enumerable.Range(0, 12).Select(i => Liked("d" + i.ToString(CultureInfo.InvariantCulture), "x", Music.Now.AddDays(-300 - i))).ToList();
        var history = new List<PlayRecord> { Music.Play("other", "y", Music.Now.AddDays(-30)) };

        var first = Dust(Build(liked, history));
        var again = Dust(Build(liked, history));
        var playedOne = Dust(Build(liked, [.. history, Music.Play(first[0].Track!.Id!, "x", Music.Now.AddHours(-1))]));
        var tomorrow = Dust(Build(liked, history, now: Music.Now.AddDays(1)));

        Assert.Equal(RediscoverBuilder.MaxDustCards, first.Count);
        Assert.Equal(first.Select(c => c.Title), again.Select(c => c.Title));
        Assert.Equal(first.Skip(1).Select(c => c.Title), playedOne.Take(5).Select(c => c.Title));
        Assert.NotEqual(first.Select(c => c.Title), tomorrow.Select(c => c.Title));
        Assert.Equal(12, first[1].Tracks.Count);
        Assert.Equal(first[1].Track, first[1].Tracks[0]);
    }

    [Fact]
    public void Deep_cuts_are_favourite_albums_with_songs_not_liked_yet_in_album_order()
    {
        var liked = new[] { "t1", "t3", "t4" }
            .Select(id => Liked(id, "x", Music.Now.AddDays(-50)) with { AlbumId = "fav", Album = "Favourite" })
            .Append(Liked("u1", "y", Music.Now.AddDays(-50)) with { AlbumId = "few", Album = "Few" })
            .ToList();
        var albums = new Dictionary<string, RediscoverAlbum>
        {
            ["fav"] = Album("fav", "t1", "t2", "t3", "t4", "t5"),
            ["few"] = Album("few", "u1", "u2"),
        };

        var card = Assert.Single(Build(liked, albums: albums).Cards, c => c.Kind == RediscoverKind.DeepCut);

        Assert.Equal("Album fav", card.Title);
        Assert.Equal("2 songs you haven't liked yet", card.Note);
        Assert.Equal(["Song t2", "Song t5"], card.Tracks.Select(t => t.Title));
        Assert.Equal("DEEP CUTS", card.Label);
    }

    [Fact]
    public void Albums_are_asked_for_most_liked_first_then_the_oldest_within_the_budget()
    {
        var liked = new List<TrackInfo>();
        foreach (var (album, count) in new[] { ("big", 5), ("mid", 4), ("old", 3), ("fresh", 3), ("small", 2) })
        {
            liked.AddRange(Enumerable.Range(0, count).Select(i => Liked(album + i.ToString(CultureInfo.InvariantCulture), "x", Music.Now.AddDays(-9)) with { AlbumId = album }));
        }

        var albums = new Dictionary<string, RediscoverAlbum>
        {
            ["old"] = new() { Id = "old", FetchedAt = Music.Now.AddDays(-60) },
            ["fresh"] = new() { Id = "fresh", FetchedAt = Music.Now.AddDays(-5) },
        };

        Assert.Equal(["big", "mid", "old"], RediscoverBuilder.AlbumsToFetch(liked, albums, Music.Now, 5));
        Assert.Equal(["big"], RediscoverBuilder.AlbumsToFetch(liked, albums, Music.Now, 1));
        Assert.Empty(RediscoverBuilder.AlbumsToFetch(liked, albums, Music.Now, 0));
    }

    [Fact]
    public void Song_cards_come_first_then_dust_and_deep_cuts_take_turns()
    {
        var liked = new List<TrackInfo> { Liked("anniversary", "x", Music.Now.AddYears(-2)) };
        liked.AddRange(Enumerable.Range(0, 3).Select(i => Liked("d" + i.ToString(CultureInfo.InvariantCulture), "x", Music.Now.AddDays(-300))));
        liked.AddRange(new[] { "t1", "t2", "t3" }.Select(id => Liked(id, "x", Music.Now.AddDays(-20)) with { AlbumId = "fav" }));
        var albums = new Dictionary<string, RediscoverAlbum> { ["fav"] = Album("fav", "t1", "t2", "t3", "t4") };
        var history = new List<PlayRecord> { Music.Play("other", "y", Music.Now.AddDays(-30)) };

        var kinds = Build(liked, history, albums: albums).Cards.Select(c => c.Kind).ToList();

        Assert.Equal(
            [RediscoverKind.OnThisDay, RediscoverKind.GatheringDust, RediscoverKind.DeepCut, RediscoverKind.GatheringDust, RediscoverKind.GatheringDust],
            kinds);
    }

    internal static TrackInfo Liked(string id, string artistId, DateTimeOffset added) =>
        TrackInfo.From(Music.Song(id, artistId), addedAt: added)!;

    private static RediscoverAlbum Album(string id, params string[] songs) => new()
    {
        Id = id,
        Name = "Album " + id,
        Artists = "Artist x",
        FetchedAt = Music.Now,
        Tracks = songs.Select((s, i) => TrackInfo.From(Music.Song(s, "x"), position: i)! with { AlbumId = id }).ToList(),
    };

    private static List<RediscoverCard> Dust(RediscoverPicks picks) =>
        picks.Cards.Where(c => c.Kind == RediscoverKind.GatheringDust).ToList();

    private static RediscoverPicks Build(
        IReadOnlyList<TrackInfo> liked,
        IReadOnlyList<PlayRecord>? history = null,
        IReadOnlyCollection<string>? top = null,
        Dictionary<string, RediscoverAlbum>? albums = null,
        DateTimeOffset? now = null) =>
        RediscoverBuilder.Build(new RediscoverInput(
            liked,
            history ?? [],
            top ?? [],
            albums ?? [],
            now ?? Music.Now,
            TimeZoneInfo.Utc,
            CultureInfo.InvariantCulture));
}

public sealed class RediscoverFeedTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly FakeTimeProvider _time = new(Music.Now);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-rediscover-{Guid.NewGuid():N}");

    public RediscoverFeedTests()
    {
        // Ten albums with three liked songs each, and a fourth song on each not liked.
        for (var album = 0; album < 10; album++)
        {
            var albumId = "album" + album.ToString(CultureInfo.InvariantCulture);
            var songs = Enumerable.Range(0, 4).Select(i => Song(albumId, i)).ToList();
            foreach (var song in songs.Take(3))
            {
                _web.SavedTracks.Add(new SavedTrack { Track = song, AddedAt = "2025-01-01T00:00:00Z" });
            }

            _web.Albums[albumId] = new Album
            {
                Id = albumId,
                Name = "Album " + albumId,
                Uri = "spotify:album:" + albumId,
                Tracks = new Page<PlayableItem> { Items = songs.ToList<PlayableItem?>(), Total = songs.Count },
            };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Asks_for_a_few_albums_a_day_and_keeps_them()
    {
        var library = new LibraryService(_web, cache: null, _time);
        using var home = new HomeFeed(_web, library, new ListeningHistory(_web, null, _time), null, _time);
        var feed = new RediscoverFeed(library, home, Path.Combine(_folder, "rediscover.json"), _time, TimeZoneInfo.Utc);

        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var first = feed.StoredAlbums;
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var sameDay = feed.StoredAlbums;
        _time.Advance(TimeSpan.FromDays(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var nextDay = feed.StoredAlbums;

        // Read back from the file by a new feed (a new start): nothing more to ask for.
        var reopened = new RediscoverFeed(library, home, Path.Combine(_folder, "rediscover.json"), _time, TimeZoneInfo.Utc);
        await reopened.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RediscoverBuilder.AlbumFetchesPerDay, first);
        Assert.Equal(first, sameDay);
        Assert.Equal(10, nextDay);
        Assert.Equal(10, reopened.StoredAlbums);
        Assert.Equal(RediscoverBuilder.MaxDeepCuts, DeepCuts(reopened));
        Assert.All(feed.Picks!.Cards.Where(c => c.Kind == RediscoverKind.DeepCut), c => Assert.Equal("1 song you haven't liked yet", c.Note));
    }

    [Fact]
    public async Task Signing_out_forgets_the_albums()
    {
        var library = new LibraryService(_web, cache: null, _time);
        using var home = new HomeFeed(_web, library, new ListeningHistory(_web, null, _time), null, _time);
        var path = Path.Combine(_folder, "rediscover.json");
        var feed = new RediscoverFeed(library, home, path, _time, TimeZoneInfo.Utc);
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path));

        home.Forget();

        Assert.False(File.Exists(path));
        Assert.Null(feed.Picks);
    }

    private static int DeepCuts(RediscoverFeed feed) =>
        feed.Picks?.Cards.Count(c => c.Kind == RediscoverKind.DeepCut) ?? 0;

    private static PlayableItem Song(string albumId, int i)
    {
        var id = albumId + "-" + i.ToString(CultureInfo.InvariantCulture);
        return new PlayableItem
        {
            Id = id,
            Name = "Song " + id,
            Uri = "spotify:track:" + id,
            Type = "track",
            DurationMs = 200_000,
            Artists = [new SimplifiedArtist { Id = "x", Name = "Artist x" }],
            Album = new SimplifiedAlbum { Id = albumId, Name = "Album " + albumId, Uri = "spotify:album:" + albumId },
        };
    }
}

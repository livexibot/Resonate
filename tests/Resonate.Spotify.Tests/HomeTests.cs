using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>Songs, plays and artists for the Home tests. Artist "x" is called "Artist x".</summary>
internal static class Music
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T12:00:00Z", CultureInfo.InvariantCulture);

    public static PlayableItem Song(string id, string artistId, int seconds = 200, string? featuring = null)
    {
        var artists = new List<SimplifiedArtist> { Artist(artistId) };
        if (featuring is not null)
        {
            artists.Add(Artist(featuring));
        }

        return new PlayableItem
        {
            Id = id,
            Name = "Song " + id,
            Uri = "spotify:track:" + id,
            Type = "track",
            DurationMs = seconds * 1000,
            Artists = artists,
            Album = new SimplifiedAlbum
            {
                Id = "album-" + id,
                Name = "Album " + id,
                Uri = "spotify:album:album-" + id,
                Images = [new SpotifyImage { Url = "https://img/" + id, Width = 300 }],
            },
        };
    }

    /// <summary><paramref name="count"/> songs by one artist, with IDs such as "a0", "a1".</summary>
    public static IEnumerable<TrackInfo> Songs(string artistId, int count) =>
        Enumerable.Range(0, count).Select(i => TrackInfo.From(Song(artistId + i.ToString(CultureInfo.InvariantCulture), artistId))!);

    public static PlayHistoryItem Played(PlayableItem song, DateTimeOffset at, string? context = null) => new()
    {
        Track = song,
        PlayedAt = at.ToString("O", CultureInfo.InvariantCulture),
        Context = context is null ? null : new PlaybackContext { Uri = context },
    };

    public static PlayRecord Play(string id, string artistId, DateTimeOffset at, int seconds = 200) =>
        PlayRecord.From(Played(Song(id, artistId, seconds), at))!;

    /// <summary>One listening session: a song by each artist, a few minutes apart, ending at <paramref name="end"/>.</summary>
    public static List<PlayRecord> Session(DateTimeOffset end, params string[] artistIds) =>
        artistIds.Select((a, i) => Play(a + "-played", a, end.AddMinutes(-4 * (artistIds.Length - 1 - i)))).ToList();

    public static MixSeed Seed(string artistId) => new(artistId, "Artist " + artistId, "https://img/artist-" + artistId);

    private static SimplifiedArtist Artist(string id) => new() { Id = id, Name = "Artist " + id };
}

public sealed class ListeningHistoryTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly FakeTimeProvider _time = new(Music.Now);
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"resonate-history-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_file);
        File.Delete(_file + ".tmp");
    }

    [Fact]
    public async Task Saves_new_plays_newest_first_and_asks_only_for_plays_after_the_newest_known()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("b", "x"), Music.Now.AddMinutes(-10)));
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a", "x"), Music.Now.AddMinutes(-20)));
        using var history = History();

        var first = await history.SyncAsync(TestContext.Current.CancellationToken);
        _web.RecentlyPlayed.Insert(0, Music.Played(Music.Song("c", "y"), Music.Now.AddMinutes(-1)));
        _time.Advance(ListeningHistory.MinSyncInterval);
        var second = await history.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal(["spotify:track:c", "spotify:track:b", "spotify:track:a"], history.Plays.Select(p => p.Uri));
        Assert.Equal(new DateTimeOffset?[] { null, Music.Now.AddMinutes(-10) }, _web.RecentlyPlayedRequests);
    }

    [Fact]
    public async Task Opening_Home_again_within_a_minute_costs_no_request()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a", "x"), Music.Now.AddMinutes(-3)));
        using var history = History();

        await history.SyncAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(30));
        await history.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Single(_web.RecentlyPlayedRequests);
    }

    [Fact]
    public void The_same_song_at_the_same_moment_is_one_play_and_at_another_moment_another()
    {
        var existing = new[] { Music.Play("a", "x", Music.Now.AddHours(-2)), Music.Play("a", "x", Music.Now.AddHours(-3)) };
        var incoming = new[] { Music.Play("a", "x", Music.Now.AddHours(-1)), Music.Play("a", "x", Music.Now.AddHours(-2)) };

        var merged = ListeningHistory.Merge(existing, incoming, Music.Now);

        Assert.Equal([Music.Now.AddHours(-1), Music.Now.AddHours(-2), Music.Now.AddHours(-3)], merged.Select(p => p.PlayedAt));
    }

    [Fact]
    public async Task Plays_are_kept_on_disk_for_ninety_days()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("new", "x"), Music.Now.AddDays(-1)));
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("old", "x"), Music.Now.AddDays(-89)));
        using (var history = History())
        {
            await history.SyncAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromDays(2));
        using var reopened = History();
        reopened.Load();

        var play = Assert.Single(reopened.Plays);
        Assert.Equal("spotify:track:new", play.Uri);
        Assert.Equal("Artist x", play.Artists[0].Name);
        Assert.Equal(Music.Now.AddDays(-1), play.PlayedAt);
    }

    [Fact]
    public async Task Clearing_forgets_the_plays_on_disk_too()
    {
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a", "x"), Music.Now.AddMinutes(-3)));
        using var history = History();
        await history.SyncAsync(TestContext.Current.CancellationToken);

        history.Clear();
        using var reopened = History();
        reopened.Load();

        Assert.Empty(history.Plays);
        Assert.Empty(reopened.Plays);
    }

    [Fact]
    public void A_play_keeps_what_song_lists_need()
    {
        var item = Music.Played(Music.Song("a", "x", seconds: 215, featuring: "y"), Music.Now, context: "spotify:playlist:p");

        var play = PlayRecord.From(item)!;
        var track = play.ToTrack();

        Assert.Equal("spotify:playlist:p", play.ContextUri);
        Assert.Equal("spotify:track:a", track.Uri);
        Assert.Equal("a", track.Id);
        Assert.Equal("Artist x, Artist y", track.Artists);
        Assert.Equal(["x", "y"], track.ArtistRefs.Select(a => a.Id));
        Assert.Equal("album-a", track.AlbumId);
        Assert.Equal("spotify:album:album-a", track.AlbumUri);
        Assert.Equal("https://img/a", track.LargeImageUrl);
        Assert.Equal(TimeSpan.FromSeconds(215), track.Duration);
        Assert.True(track.IsPlayable);
    }

    [Fact]
    public void Plays_without_a_song_or_a_time_are_skipped()
    {
        Assert.Null(PlayRecord.From(new PlayHistoryItem { PlayedAt = "2026-10-07T12:00:00Z" }));
        Assert.Null(PlayRecord.From(new PlayHistoryItem { Track = Music.Song("a", "x"), PlayedAt = "not a time" }));
    }

    private ListeningHistory History() => new(_web, _file, _time);
}

public sealed class ListeningStatsTests
{
    private static readonly DateTimeOffset Now = Music.Now;

    [Fact]
    public void The_past_day_and_week_are_counted_separately()
    {
        var plays = new[]
        {
            Music.Play("a", "x", Now.AddHours(-1), seconds: 180),
            Music.Play("a", "x", Now.AddHours(-2), seconds: 180),
            Music.Play("b", "y", Now.AddHours(-3), seconds: 240),
            Music.Play("c", "y", Now.AddDays(-3), seconds: 300),
            Music.Play("d", "y", Now.AddDays(-4), seconds: 300),
            Music.Play("e", "z", Now.AddDays(-8), seconds: 300),
        };

        var day = ListeningStats.Summarize(plays, Now, ListeningStats.Day);
        var week = ListeningStats.Summarize(plays, Now, ListeningStats.Week);

        Assert.Equal(3, day.Songs);
        Assert.Equal(TimeSpan.FromSeconds(600), day.Listened);
        Assert.Equal("x", day.TopArtist?.Id);
        Assert.Equal(2, day.TopArtistPlays);
        Assert.Equal("spotify:track:a", day.TopSong?.Uri);
        Assert.Equal(2, day.TopSongPlays);

        Assert.Equal(5, week.Songs);
        Assert.Equal(TimeSpan.FromSeconds(1200), week.Listened);
        Assert.Equal("y", week.TopArtist?.Id);
        Assert.Equal(3, week.TopArtistPlays);
    }

    [Fact]
    public void Ties_go_to_more_listening_time_for_artists_and_to_the_latest_song()
    {
        var plays = new[]
        {
            Music.Play("short", "x", Now.AddMinutes(-5), seconds: 120),
            Music.Play("long", "y", Now.AddMinutes(-30), seconds: 400),
        };

        var day = ListeningStats.Summarize(plays, Now, ListeningStats.Day);

        Assert.Equal("y", day.TopArtist?.Id);
        Assert.Equal("spotify:track:short", day.TopSong?.Uri);
        Assert.Equal(1, day.TopSongPlays);
    }

    [Fact]
    public void Nothing_played_is_an_empty_summary()
    {
        var day = ListeningStats.Summarize([Music.Play("a", "x", Now.AddDays(-2))], Now, ListeningStats.Day);

        Assert.Equal(ListeningSummary.Empty, day);
        Assert.Null(day.TopArtist);
    }

    [Fact]
    public void Recent_songs_are_each_shown_once_newest_first_without_local_files()
    {
        var local = Music.Play("l", "x", Now.AddMinutes(-1));
        local.Uri = "spotify:local:Me:Demo:Song:120";
        var plays = new[]
        {
            local,
            Music.Play("a", "x", Now.AddMinutes(-2)),
            Music.Play("b", "x", Now.AddMinutes(-3)),
            Music.Play("a", "x", Now.AddMinutes(-4)),
            Music.Play("c", "x", Now.AddMinutes(-5)),
        };

        var recent = ListeningStats.RecentSongs(plays, 2);

        Assert.Equal(["spotify:track:a", "spotify:track:b"], recent.Select(p => p.Uri));
    }

    [Fact]
    public void Top_artists_from_the_history_are_counted_by_first_artist()
    {
        var plays = new[]
        {
            Music.Play("a", "x", Now.AddMinutes(-1)),
            Music.Play("b", "y", Now.AddMinutes(-2)),
            Music.Play("c", "y", Now.AddMinutes(-3)),
        };

        Assert.Equal(["y", "x"], ListeningStats.TopArtists(plays, 5).Select(a => a.Id));
    }
}

public sealed class DailyMixTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void A_mix_takes_the_seeds_liked_songs_and_those_of_artists_that_go_with_it()
    {
        var liked = Music.Songs("a", 12).Concat(Music.Songs("c", 6)).Concat(Music.Songs("d", 6)).Concat(Music.Songs("e", 6)).ToList();
        IReadOnlyList<TrackInfo>[] playlists = [[.. Music.Songs("a", 1), .. Music.Songs("c", 1)]];
        var history = Music.Session(Music.Now, "a", "d");

        var mixes = DailyMixBuilder.Build([Music.Seed("a")], liked, playlists, history, Today);

        var mix = mixes[0];
        Assert.Equal(1, mix.Number);
        Assert.Equal("Daily Mix 1", mix.Title);
        Assert.Equal("a", mix.SeedId);
        Assert.Equal("https://img/artist-a", mix.ImageUrl);
        Assert.Equal("Artist a", mix.ArtistNames[0]);
        Assert.Equal(["Artist c", "Artist d"], mix.ArtistNames.Skip(1).Order());
        Assert.Equal(12 + 5 + 5, mix.Tracks.Count);
        Assert.Equal(12, mix.Tracks.Count(t => t.ArtistRefs[0].Id == "a"));
        Assert.DoesNotContain(mix.Tracks, t => t.ArtistRefs[0].Id == "e");
    }

    [Fact]
    public void The_order_stays_the_same_all_day_and_changes_the_next()
    {
        var liked = Music.Songs("a", 30).ToList();

        var today = Uris(DailyMixBuilder.Build([Music.Seed("a")], liked, [], [], Today));
        var again = Uris(DailyMixBuilder.Build([Music.Seed("a")], liked, [], [], Today));
        var tomorrow = Uris(DailyMixBuilder.Build([Music.Seed("a")], liked, [], [], Today.AddDays(1)));

        Assert.Equal(30, today.Count);
        Assert.Equal(today, again);
        Assert.NotEqual(today, tomorrow);
        Assert.Equal(today.Order(), tomorrow.Order());
    }

    [Fact]
    public void A_mix_keeps_its_own_order_when_shown_in_mix_order()
    {
        // Liked Songs come numbered by their place in Liked Songs.
        var liked = Music.Songs("a", 15).Concat(Music.Songs("b", 15)).Select((t, i) => t with { Position = i }).ToList();
        IReadOnlyList<TrackInfo>[] playlists = [liked];

        var mix = DailyMixBuilder.Build([Music.Seed("a")], liked, playlists, [], Today)[0];

        Assert.Equal(Enumerable.Range(0, mix.Tracks.Count), mix.Tracks.Select(t => t.Position ?? -1));
        Assert.Equal(mix.Tracks.Select(t => t.Uri), TrackSorter.Apply(mix.Tracks, TrackSort.Default).Select(t => t.Uri));
        Assert.NotEqual(liked.Where(t => mix.Tracks.Any(m => m.Uri == t.Uri)).Select(t => t.Uri), mix.Tracks.Select(t => t.Uri));
    }

    [Fact]
    public void A_song_is_in_one_mix_only()
    {
        // Eight artists that all share one playlist: the first mixes take most of the songs.
        var liked = Enumerable.Range(0, 8).SelectMany(i => Music.Songs("s" + i.ToString(CultureInfo.InvariantCulture), 12)).ToList();
        var seeds = Enumerable.Range(0, 8).Select(i => Music.Seed("s" + i.ToString(CultureInfo.InvariantCulture))).ToList();

        var mixes = DailyMixBuilder.Build(seeds, liked, [liked], [], Today);

        var all = mixes.SelectMany(m => m.Tracks).Select(t => t.Uri).ToList();
        Assert.True(mixes.Count >= 2);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(mixes, m => Assert.InRange(m.Tracks.Count, DailyMixBuilder.MinSongs, DailyMixBuilder.MaxSongs));
    }

    [Fact]
    public void At_most_six_mixes_are_made()
    {
        var ids = Enumerable.Range(0, 8).Select(i => "s" + i.ToString(CultureInfo.InvariantCulture)).ToList();
        var liked = ids.SelectMany(id => Music.Songs(id, 12)).ToList();

        var mixes = DailyMixBuilder.Build(ids.Select(Music.Seed).ToList(), liked, [], [], Today);

        Assert.Equal([1, 2, 3, 4, 5, 6], mixes.Select(m => m.Number));
        Assert.Equal(ids.Take(6), mixes.Select(m => m.SeedId));
    }

    [Fact]
    public void A_mix_is_never_longer_than_fifty_songs()
    {
        var friends = Enumerable.Range(0, 12).Select(i => "f" + i.ToString(CultureInfo.InvariantCulture)).ToList();
        var liked = Music.Songs("a", 25).Concat(friends.SelectMany(f => Music.Songs(f, 8))).ToList();
        IReadOnlyList<TrackInfo>[] playlists = [liked];

        var mixes = DailyMixBuilder.Build([Music.Seed("a")], liked, playlists, [], Today);

        Assert.Equal(DailyMixBuilder.MaxSongs, mixes[0].Tracks.Count);
    }

    [Fact]
    public void Artists_with_too_few_songs_get_no_mix()
    {
        Assert.Empty(DailyMixBuilder.Build([Music.Seed("a")], Music.Songs("a", 5).ToList(), [], [], Today));
    }

    [Fact]
    public void Without_top_artists_the_most_played_then_the_most_liked_artists_lead()
    {
        var liked = Music.Songs("a", 12).Concat(Music.Songs("b", 15)).ToList();
        List<PlayRecord> history = [Music.Play("a0", "a", Music.Now.AddDays(-1)), Music.Play("a1", "a", Music.Now.AddDays(-2))];

        var mixes = DailyMixBuilder.Build([], liked, [], history, Today);

        Assert.Equal(["a", "b"], mixes.Select(m => m.SeedId));
        Assert.Null(mixes[0].ImageUrl);
        Assert.Equal("Artist a", mixes[0].Subtitle);
    }

    [Fact]
    public void Songs_by_one_artist_are_spread_apart_when_possible()
    {
        var a = Music.Songs("a", 3).ToList();
        var b = Music.Songs("b", 3).ToList();

        var spread = DailyMixBuilder.Spread([.. a, .. b]);

        for (var i = 1; i < spread.Count; i++)
        {
            Assert.NotEqual(spread[i - 1].PrimaryArtist, spread[i].PrimaryArtist);
        }
    }

    [Fact]
    public void Plays_far_apart_are_separate_sessions()
    {
        List<PlayRecord> history =
        [
            Music.Play("a", "x", Music.Now),
            Music.Play("b", "y", Music.Now.AddMinutes(-5)),
            Music.Play("c", "z", Music.Now.AddHours(-3)),
        ];

        var sessions = DailyMixBuilder.Sessions(history).ToList();

        Assert.Equal([1, 2], sessions.Select(s => s.Count));
    }

    private static List<string?> Uris(List<DailyMix> mixes) => mixes[0].Tracks.Select(t => t.Uri).ToList();
}

public sealed class HomeFeedTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly FakeTimeProvider _time = new(Music.Now);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-home-{Guid.NewGuid():N}");

    public HomeFeedTests()
    {
        foreach (var song in Music.Songs("a", 12).Concat(Music.Songs("c", 6)))
        {
            _web.SavedTracks.Add(new SavedTrack { Track = Music.Song(song.Id!, song.ArtistRefs[0].Id!), AddedAt = "2026-01-01T00:00:00Z" });
        }

        _web.TopArtists.Add(new Artist { Id = "a", Name = "Artist a", Images = [new SpotifyImage { Url = "https://img/artist-a", Width = 320 }] });
        _web.TopTracks.Add(Music.Song("a0", "a"));
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("c0", "c"), Music.Now.AddMinutes(-5)));
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a0", "a"), Music.Now.AddMinutes(-9)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Mixes_are_made_once_a_day()
    {
        using var library = Library();
        using var feed = Feed(library);

        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var first = feed.Content;
        var requests = _web.TopArtistRequests.Count;
        _time.Advance(TimeSpan.FromHours(3));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var later = feed.Content;
        _time.Advance(TimeSpan.FromDays(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        var mix = Assert.Single(first!.Mixes);
        Assert.Equal("a", mix.SeedId);
        Assert.Contains(mix.Tracks, t => t.ArtistRefs[0].Id == "c");
        Assert.Equal(["spotify:track:a0"], first.OnRepeat.Select(t => t.Uri));
        Assert.Same(first, later);
        Assert.Equal(requests, _web.TopArtistRequests.Count - 3);
        Assert.Equal(new DateOnly(2026, 10, 8), feed.Content!.Day);
    }

    [Fact]
    public async Task Plays_and_mixes_are_there_at_once_after_a_restart()
    {
        List<string?> uris;
        using (var library = Library())
        using (var feed = Feed(library))
        {
            await feed.RefreshAsync(TestContext.Current.CancellationToken);
            uris = feed.Content!.Mixes[0].Tracks.Select(t => t.Uri).ToList();
        }

        using var reopenedLibrary = Library();
        using var reopened = Feed(reopenedLibrary);
        reopened.LoadStored();

        Assert.True(reopened.IsLoaded);
        Assert.Equal(2, reopened.History.Plays.Count);
        Assert.Equal(uris, reopened.Content!.Mixes[0].Tracks.Select(t => t.Uri));
        Assert.Equal("https://img/artist-a", reopened.KnownArtistImage("a"));
        Assert.Equal([TopRange.ShortTerm, TopRange.MediumTerm, TopRange.LongTerm], reopened.Content.Top.Select(t => t.Range));
        Assert.Equal("Artist a", reopened.Content.TopFor(TopRange.LongTerm)!.Artists[0].Name);
        Assert.False(reopened.NeedsMixes());
        Assert.False(reopened.NeedsTop());
    }

    [Fact]
    public async Task Spotifys_top_artists_and_songs_come_for_each_time_range()
    {
        _web.TopArtistsIn[TopRange.MediumTerm] = [new Artist { Id = "c", Name = "Artist c" }, new Artist { Id = "a", Name = "Artist a" }];
        _web.TopArtistsIn[TopRange.LongTerm] = Enumerable.Range(0, 15).Select(i => new Artist { Id = $"x{i}", Name = $"Artist x{i}" }).ToList();
        _web.TopTracksIn[TopRange.ShortTerm] = Enumerable.Range(0, 12).Select(i => Music.Song($"a{i}", "a")).ToList();
        _web.TopTracksIn[TopRange.MediumTerm] = [Music.Song("c1", "c"), Music.Song("a0", "a")];
        using var library = Library();
        using var feed = Feed(library);

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        var content = feed.Content!;
        Assert.Equal(["a"], content.TopFor(TopRange.ShortTerm)!.Artists.Select(a => a.Id));
        Assert.Equal("https://img/artist-a", content.TopFor(TopRange.ShortTerm)!.Artists[0].ImageUrl);
        Assert.Equal(["c", "a"], content.TopFor(TopRange.MediumTerm)!.Artists.Select(a => a.Id));
        Assert.Equal(HomeFeed.TopSize, content.TopFor(TopRange.LongTerm)!.Artists.Count);

        // Home shows the first ten songs; "On repeat" keeps them all.
        Assert.Equal(HomeFeed.TopSize, content.TopFor(TopRange.ShortTerm)!.Songs.Count);
        Assert.Equal(12, content.OnRepeat.Count);
        Assert.Equal(["spotify:track:c1", "spotify:track:a0"], content.TopFor(TopRange.MediumTerm)!.Songs.Select(t => t.Uri));

        // The mixes still grow from the past four weeks' artists first.
        Assert.Equal("a", content.Mixes[0].SeedId);
    }

    [Fact]
    public async Task Missing_top_lists_are_asked_for_again_without_making_the_mixes_again()
    {
        // Today's mixes were made while Spotify did not answer.
        _web.TopItemsFailure = new SpotifyApiException(HttpStatusCode.TooManyRequests, null, "Too many requests");
        using var library = Library();
        using var feed = Feed(library);
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var first = feed.Content!;
        Assert.Empty(first.Top);

        // A moment later nothing is asked again.
        _web.TopItemsFailure = null;
        var requests = _web.TopArtistRequests.Count;
        _time.Advance(TimeSpan.FromMinutes(2));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(requests, _web.TopArtistRequests.Count);

        _time.Advance(TimeSpan.FromMinutes(15));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        var later = feed.Content!;
        Assert.Equal(3, later.Top.Count);
        Assert.Same(first.Mixes, later.Mixes);
        Assert.Equal(["spotify:track:a0"], later.OnRepeat.Select(t => t.Uri));
        Assert.False(feed.NeedsTop());
    }

    [Fact]
    public async Task Without_permission_for_top_artists_mixes_come_from_the_history_and_liked_songs()
    {
        _web.TopItemsFailure = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Insufficient client scope");
        _web.RecentlyPlayed.Add(Music.Played(Music.Song("a1", "a"), Music.Now.AddMinutes(-13)));
        using var library = Library();
        using var feed = Feed(library);

        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a", Assert.Single(feed.Content!.Mixes).SeedId);
        Assert.Empty(feed.Content.OnRepeat);
        Assert.Empty(feed.Content.Top);

        // The first refusal ends the round: one request, not one per time range.
        Assert.Equal([TopRange.ShortTerm], _web.TopArtistRequests);
    }

    [Fact]
    public async Task Own_playlists_tell_which_artists_go_together_and_load_once_per_version()
    {
        _web.RecentlyPlayed.Clear();
        _web.Playlists.Add(new SimplifiedPlaylist
        {
            Id = "p",
            Name = "Mine",
            Uri = "spotify:playlist:p",
            Owner = new PlaylistOwner { Id = "me" },
            SnapshotId = "v1",
            Items = new ItemsReference { Total = 2 },
        });
        _web.PlaylistEntries["p"] = [new PlaylistEntry { Item = Music.Song("a0", "a") }, new PlaylistEntry { Item = Music.Song("c0", "c") }];
        using var library = Library();
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        using var feed = Feed(library);

        await feed.RefreshAsync(TestContext.Current.CancellationToken);
        var reads = _web.PlaylistItemReads;
        _time.Advance(TimeSpan.FromDays(1));
        await feed.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Contains(feed.Content!.Mixes[0].Tracks, t => t.ArtistRefs[0].Id == "c");
        Assert.Equal(1, reads);
        Assert.Equal(reads, _web.PlaylistItemReads);
    }

    [Fact]
    public async Task An_artist_picture_is_asked_for_once()
    {
        _web.Artists["z"] = new Artist { Id = "z", Name = "Artist z", Images = [new SpotifyImage { Url = "https://img/artist-z", Width = 320 }] };
        using var library = Library();
        using var feed = Feed(library);
        feed.LoadStored();

        var first = await feed.GetArtistImageAsync("z", TestContext.Current.CancellationToken);
        var second = await feed.GetArtistImageAsync("z", TestContext.Current.CancellationToken);

        Assert.Equal("https://img/artist-z", first);
        Assert.Equal(first, second);
        Assert.Equal(["z"], _web.ArtistRequests);
    }

    [Fact]
    public async Task Forgetting_clears_the_plays_and_mixes_on_disk()
    {
        using (var library = Library())
        using (var feed = Feed(library))
        {
            await feed.RefreshAsync(TestContext.Current.CancellationToken);
            feed.Forget();
            Assert.Null(feed.Content);
            Assert.Empty(feed.History.Plays);
        }

        using var reopenedLibrary = Library();
        using var reopened = Feed(reopenedLibrary);
        reopened.LoadStored();

        Assert.Null(reopened.Content);
        Assert.Empty(reopened.History.Plays);
    }

    private LibraryService Library() => new(_web, cache: null, _time, new TrackListStore(Path.Combine(_folder, "lists")));

    private HomeFeed Feed(LibraryService library) =>
        new(_web, library, new ListeningHistory(_web, Path.Combine(_folder, "history.json"), _time), Path.Combine(_folder, "home.json"), _time);
}

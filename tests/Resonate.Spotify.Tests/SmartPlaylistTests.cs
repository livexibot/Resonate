using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class SmartPlaylistTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00Z", CultureInfo.InvariantCulture);

    private readonly FakeWebApi _web = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-smart-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Every_rule_must_hold()
    {
        var songs = new[]
        {
            Song("a", saved: "2023-05-01", released: 1995, seconds: 400),
            Song("b", saved: "2023-06-01", released: 2005, seconds: 400),
            Song("c", saved: "2022-06-01", released: 1990, seconds: 400),
            Song("d", saved: "2023-07-01", released: 1985, seconds: 200),
        };
        var playlist = Liked(
            new SmartRule { Kind = SmartRuleKind.SavedInYears, Value = 2023, Value2 = 2023 },
            new SmartRule { Kind = SmartRuleKind.ReleasedBefore, Value = 2000 },
            new SmartRule { Kind = SmartRuleKind.LongerThan, Value = 300 });

        Assert.Equal(["a"], Ids(Evaluate(playlist, songs)));
    }

    [Fact]
    public void Days_ranges_artists_and_explicit_songs_are_checked()
    {
        var songs = new[]
        {
            Song("new", saved: "2026-10-01", released: 1999, artist: "Mira Sol", isExplicit: true),
            Song("old", saved: "2026-08-01", released: 1990, artist: "Lumen"),
            Song("unknown", saved: "2026-10-07", released: null, artist: "mira sol"),
        };

        Assert.Equal(["new", "unknown"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.SavedInLastDays, Value = 30 }), songs)));
        Assert.Equal(["new", "old"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.ReleasedBetween, Value = 1999, Value2 = 1990 }), songs)));
        Assert.Equal(["new", "unknown"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.ArtistIs, Text = " MIRA SOL " }), songs)));
        Assert.Equal(["old"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.ArtistIsNot, Text = "Mira Sol" }), songs)));
        Assert.Equal(["new"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.Explicit }), songs)));
        Assert.Equal(["old", "unknown"], Ids(Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.NotExplicit }), songs)));

        // A rule still being filled in lets everything through.
        Assert.Equal(3, Evaluate(Liked(new SmartRule { Kind = SmartRuleKind.ArtistIs }), songs).Count);
    }

    [Fact]
    public void Songs_in_another_playlist_local_files_and_repeats_are_left_out()
    {
        var songs = new[]
        {
            Song("a"),
            Song("b"),
            Song("a"),
            Song("c") with { IsLocal = true },
        };
        var playlist = Liked(new SmartRule { Kind = SmartRuleKind.NotInPlaylist, Text = "gym" });
        var inputs = new SmartInputs(songs, new Dictionary<string, IReadOnlySet<string>> { ["gym"] = new HashSet<string> { "spotify:track:b" } });

        Assert.Equal(["a"], Ids(SmartPlaylistEvaluator.Evaluate(playlist, inputs, Now, TimeZoneInfo.Utc)));
    }

    [Fact]
    public void Order_and_limit_apply_after_the_rules()
    {
        var songs = new[]
        {
            Song("mid", released: 1995, seconds: 300),
            Song("unknown", released: null, seconds: 100),
            Song("old", released: 1970, seconds: 500),
            Song("new", released: 2020, seconds: 200),
        };

        var oldest = Liked();
        oldest.Order = SmartOrder.OldestRelease;
        Assert.Equal(["old", "mid", "new", "unknown"], Ids(Evaluate(oldest, songs)));

        var longest = Liked();
        longest.Order = SmartOrder.Longest;
        longest.Limit = 2;
        Assert.Equal(["old", "mid"], Ids(Evaluate(longest, songs)));

        var random = Liked();
        random.Order = SmartOrder.Random;
        random.Seed = 7;
        Assert.Equal(Ids(Evaluate(random, songs)), Ids(Evaluate(random, songs)));
        Assert.Equal(4, Evaluate(random, songs).Count);
    }

    [Fact]
    public void The_rules_read_as_a_sentence()
    {
        var playlist = Liked(
            new SmartRule { Kind = SmartRuleKind.SavedInYears, Value = 2023, Value2 = 2023 },
            new SmartRule { Kind = SmartRuleKind.ReleasedBefore, Value = 2000 },
            new SmartRule { Kind = SmartRuleKind.LongerThan, Value = 300 });

        Assert.Equal("Songs from Liked Songs, saved in 2023, released before 2000, longer than 5 min", SmartPlaylistText.Sentence(playlist));
        Assert.Equal("4 min 30 sec", SmartPlaylistText.Length(270));
        Assert.Equal(("released", "in 1990–1999"), SmartPlaylistText.Describe(new SmartRule { Kind = SmartRuleKind.ReleasedBetween, Value = 1990, Value2 = 1999 }));
        Assert.Equal("at most 50 songs", SmartPlaylistText.DescribeLimit(50));
    }

    [Fact]
    public void Starters_set_their_rules()
    {
        var playlist = Liked(new SmartRule { Kind = SmartRuleKind.Explicit });
        playlist.SourcePlaylistId = "p";

        SmartStarters.Apply(playlist, "The 90s");

        Assert.Equal("The 90s", playlist.Name);
        Assert.True(playlist.FromLikedSongs);
        var rule = Assert.Single(playlist.Rules);
        Assert.Equal(SmartRuleKind.ReleasedBetween, rule.Kind);
        Assert.Equal(SmartOrder.OldestRelease, playlist.Order);
        Assert.True(SmartPlaylistEvaluator.NeedsReleaseYears(playlist));
    }

    [Theory]
    [InlineData("1999-03-01", 1999)]
    [InlineData("1999", 1999)]
    [InlineData("0000", null)]
    [InlineData(null, null)]
    public void Release_years_come_from_the_album(string? date, int? year)
    {
        var track = TrackInfo.From(new PlayableItem { Name = "S", Uri = "spotify:track:s", Album = new SimplifiedAlbum { Name = "A", ReleaseDate = date } });

        Assert.Equal(year, track!.ReleaseYear);
    }

    [Fact]
    public async Task Keeping_on_Spotify_makes_the_playlist_once_then_replaces_its_songs()
    {
        for (var i = 0; i < 150; i++)
        {
            _web.SavedTracks.Add(Saved(i.ToString(CultureInfo.InvariantCulture), Now.AddDays(-i)));
        }

        using var library = new LibraryService(_web, cache: null, _time);
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        var sync = new SmartPlaylistSync(library, _time, TimeZoneInfo.Utc);
        var playlist = Liked();
        playlist.Name = "Everything";
        playlist.KeepOnSpotify = true;
        Assert.True(sync.IsDue(playlist));

        var result = await sync.SyncAsync(playlist.Clone(), full: false, TestContext.Current.CancellationToken);
        Assert.True(SmartPlaylistSync.Apply(playlist, result));

        Assert.Equal(SmartSyncOutcome.Updated, result.Outcome);
        Assert.Equal("new", playlist.SpotifyId);
        Assert.Equal(SmartPlaylistSync.Description, _web.CreatedDescription);
        Assert.Equal(3, _web.Commands.Count);
        Assert.Equal("create Everything", _web.Commands[0]);
        Assert.StartsWith("replace new spotify:track:0,", _web.Commands[1], StringComparison.Ordinal);
        Assert.Equal(100, _web.Commands[1].Split(',').Length);
        Assert.Equal(50, _web.Commands[2].Split(',').Length);
        Assert.Equal(150, library.Snapshot!.Playlists.Single(p => p.Id == "new").ItemCount);
        Assert.False(sync.IsDue(playlist));

        // Nothing changed: nothing is sent, until the daily refresh.
        _web.Commands.Clear();
        result = await sync.SyncAsync(playlist.Clone(), full: false, TestContext.Current.CancellationToken);
        Assert.Equal(SmartSyncOutcome.Unchanged, result.Outcome);
        Assert.Empty(_web.Commands);

        _time.Advance(TimeSpan.FromDays(1));
        Assert.True(sync.IsDue(playlist));
        playlist.Name = "All of it";
        result = await sync.SyncAsync(playlist.Clone(), full: true, TestContext.Current.CancellationToken);
        SmartPlaylistSync.Apply(playlist, result);
        Assert.Equal(["rename new All of it", "replace", "add"], _web.Commands.Select(c => c.Split(' ')[0] == "rename" ? c : c.Split(' ')[0]));
        Assert.Equal("All of it", playlist.SyncedName);
    }

    [Fact]
    public async Task A_playlist_deleted_on_Spotify_is_let_go()
    {
        _web.SavedTracks.Add(Saved("a", Now));
        using var library = new LibraryService(_web, cache: null, _time);
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        var sync = new SmartPlaylistSync(library, _time, TimeZoneInfo.Utc);
        var playlist = Liked();
        playlist.KeepOnSpotify = true;
        SmartPlaylistSync.Apply(playlist, await sync.SyncAsync(playlist.Clone(), full: false, TestContext.Current.CancellationToken));
        Assert.False(sync.IsGone(playlist));

        // Spotify no longer lists it among the user's playlists.
        _time.Advance(TimeSpan.FromHours(1));
        await library.RefreshAsync(TestContext.Current.CancellationToken);
        _web.Commands.Clear();
        var result = await sync.SyncAsync(playlist.Clone(), full: true, TestContext.Current.CancellationToken);

        Assert.Equal(SmartSyncOutcome.Gone, result.Outcome);
        Assert.Empty(_web.Commands);
        Assert.False(SmartPlaylistSync.Apply(playlist, result));
        Assert.False(playlist.KeepOnSpotify);
        Assert.Null(playlist.SpotifyId);
    }

    [Fact]
    public async Task A_playlist_Spotify_no_longer_finds_is_let_go()
    {
        _web.SavedTracks.Add(Saved("a", Now));
        using var library = new LibraryService(_web, cache: null, _time);
        var sync = new SmartPlaylistSync(library, _time, TimeZoneInfo.Utc);
        var playlist = Liked();
        playlist.KeepOnSpotify = true;
        playlist.SpotifyId = "old";
        playlist.LinkedAt = Now;
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.NotFound, null, "Not found");

        var result = await sync.SyncAsync(playlist.Clone(), full: true, TestContext.Current.CancellationToken);

        Assert.Equal(SmartSyncOutcome.Gone, result.Outcome);
    }

    [Fact]
    public async Task A_failure_after_making_the_playlist_keeps_it_linked()
    {
        _web.SavedTracks.Add(Saved("a", Now));
        using var library = new LibraryService(_web, cache: null, _time);
        var sync = new SmartPlaylistSync(library, _time, TimeZoneInfo.Utc);
        var playlist = Liked();
        playlist.KeepOnSpotify = true;

        // The playlist is made, then filling it fails.
        _web.FailReplace = new HttpRequestException("Offline");
        var result = await sync.SyncAsync(playlist.Clone(), full: false, TestContext.Current.CancellationToken);
        SmartPlaylistSync.Apply(playlist, result);

        Assert.Equal(SmartSyncOutcome.Failed, result.Outcome);
        Assert.Equal("new", playlist.SpotifyId);
        Assert.Null(playlist.SyncedAt);
        Assert.True(sync.IsDue(playlist));
    }

    [Fact]
    public async Task Liked_Songs_stored_before_release_years_is_read_again_only_when_needed()
    {
        for (var i = 0; i < 60; i++)
        {
            _web.SavedTracks.Add(Saved(i.ToString(CultureInfo.InvariantCulture), Now.AddDays(-i)));
        }

        var store = new TrackListStore(_folder);
        using var library = new LibraryService(_web, cache: null, _time, store);
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);
        var stored = store.Load(LibraryService.LikedSongsKey)!;
        Assert.Equal(1, stored.Format);
        store.Save(new CachedTrackList { Key = stored.Key, SavedAt = stored.SavedAt, Tracks = stored.Tracks, Format = 0 });

        var reads = _web.SavedTrackReads;
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(reads + 1, _web.SavedTrackReads);

        await library.GetAllLikedSongsAsync(withReleaseYears: true, TestContext.Current.CancellationToken);
        Assert.Equal(reads + 3, _web.SavedTrackReads);
        Assert.Equal(1, store.Load(LibraryService.LikedSongsKey)!.Format);
    }

    private static List<TrackInfo> Evaluate(SmartPlaylist playlist, IReadOnlyList<TrackInfo> songs) =>
        SmartPlaylistEvaluator.Evaluate(playlist, new SmartInputs(songs, new Dictionary<string, IReadOnlySet<string>>()), Now, TimeZoneInfo.Utc);

    private static List<string?> Ids(IEnumerable<TrackInfo> tracks) => tracks.Select(t => t.Id).ToList();

    private static SmartPlaylist Liked(params SmartRule[] rules) => new() { Id = "s", Name = "Smart", Rules = [.. rules] };

    private static TrackInfo Song(string id, string? saved = null, int? released = 2000, int seconds = 240, string artist = "Band", bool isExplicit = false) =>
        new("spotify:track:" + id, "Song " + id, artist, "Album", null, TimeSpan.FromSeconds(seconds), null, null, isExplicit, true)
        {
            Id = id,
            AddedAt = saved is null ? null : DateTimeOffset.Parse(saved + "T12:00:00Z", CultureInfo.InvariantCulture),
            ReleaseYear = released,
            ArtistRefs = [new ArtistRef(artist, null)],
        };

    private static SavedTrack Saved(string id, DateTimeOffset added) => new()
    {
        AddedAt = added.ToString("O", CultureInfo.InvariantCulture),
        Track = new PlayableItem
        {
            Id = id,
            Name = "Song " + id,
            Uri = "spotify:track:" + id,
            DurationMs = 1000,
            Album = new SimplifiedAlbum { Name = "Album", ReleaseDate = "1999-01-01" },
        },
    };
}

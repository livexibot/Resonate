using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class NewEndpointTests
{
    private readonly FakeHttpHandler _handler = new();

    [Fact]
    public async Task Shuffle_and_repeat_use_Spotifys_words()
    {
        _handler.Respond(HttpStatusCode.NoContent).Respond(HttpStatusCode.NoContent);

        await Api().SetShuffleAsync(false, "d1", TestContext.Current.CancellationToken);
        await Api().SetRepeatAsync(RepeatMode.All, null, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.spotify.com/v1/me/player/shuffle?state=false&device_id=d1", _handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://api.spotify.com/v1/me/player/repeat?state=context", _handler.Requests[1].Uri.AbsoluteUri);
        Assert.All(_handler.Requests, r => Assert.Equal(HttpMethod.Put, r.Method));
    }

    [Fact]
    public async Task Queueing_escapes_the_song_uri()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        await Api().AddToQueueAsync("spotify:track:abc", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, _handler.Requests[0].Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/queue?uri=spotify%3Atrack%3Aabc", _handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Library_calls_send_uris_in_the_query_string()
    {
        _handler
            .Respond(HttpStatusCode.OK, "[true,false]")
            .Respond(HttpStatusCode.OK)
            .Respond(HttpStatusCode.OK);

        var saved = await Api().CheckLibraryAsync(["spotify:track:a", "spotify:track:b"], TestContext.Current.CancellationToken);
        await Api().SaveToLibraryAsync(["spotify:track:a"], TestContext.Current.CancellationToken);
        await Api().RemoveFromLibraryAsync(["spotify:track:b"], TestContext.Current.CancellationToken);

        Assert.Equal([true, false], saved);
        Assert.Equal("https://api.spotify.com/v1/me/library/contains?uris=spotify%3Atrack%3Aa,spotify%3Atrack%3Ab", _handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Put, _handler.Requests[1].Method);
        Assert.Equal("https://api.spotify.com/v1/me/library?uris=spotify%3Atrack%3Aa", _handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Delete, _handler.Requests[2].Method);
    }

    [Fact]
    public async Task Library_calls_refuse_more_than_forty_uris()
    {
        var uris = Enumerable.Range(0, 41).Select(i => $"spotify:track:{i}").ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => Api().SaveToLibraryAsync(uris, TestContext.Current.CancellationToken));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Reordering_a_playlist_sends_the_range_and_returns_the_new_version()
    {
        _handler.Respond(HttpStatusCode.OK, """{"snapshot_id":"v2"}""");

        var snapshot = await Api().ReorderPlaylistItemsAsync("p1", 4, 0, 1, "v1", TestContext.Current.CancellationToken);

        Assert.Equal("v2", snapshot);
        Assert.Equal(HttpMethod.Put, _handler.Requests[0].Method);
        Assert.Equal("https://api.spotify.com/v1/playlists/p1/items", _handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("""{"range_start":4,"insert_before":0,"range_length":1,"snapshot_id":"v1"}""", _handler.Requests[0].Body);
    }

    [Fact]
    public async Task Removing_songs_names_them_as_items()
    {
        _handler.Respond(HttpStatusCode.OK, """{"snapshot_id":"v3"}""");

        await Api().RemovePlaylistItemsAsync("p1", ["spotify:track:a"], null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, _handler.Requests[0].Method);
        Assert.Equal("""{"items":[{"uri":"spotify:track:a"}]}""", _handler.Requests[0].Body);
    }

    [Fact]
    public async Task Recently_played_can_start_after_a_time()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {"items":[{"track":{"name":"Song","uri":"spotify:track:s","duration_ms":1000},"played_at":"2026-10-07T10:00:00.000Z","context":{"type":"playlist","uri":"spotify:playlist:p"}}],
             "next":null,"limit":50,"cursors":{"after":"1","before":"0"}}
            """);

        var page = await Api().GetRecentlyPlayedAsync(50, DateTimeOffset.FromUnixTimeMilliseconds(1_000), TestContext.Current.CancellationToken);

        Assert.Equal("Song", page.Items[0]!.Track!.Name);
        Assert.Equal("spotify:playlist:p", page.Items[0]!.Context!.Uri);
        Assert.Equal("https://api.spotify.com/v1/me/player/recently-played?limit=50&after=1000", _handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Top_artists_ask_for_the_time_range()
    {
        _handler.Respond(HttpStatusCode.OK, """{"items":[{"id":"a","name":"Artist","uri":"spotify:artist:a","genres":["pop"]}],"total":1}""");

        var page = await Api().GetTopArtistsAsync(TopRange.ShortTerm, 0, 20, TestContext.Current.CancellationToken);

        Assert.Equal("pop", page.Items[0]!.Genres![0]);
        Assert.Contains("me/top/artists?time_range=short_term", _handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_queue_answer_is_an_empty_queue()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        var queue = await Api().GetQueueAsync(TestContext.Current.CancellationToken);

        Assert.Null(queue.CurrentlyPlaying);
        Assert.Empty(queue.Queue);
    }

    [Fact]
    public void Playback_state_reads_repeat_and_disallowed_actions()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize(
            """{"is_playing":true,"repeat_state":"track","shuffle_state":true,"actions":{"disallows":{"toggling_shuffle":true}}}""",
            SpotifyJsonContext.Default.PlaybackState)!;

        Assert.Equal(RepeatMode.One, state.Repeat);
        Assert.True(state.ShuffleState);
        Assert.True(state.Actions!.Disallowed("toggling_shuffle"));
        Assert.False(state.Actions.Disallowed("pausing"));
    }

    [Fact]
    public void Repeat_cycles_like_Spotify()
    {
        Assert.Equal(RepeatMode.All, RepeatModes.Next(RepeatMode.Off));
        Assert.Equal(RepeatMode.One, RepeatModes.Next(RepeatMode.All));
        Assert.Equal(RepeatMode.Off, RepeatModes.Next(RepeatMode.One));
    }

    private SpotifyWebApi Api() => new(new HttpClient(_handler), new FixedToken());

    private sealed class FixedToken : IAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken) => Task.FromResult("t");
    }
}

public sealed class SortingTests
{
    private static TrackInfo Song(string title, string artist, string album, int seconds, int position, int daysAgo = 0) =>
        new TrackInfo($"spotify:track:{title}", title, artist, album, null, TimeSpan.FromSeconds(seconds), null, null, false, true)
        {
            Position = position,
            ArtistRefs = [new ArtistRef(artist, artist)],
            AddedAt = DateTimeOffset.UnixEpoch.AddDays(1000 - daysAgo),
        };

    private static readonly TrackInfo[] Songs =
    [
        Song("beta", "Zed", "B", 200, 0, daysAgo: 5),
        Song("Álpha", "amy", "A", 100, 1, daysAgo: 1),
        Song("gamma", "Amy", "C", 300, 2, daysAgo: 9),
    ];

    [Fact]
    public void Sorts_by_title_ignoring_case_and_accents()
    {
        Assert.Equal(["Álpha", "beta", "gamma"], TrackSorter.Apply(Songs, new TrackSort(TrackSortField.Title, false)).Select(t => t.Title));
    }

    [Fact]
    public void Sorts_by_artist_then_album()
    {
        Assert.Equal(["Álpha", "gamma", "beta"], TrackSorter.Apply(Songs, new TrackSort(TrackSortField.Artist, false)).Select(t => t.Title));
    }

    [Fact]
    public void Sorts_by_date_added_newest_first_when_descending()
    {
        Assert.Equal(["Álpha", "beta", "gamma"], TrackSorter.Apply(Songs, new TrackSort(TrackSortField.DateAdded, true)).Select(t => t.Title));
    }

    [Fact]
    public void The_default_sort_keeps_the_lists_own_order()
    {
        Assert.Equal(["beta", "Álpha", "gamma"], TrackSorter.Apply(Songs, TrackSort.Default).Select(t => t.Title));
    }

    [Fact]
    public void Sorts_survive_a_round_trip_through_settings()
    {
        var sort = new TrackSort(TrackSortField.Duration, true);

        Assert.Equal(sort, TrackSort.Parse(sort.Serialize()));
        Assert.Equal(TrackSort.Default, TrackSort.Parse("nonsense"));
    }

    [Fact]
    public void Filters_by_every_word_in_title_artist_or_album()
    {
        Assert.True(TrackSorter.Matches(Songs[1], "alpha AMY"));
        Assert.False(TrackSorter.Matches(Songs[1], "alpha zed"));
        Assert.True(TrackSorter.Matches(Songs[1], "  "));
    }

    [Fact]
    public void Custom_playlist_order_puts_new_playlists_first()
    {
        var playlists = new[] { "a", "b", "c", "new" }.Select(id => new SimplifiedPlaylist { Id = id, Name = id }).ToList();

        var sorted = PlaylistSorter.Apply(playlists, PlaylistSortMode.Custom, customOrder: ["c", "a", "b"]);

        Assert.Equal(["new", "c", "a", "b"], sorted.Select(p => p.Id));
    }

    [Fact]
    public void Dragging_a_playlist_moves_it_in_the_custom_order()
    {
        Assert.Equal(["b", "a", "c"], PlaylistSorter.Move(["a", "b", "c"], "b", 0));
        Assert.Equal(["a", "c", "b"], PlaylistSorter.Move(["a", "b", "c"], "b", 99));
    }

    [Fact]
    public void Recently_played_playlists_come_first()
    {
        var playlists = new[] { "a", "b", "c" }.Select(id => new SimplifiedPlaylist { Id = id, Name = id }).ToList();
        var played = new Dictionary<string, DateTimeOffset> { ["c"] = DateTimeOffset.UnixEpoch.AddDays(2), ["b"] = DateTimeOffset.UnixEpoch.AddDays(1) };

        Assert.Equal(["c", "b", "a"], PlaylistSorter.Apply(playlists, PlaylistSortMode.RecentlyPlayed, lastPlayed: played).Select(p => p.Id));
    }
}

public sealed class TrueShuffleTests
{
    [Fact]
    public void Keeps_every_item_exactly_once()
    {
        var items = Enumerable.Range(0, 500).ToList();

        var shuffled = TrueShuffle.Shuffle(items);

        Assert.Equal(items, shuffled.Order());
        Assert.NotEqual(items, shuffled);
    }

    [Fact]
    public void Starts_with_the_chosen_song()
    {
        var shuffled = TrueShuffle.ShuffleAfter(Enumerable.Range(0, 50).ToList(), 17);

        Assert.Equal(17, shuffled[0]);
        Assert.Equal(Enumerable.Range(0, 50), shuffled.Order());
    }

    [Fact]
    public void Every_position_is_equally_likely()
    {
        // 4 items have 24 orders; a fair shuffle gives each about 1/24 of the runs.
        const int Runs = 48_000;
        var counts = new Dictionary<string, int>();
        for (var i = 0; i < Runs; i++)
        {
            var key = string.Join(',', TrueShuffle.Shuffle([0, 1, 2, 3]));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        Assert.Equal(24, counts.Count);

        // Chi-squared with 23 degrees of freedom; 60 is far beyond chance (p < 0.0001).
        var expected = Runs / 24.0;
        var chiSquared = counts.Values.Sum(c => (c - expected) * (c - expected) / expected);
        Assert.True(chiSquared < 60, $"chi-squared {chiSquared:0.0}");
    }

    [Fact]
    public void Uses_the_given_random_source()
    {
        // Always picking 0 swaps each item with the first: a known order.
        Assert.Equal([1, 2, 3, 0], TrueShuffle.Shuffle([0, 1, 2, 3], _ => 0));
    }
}

public sealed class WholeListTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-lists-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Loads_all_liked_songs_then_only_the_first_page_while_nothing_changed()
    {
        AddLiked(130);
        using var library = Library();

        var first = await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);
        var readsAfterFirst = _web.SavedTrackReads;
        var second = await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(130, first.Count);
        Assert.Equal(Enumerable.Range(0, 130), first.Select(t => t.Position!.Value));
        Assert.Equal(3, readsAfterFirst);
        Assert.Equal(readsAfterFirst + 1, _web.SavedTrackReads);
        Assert.Equal(first.Select(t => t.Uri), second.Select(t => t.Uri));
    }

    [Fact]
    public async Task Newly_liked_songs_are_added_without_reloading_everything()
    {
        AddLiked(130);
        using var library = Library();
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);
        var reads = _web.SavedTrackReads;

        _web.SavedTracks.Insert(0, Saved("new", DateTimeOffset.UnixEpoch.AddDays(10_000)));
        var all = await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(131, all.Count);
        Assert.Equal("spotify:track:new", all[0].Uri);
        Assert.Equal(reads + 1, _web.SavedTrackReads);
    }

    [Fact]
    public async Task Removed_songs_trigger_a_full_reload()
    {
        AddLiked(130);
        using var library = Library();
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        _web.SavedTracks.RemoveAt(5);
        var all = await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(129, all.Count);
    }

    [Fact]
    public async Task A_song_liked_in_Resonate_gets_Spotifys_full_details_on_the_next_load()
    {
        AddLiked(130);
        using var library = Library();
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        // A like can come with only the song's address and names.
        _web.SavedTracks.Insert(0, InFull("new", DateTimeOffset.UnixEpoch.AddDays(10_000)));
        var bare = new TrackInfo("spotify:track:new", "Song new", "Band", "Record", null, TimeSpan.FromSeconds(1), null, null, false, true);
        await library.NoteLikeChangedAsync(bare, liked: true, TestContext.Current.CancellationToken);
        var all = await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(131, all.Count);
        Assert.Equal("band", Assert.Single(all[0].ArtistRefs).Id);
        Assert.Equal("record", all[0].AlbumId);
        Assert.Equal("record", new TrackListStore(_folder).Load(LibraryService.LikedSongsKey)!.Tracks[0].AlbumId);
    }

    [Fact]
    public async Task Liked_Songs_is_not_written_again_while_nothing_changed()
    {
        for (var i = 0; i < 60; i++)
        {
            _web.SavedTracks.Add(InFull(i.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch.AddDays(5000 - i)));
        }

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var library = new LibraryService(_web, cache: null, time, new TrackListStore(_folder));
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);
        var savedAt = new TrackListStore(_folder).Load(LibraryService.LikedSongsKey)!.SavedAt;

        time.Advance(TimeSpan.FromHours(1));
        await library.GetAllLikedSongsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(savedAt, new TrackListStore(_folder).Load(LibraryService.LikedSongsKey)!.SavedAt);
    }

    [Fact]
    public async Task An_unchanged_playlist_version_needs_no_requests()
    {
        _web.PlaylistEntries["p"] = Enumerable.Range(0, 75)
            .Select(i => new PlaylistEntry { Item = new PlayableItem { Name = $"S{i}", Uri = $"spotify:track:{i}", DurationMs = 1000 } })
            .ToList();
        using var library = Library();

        var first = await library.GetAllPlaylistTracksAsync("p", "v1", TestContext.Current.CancellationToken);
        var reads = _web.PlaylistItemReads;
        var again = await library.GetAllPlaylistTracksAsync("p", "v1", TestContext.Current.CancellationToken);
        await library.GetAllPlaylistTracksAsync("p", "v2", TestContext.Current.CancellationToken);

        Assert.Equal(75, first.Tracks.Count);
        Assert.Equal(75, again.Tracks.Count);
        Assert.Equal(2, reads);
        Assert.Equal(reads + 2, _web.PlaylistItemReads);
    }

    [Fact]
    public async Task A_playlist_Spotify_will_not_list_is_hidden()
    {
        _web.PlaylistItemsFailure = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Forbidden");
        using var library = Library();

        var list = await library.GetAllPlaylistTracksAsync("p", "v1", TestContext.Current.CancellationToken);

        Assert.True(list.ItemsHidden);
    }

    [Fact]
    public async Task Local_files_keep_their_position_and_are_marked_local()
    {
        _web.PlaylistEntries["p"] =
        [
            new PlaylistEntry { Item = new PlayableItem { Name = "Spotify song", Uri = "spotify:track:1", DurationMs = 1000 } },
            new PlaylistEntry { IsLocal = true, Item = new PlayableItem { Name = "My demo", Uri = "spotify:local:Me:Demos:My+demo:120", IsLocal = true, DurationMs = 120_000 } },
        ];
        using var library = Library();

        var list = await library.GetAllPlaylistTracksAsync("p", "v1", TestContext.Current.CancellationToken);

        var local = list.Tracks[1];
        Assert.True(local.IsLocal);
        Assert.False(local.IsPlayable);
        Assert.Equal(1, local.Position);
    }

    private LibraryService Library() => new(_web, cache: null, lists: new TrackListStore(_folder));

    private void AddLiked(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _web.SavedTracks.Add(Saved(i.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch.AddDays(5000 - i)));
        }
    }

    private static SavedTrack Saved(string id, DateTimeOffset added) => new()
    {
        AddedAt = added.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Track = new PlayableItem { Name = "Song " + id, Uri = "spotify:track:" + id, DurationMs = 1000 },
    };

    /// <summary>A liked song with everything Spotify tells about it: artists, album, covers.</summary>
    private static SavedTrack InFull(string id, DateTimeOffset added) => new()
    {
        AddedAt = added.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Track = new PlayableItem
        {
            Id = id,
            Name = "Song " + id,
            Uri = "spotify:track:" + id,
            DurationMs = 201_337,
            TrackNumber = 3,
            DiscNumber = 1,
            Artists = [new SimplifiedArtist { Id = "band", Name = "Band" }],
            Album = new SimplifiedAlbum
            {
                Id = "record",
                Name = "Record",
                Uri = "spotify:album:record",
                Images = [new SpotifyImage { Url = "https://i.scdn.co/large", Width = 640 }, new SpotifyImage { Url = "https://i.scdn.co/small", Width = 64 }],
            },
        },
    };
}

public sealed class ScopeTests
{
    [Fact]
    public void An_older_sign_in_misses_the_new_permissions()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("a", "r", DateTimeOffset.MaxValue, "user-library-read playlist-read-private"));
        using var session = new SpotifySession(
            new SpotifyAuthClient(new HttpClient(), new SpotifyAuthOptions { ClientId = new string('a', 32) }),
            store);

        var missing = session.MissingScopes(SpotifyAuthOptions.DefaultScopes);

        Assert.Contains("user-library-modify", missing);
        Assert.DoesNotContain("user-library-read", missing);
    }

    [Fact]
    public void Unknown_permissions_are_not_reported_as_missing()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("a", "r", DateTimeOffset.MaxValue, string.Empty));
        using var session = new SpotifySession(
            new SpotifyAuthClient(new HttpClient(), new SpotifyAuthOptions { ClientId = new string('a', 32) }),
            store);

        Assert.Empty(session.MissingScopes(SpotifyAuthOptions.DefaultScopes));
    }
}

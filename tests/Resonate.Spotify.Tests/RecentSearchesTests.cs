using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>Search's recent queries and recently viewed results, and its top result.</summary>
public sealed class RecentSearchesTests
{
    [Fact]
    public void A_query_goes_first_once_whatever_its_case_and_the_list_stays_short()
    {
        List<string> queries = [];
        Assert.True(RecentSearches.AddQuery(queries, "  daft punk "));
        Assert.True(RecentSearches.AddQuery(queries, "Muse"));
        Assert.True(RecentSearches.AddQuery(queries, "Daft Punk"));
        Assert.Equal(["Daft Punk", "Muse"], queries);

        Assert.False(RecentSearches.AddQuery(queries, "x"));
        Assert.False(RecentSearches.AddQuery(queries, "   "));
        Assert.False(RecentSearches.AddQuery(queries, null));

        for (var i = 0; i < 20; i++)
        {
            RecentSearches.AddQuery(queries, $"query {i}");
        }

        Assert.Equal(RecentSearches.MaxQueries, queries.Count);
        Assert.Equal("query 19", queries[0]);
    }

    [Fact]
    public void A_pick_goes_first_once_per_address()
    {
        List<RecentSearchPick> picks = [];
        var artist = new RecentSearchPick(RecentSearchKind.Artist, "spotify:artist:1", "1", "Muse", "Artist", null);
        var album = new RecentSearchPick(RecentSearchKind.Album, "spotify:album:2", "2", "Origin", "Album", null);
        RecentSearches.AddPick(picks, artist);
        RecentSearches.AddPick(picks, album);
        RecentSearches.AddPick(picks, artist);

        Assert.Equal([artist, album], picks);
        Assert.False(RecentSearches.AddPick(picks, artist with { Uri = string.Empty }));

        for (var i = 0; i < 30; i++)
        {
            RecentSearches.AddPick(picks, artist with { Uri = $"spotify:artist:{i + 10}" });
        }

        Assert.Equal(RecentSearches.MaxPicks, picks.Count);
    }

    [Fact]
    public void A_song_pick_plays_the_same_song_again()
    {
        var track = new TrackInfo("spotify:track:9", "Uprising", "Muse", "The Resistance", "spotify:album:3", TimeSpan.FromSeconds(305), "small.jpg", "large.jpg", IsExplicit: false, IsPlayable: true) { Id = "9" };

        var again = RecentSearchPick.Song(track).ToTrack();

        Assert.Equal(track.Uri, again.Uri);
        Assert.Equal(track.Id, again.Id);
        Assert.Equal(track.Title, again.Title);
        Assert.Equal(track.Artists, again.Artists);
        Assert.Equal(track.AlbumUri, again.AlbumUri);
        Assert.Equal(track.Duration, again.Duration);
        Assert.True(again.IsPlayable);
    }

    [Fact]
    public void The_top_result_is_the_artist_named_else_the_first_song()
    {
        var song = new TrackInfo("spotify:track:1", "Muse", "Someone", "Album", null, TimeSpan.FromMinutes(3), null, null, false, true);
        var muse = new Artist { Id = "a", Name = "Muse", Uri = "spotify:artist:a" };
        var other = new Artist { Id = "b", Name = "Museum", Uri = "spotify:artist:b" };

        Assert.Equal(RecentSearchKind.Artist, RecentSearches.TopKind("muse", new SearchMatches([song], [], [muse], [])));
        Assert.Equal(RecentSearchKind.Song, RecentSearches.TopKind("muse", new SearchMatches([song], [], [other], [])));
        Assert.Equal(RecentSearchKind.Artist, RecentSearches.TopKind("muse", new SearchMatches([], [], [other], [])));
        Assert.Null(RecentSearches.TopKind("muse", new SearchMatches([], [], [], [])));
    }
}

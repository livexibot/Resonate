using System.Globalization;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Tests;

public sealed class ArtistOrbitTests
{
    [Fact]
    public void Companions_are_the_artists_sharing_playlists_most_often_first()
    {
        var playlists = new List<IReadOnlyList<TrackInfo>>
        {
            Playlist(Music.Songs("a", 2), Music.Songs("b", 1), Music.Songs("c", 1)),
            Playlist(Music.Songs("a", 1), Music.Songs("b", 3)),
            Playlist(Music.Songs("b", 1), Music.Songs("d", 1)),
        };

        var view = ArtistOrbit.Build(playlists, [], []).For("a");

        Assert.Equal("Artist a", view.ArtistName);
        Assert.Equal(["b", "c"], view.Companions.Select(c => c.Id));
        Assert.Equal(2, view.Companions[0].Playlists);
        Assert.Equal("Together in 2 of your playlists", view.Companions[0].Description);
        Assert.Equal("Together in 1 of your playlists", view.Companions[1].Description);
    }

    [Fact]
    public void Listening_sessions_and_shared_songs_count_too()
    {
        var history = Music.Session(Music.Now, "a", "b")
            .Concat(Music.Session(Music.Now.AddHours(-5), "a", "b", "c"))
            .Append(Music.Play("late", "d", Music.Now.AddHours(2)))
            .OrderByDescending(p => p.PlayedAt)
            .ToList();
        var liked = new[] { TrackInfo.From(Music.Song("duet", "a", featuring: "e"))! };

        var view = ArtistOrbit.Build([], liked, history).For("a");

        var b = Assert.Single(view.Companions, c => c.Id == "b");
        Assert.Equal(2, b.Sessions);
        Assert.Equal("Together in 2 listening sessions", b.Description);
        var e = Assert.Single(view.Companions, c => c.Id == "e");
        Assert.Equal(1, e.Songs);
        Assert.Equal("On 1 song together", e.Description);
        Assert.DoesNotContain(view.Companions, c => c.Id == "d");

        // A song together counts for more than a session of three.
        Assert.True(e.Strength > view.Companions.Single(c => c.Id == "c").Strength);
    }

    [Fact]
    public void At_most_ten_companions_and_the_artists_liked_albums_most_liked_first()
    {
        var crowd = Enumerable.Range(0, 15).SelectMany(i => Music.Songs("o" + i.ToString(CultureInfo.InvariantCulture), 1)).ToList();
        var playlists = new List<IReadOnlyList<TrackInfo>> { Playlist(Music.Songs("a", 1), crowd) };
        var liked = Music.Songs("a", 3)
            .Select((t, i) => t with { AlbumId = i < 2 ? "two" : "one", Album = i < 2 ? "Two" : "One" })
            .Append(TrackInfo.From(Music.Song("guest", "z", featuring: "a"))! with { AlbumId = "theirs" })
            .ToList();

        var view = ArtistOrbit.Build(playlists, liked, []).For("a");

        Assert.Equal(ArtistOrbit.MaxCompanions, view.Companions.Count);
        Assert.Equal(["two", "one"], view.Albums.Select(a => a.Id));
        Assert.Equal(2, view.Albums[0].LikedSongs);
    }

    [Fact]
    public void An_artist_nobody_keeps_company_with_has_an_empty_orbit()
    {
        var view = ArtistOrbit.Build([Music.Songs("a", 4).ToList()], [], []).For("a");

        Assert.Empty(view.Companions);
        Assert.Empty(ArtistOrbit.Build([], [], []).For("unknown").Companions);
    }

    private static List<TrackInfo> Playlist(params IEnumerable<TrackInfo>[] parts) => parts.SelectMany(p => p).ToList();
}

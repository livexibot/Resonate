using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class QuickSearchTests
{
    private static readonly SimplifiedPlaylist[] Playlists =
    [
        new() { Id = "run", Name = "Running", Uri = "spotify:playlist:run", Owner = new PlaylistOwner { DisplayName = "Me" } },
        new() { Id = "calm", Name = "Calm evenings", Uri = "spotify:playlist:calm" },
    ];

    private static readonly TrackInfo[] Liked =
    [
        Song("1", "Halo", "Beyoncé", "I Am... Sasha Fierce", "a1", ("Beyoncé", "beyonce")),
        Song("2", "Run the World", "Beyoncé", "4", "a2", ("Beyoncé", "beyonce")),
        Song("3", "Running Up That Hill", "Kate Bush", "Hounds of Love", "a3", ("Kate Bush", "kate")),
        Song("4", "Halo", "Beyoncé", "I Am... Sasha Fierce", "a1", ("Beyoncé", "beyonce")),
    ];

    private static readonly TrackInfo[] Local =
    [
        new TrackInfo(null, "Home demo", "Me", "Tapes", null, TimeSpan.FromMinutes(3), null, null, false, true) { FilePath = @"C:\Music\home.mp3" },
    ];

    private static readonly QuickSearchIndex Index = QuickSearchIndex.Build(Playlists, Liked, Local);

    [Fact]
    public void Nothing_typed_offers_liked_songs_and_the_playlists_in_order()
    {
        var items = Index.SearchLibrary("  ", 10);

        Assert.Equal([QuickKind.LikedSongs, QuickKind.Playlist, QuickKind.Playlist], items.Select(i => i.Kind));
        Assert.Equal("Running", items[1].Title);
        Assert.Empty(Index.SearchLocal(string.Empty, 10));
    }

    [Fact]
    public void Lists_come_first_then_artists_albums_and_songs()
    {
        var items = Index.SearchLibrary("run", 10);

        Assert.Equal(QuickKind.Playlist, items[0].Kind);
        Assert.Equal("Running", items[0].Title);
        Assert.Equal(["Run the World", "Running Up That Hill"], items.Where(i => i.Kind == QuickKind.Song).Select(i => i.Title).Order());
    }

    [Fact]
    public void Accents_and_case_do_not_matter_and_every_word_must_match()
    {
        Assert.Contains(Index.SearchLibrary("BEYONCE", 10), i => i is { Kind: QuickKind.Artist, Title: "Beyoncé", Id: "beyonce" });
        Assert.Equal(["Halo"], Index.SearchLibrary("halo beyonce", 10).Where(i => i.Kind == QuickKind.Song).Select(i => i.Title).Distinct());
        Assert.Empty(Index.SearchLibrary("halo kate", 10));
    }

    [Fact]
    public void Albums_and_artists_are_listed_once()
    {
        var items = Index.SearchLibrary("sasha", 10);

        Assert.Single(items, i => i.Kind == QuickKind.Album);
        Assert.Single(Index.SearchLibrary("beyonce", 10), i => i.Kind == QuickKind.Artist);
        Assert.Equal("spotify:album:a1", items.Single(i => i.Kind == QuickKind.Album).Uri);
    }

    [Fact]
    public void A_title_that_starts_with_the_words_comes_before_one_that_only_contains_them()
    {
        var songs = QuickSearchIndex.Build([], [Song("5", "Overrun", "X", "Y", "a5"), Song("6", "Run", "X", "Y", "a6")], [])
            .SearchLibrary("run", 10)
            .Where(i => i.Kind == QuickKind.Song)
            .Select(i => i.Title);

        Assert.Equal(["Run", "Overrun"], songs);
    }

    [Fact]
    public void Local_files_are_searched_on_their_own()
    {
        var items = Index.SearchLocal("home", 10);

        Assert.Equal(QuickKind.LocalSong, Assert.Single(items).Kind);
        Assert.Same(Local[0], items[0].Track);
        Assert.DoesNotContain(Index.SearchLibrary("home", 10), i => i.Kind == QuickKind.LocalSong);
    }

    private static TrackInfo Song(string id, string title, string artists, string album, string albumId, params (string Name, string Id)[] refs) =>
        new($"spotify:track:{id}", title, artists, album, $"spotify:album:{albumId}", TimeSpan.FromMinutes(3), null, null, false, true)
        {
            Id = id,
            AlbumId = albumId,
            ArtistRefs = refs.Select(r => new ArtistRef(r.Name, r.Id)).ToList(),
        };
}

using System.Net;
using Resonate.Spotify.Library;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly string _cacheFile = Path.Combine(Path.GetTempPath(), $"resonate-test-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_cacheFile);

    [Fact]
    public async Task Refresh_reads_every_page_of_playlists_and_caches_them()
    {
        for (var i = 0; i < 120; i++)
        {
            _web.Playlists.Add(new SimplifiedPlaylist { Id = $"p{i}", Name = $"List {i}", Uri = $"spotify:playlist:p{i}" });
        }

        var library = new LibraryService(_web, new LibraryCache(_cacheFile));
        var snapshot = await library.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(120, snapshot.Playlists.Count);
        Assert.Equal("me", snapshot.User!.Id);

        var reopened = new LibraryService(_web, new LibraryCache(_cacheFile));
        Assert.Equal(120, reopened.Snapshot!.Playlists.Count);
        Assert.Equal("List 119", reopened.Snapshot.Playlists[^1].Name);
    }

    [Fact]
    public async Task Forgetting_the_library_also_empties_the_cache()
    {
        _web.Playlists.Add(new SimplifiedPlaylist { Id = "p", Name = "Mine", Uri = "spotify:playlist:p" });
        var library = new LibraryService(_web, new LibraryCache(_cacheFile));
        await library.RefreshAsync(TestContext.Current.CancellationToken);

        library.Forget();

        Assert.Null(library.Snapshot);
        Assert.Null(new LibraryService(_web, new LibraryCache(_cacheFile)).Snapshot);
    }

    [Fact]
    public async Task A_playlist_Spotify_will_not_list_is_marked_hidden()
    {
        _web.PlaylistResult = new Playlist { Id = "p", Name = "Someone else's", Uri = "spotify:playlist:p", Items = null };
        var library = new LibraryService(_web, cache: null);

        var details = await library.GetPlaylistAsync("p", TestContext.Current.CancellationToken);

        Assert.True(details.FirstPage.ItemsHidden);
        Assert.Empty(details.FirstPage.Tracks);
    }

    [Fact]
    public async Task A_refused_song_list_is_marked_hidden()
    {
        _web.PlaylistItemsFailure = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Forbidden");
        var library = new LibraryService(_web, cache: null);

        var page = await library.GetPlaylistTracksAsync("p", 50, TestContext.Current.CancellationToken);

        Assert.True(page.ItemsHidden);
    }

    [Fact]
    public async Task Owned_and_collaborative_playlists_can_list_their_songs()
    {
        var library = new LibraryService(_web, cache: null);
        await library.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(library.CanListSongs(new SimplifiedPlaylist { Owner = new PlaylistOwner { Id = "me" } }));
        Assert.True(library.CanListSongs(new SimplifiedPlaylist { Owner = new PlaylistOwner { Id = "friend" }, Collaborative = true }));
        Assert.False(library.CanListSongs(new SimplifiedPlaylist { Owner = new PlaylistOwner { Id = "spotify" } }));
    }

    [Fact]
    public void A_damaged_cache_file_is_ignored()
    {
        File.WriteAllText(_cacheFile, "{ not json");

        Assert.Null(new LibraryCache(_cacheFile).Load());
    }

    [Fact]
    public void Track_info_picks_a_small_a_large_and_a_full_size_cover()
    {
        var info = TrackInfo.From(new PlayableItem
        {
            Name = "Song",
            Uri = "spotify:track:t",
            DurationMs = 61000,
            Artists = [new SimplifiedArtist { Name = "A" }, new SimplifiedArtist { Name = "B" }],
            Album = new SimplifiedAlbum
            {
                Name = "Album",
                Images = [new SpotifyImage { Url = "640", Width = 640 }, new SpotifyImage { Url = "300", Width = 300 }, new SpotifyImage { Url = "64", Width = 64 }],
            },
        })!;

        Assert.Equal("A, B", info.Artists);
        Assert.Equal("64", info.SmallImageUrl);
        Assert.Equal("300", info.LargeImageUrl);
        Assert.Equal("640", info.FullImageUrl);
        Assert.True(info.IsPlayable);
    }

    [Fact]
    public void Local_files_can_not_be_started_through_the_Web_API()
    {
        var info = TrackInfo.From(new PlayableItem { Name = "Local", Uri = "spotify:local:x", IsLocal = true })!;

        Assert.False(info.IsPlayable);
    }
}

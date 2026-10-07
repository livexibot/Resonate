using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class PlayerRouterTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _channel = new();
    private readonly FakeWebApi _web = new();
    private readonly FakeLocalPlayer _files = new();
    private readonly PlayerController _spotify;
    private readonly PlayerRouter _router;

    public PlayerRouterTests()
    {
        _spotify = new PlayerController(_channel, _channel, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time);
        _router = new PlayerRouter(_spotify, _files);
    }

    public void Dispose()
    {
        _router.Dispose();
        _spotify.Dispose();
    }

    [Fact]
    public async Task Files_play_in_the_local_player_and_pause_Spotify()
    {
        await StartSpotifyPlaying();
        var request = new PlayRequest([File("a"), File("b")], 1, null, "Local Files");

        await _router.PlayAsync(request);

        Assert.Same(request, Assert.Single(_files.Played));
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("b", _router.State.Title);
        Assert.False(_spotify.State.IsPlaying);
        await WaitUntil(() => _channel.Commands.Contains("pause"));
        Assert.Empty(_web.PlayBodies);
    }

    [Fact]
    public async Task Spotify_songs_play_in_Spotify_and_pause_the_local_player()
    {
        await StartSpotifyPlaying();
        await _router.PlayAsync(new PlayRequest([File("a")], 0, null, "Local Files"));

        await _router.PlayAsync(new PlayRequest([Song("x"), Song("y")], 0, null, "Search"));

        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Contains("pause", _files.Commands);
        Assert.Single(_web.PlayBodies);
        Assert.Equal("Song x", _router.State.Title);
    }

    [Fact]
    public async Task Controls_go_to_the_player_that_has_the_music()
    {
        await StartSpotifyPlaying();
        await _router.PlayAsync(new PlayRequest([File("a")], 0, null, "Local Files"));

        await _router.NextAsync();
        await _router.SetShuffleAsync(true);
        await _router.SetRepeatAsync(RepeatMode.One);

        Assert.Equal(["next", "shuffle on", "repeat track"], _files.Commands);
        Assert.DoesNotContain(_web.Commands, c => c.StartsWith("shuffle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_player_that_starts_playing_by_itself_takes_over()
    {
        await StartSpotifyPlaying();
        var changes = 0;
        _router.StateChanged += (_, _) => changes++;

        // The local player resumes (from its own media key).
        _files.Report(_files.State with { IsPlaying = true, Title = "File song" });

        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("File song", _router.State.Title);
        Assert.True(changes > 0);
        await WaitUntil(() => _channel.Commands.Contains("pause"));

        // Later a phone starts Spotify again: Spotify takes the bar back.
        _time.Advance(PlayerController.PlayStateHold);
        _channel.Report(Snapshot("Phone song", playing: true));

        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Contains("pause", _files.Commands);
    }

    [Fact]
    public async Task A_player_that_is_not_shown_does_not_change_the_bar_while_paused()
    {
        await StartSpotifyPlaying();

        _files.Report(_files.State with { IsPlaying = false, Title = "Paused file" });

        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Equal("Song A", _router.State.Title);
    }

    [Fact]
    public async Task Queueing_goes_to_the_player_of_the_song_and_tells_the_queue_view()
    {
        await StartSpotifyPlaying();
        var changes = 0;
        _router.QueueChanged += (_, _) => changes++;

        await _router.AddToQueueAsync(File("a"));
        await _router.AddToQueueAsync(Song("x"));

        Assert.Equal("a", Assert.Single(_files.Queued).Title);
        Assert.Contains("queue spotify:track:x@here", _web.Commands);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Errors_from_either_player_are_passed_on()
    {
        var errors = new List<string>();
        _router.ErrorOccurred += (_, message) => errors.Add(message);

        _files.Fail("The file could not be read.");

        Assert.Equal(["The file could not be read."], errors);
    }

    private static TrackInfo File(string name) =>
        new(null, name, "Me", "Files", null, TimeSpan.FromSeconds(100), null, null, IsExplicit: false, IsPlayable: true)
        {
            FilePath = $"C:\\Music\\{name}.flac",
        };

    private static TrackInfo Song(string id) =>
        new($"spotify:track:{id}", $"Song {id}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private LocalMediaSnapshot Snapshot(string title, bool playing) => new()
    {
        HasSession = true,
        Title = title,
        Artist = "Artist",
        Album = "Album",
        IsPlaying = playing,
        Position = TimeSpan.FromSeconds(10),
        PositionUpdatedAt = _time.GetUtcNow(),
        Duration = TimeSpan.FromSeconds(200),
        CanSeek = true,
    };

    private async Task StartSpotifyPlaying()
    {
        _channel.Report(Snapshot("Song A", playing: true));
        await _spotify.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_router.State.IsPlaying);
    }
}

public sealed class LikedSongsTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly LibraryService _library;
    private readonly LikedSongs _likes;
    private readonly List<LikeChange> _changes = [];

    public LikedSongsTests()
    {
        _library = new LibraryService(_web, cache: null);
        _likes = new LikedSongs(_web, _library);
        _likes.Changed += (_, change) => _changes.Add(change);
    }

    public void Dispose() => _library.Dispose();

    [Fact]
    public async Task Liking_shows_at_once_and_tells_every_list()
    {
        var answer = new TaskCompletionSource();
        _web.HoldLibraryWrites = answer.Task;

        var liking = _likes.SetLikedAsync(Song("a"), liked: true, TestContext.Current.CancellationToken);

        Assert.True(_likes.IsLiked("spotify:track:a"));
        var change = Assert.Single(_changes);
        Assert.True(change.IsLiked);
        Assert.Equal("spotify:track:a", change.Uri);
        Assert.Empty(_web.Commands);

        answer.SetResult();
        await liking;
        Assert.Equal(["save spotify:track:a"], _web.Commands);
        Assert.True(_likes.IsLiked("spotify:track:a"));
    }

    [Fact]
    public async Task A_refused_like_is_taken_back()
    {
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Insufficient client scope");

        await Assert.ThrowsAsync<SpotifyApiException>(() => _likes.SetLikedAsync(Song("a"), liked: true, TestContext.Current.CancellationToken));

        Assert.False(_likes.IsLiked("spotify:track:a"));
        Assert.Equal([true, false], _changes.Select(c => c.IsLiked));
    }

    [Fact]
    public async Task Unliking_a_song_removes_it()
    {
        _web.SavedTracks.Add(new SavedTrack { Track = new PlayableItem { Name = "Song a", Uri = "spotify:track:a", DurationMs = 1000 } });
        await _likes.LoadAsync(TestContext.Current.CancellationToken);
        _changes.Clear();

        await _likes.SetLikedAsync(Song("a"), liked: false, TestContext.Current.CancellationToken);

        Assert.False(_likes.IsLiked("spotify:track:a"));
        Assert.False(Assert.Single(_changes).IsLiked);
        Assert.Equal(["unsave spotify:track:a"], _web.Commands);
    }

    [Fact]
    public async Task Liking_a_liked_song_again_sends_nothing()
    {
        await _likes.SetLikedAsync(Song("a"), liked: true, TestContext.Current.CancellationToken);

        await _likes.SetLikedAsync(Song("a"), liked: true, TestContext.Current.CancellationToken);

        Assert.Single(_web.Commands);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task Files_from_the_computer_can_not_be_liked()
    {
        var file = Song("x") with { Uri = "spotify:local:Me:Demos:Demo:120", IsLocal = true, IsPlayable = false };

        await _likes.SetLikedAsync(file, liked: true, TestContext.Current.CancellationToken);

        Assert.Empty(_web.Commands);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Loading_reads_Liked_Songs_and_tells_every_list_once()
    {
        _web.SavedTracks.Add(new SavedTrack { Track = new PlayableItem { Name = "Song a", Uri = "spotify:track:a", DurationMs = 1000 } });
        Assert.False(_likes.IsLoaded);

        await _likes.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(_likes.IsLoaded);
        Assert.True(_likes.IsLiked("spotify:track:a"));
        Assert.False(_likes.IsLiked("spotify:track:b"));
        Assert.Null(Assert.Single(_changes).Track);
    }

    private static TrackInfo Song(string id) =>
        new($"spotify:track:{id}", $"Song {id}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true);
}

public sealed class PlaylistEditingTests : IDisposable
{
    private readonly FakeWebApi _web = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"resonate-edit-{Guid.NewGuid():N}");
    private readonly LibraryService _library;

    public PlaylistEditingTests()
    {
        _web.Playlists.Add(new SimplifiedPlaylist
        {
            Id = "p",
            Name = "Mine",
            Uri = "spotify:playlist:p",
            Owner = new PlaylistOwner { Id = "me" },
            SnapshotId = "v1",
            Items = new ItemsReference { Total = 5 },
        });
        _library = new LibraryService(_web, cache: null, lists: new TrackListStore(_folder));
    }

    public void Dispose()
    {
        _library.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Moving_a_song_down_uses_the_real_positions_in_the_playlist()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);

        // Spotify left out the entry at position 2, so the list shown has a gap.
        await _library.MovePlaylistTrackAsync("p", WithPositions(0, 1, 3, 4), 1, 3, TestContext.Current.CancellationToken);

        // Song 1 goes after the song at position 4: before position 5, counted before the move.
        Assert.Equal(["reorder p 1->5 x1"], _web.Commands);
    }

    [Fact]
    public async Task Moving_a_song_up_uses_the_real_positions_in_the_playlist()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);

        await _library.MovePlaylistTrackAsync("p", WithPositions(0, 1, 3, 4), 3, 0, TestContext.Current.CancellationToken);

        Assert.Equal(["reorder p 4->0 x1"], _web.Commands);
    }

    [Fact]
    public async Task A_move_in_a_list_without_gaps_keeps_the_stored_copy_in_step()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);

        await _library.MovePlaylistTrackAsync("p", WithPositions(0, 1, 2, 3), 0, 2, TestContext.Current.CancellationToken);
        var reads = _web.PlaylistItemReads;
        var stored = await _library.GetAllPlaylistTracksAsync("p", "snapshot-after-reorder", TestContext.Current.CancellationToken);

        Assert.Equal(reads, _web.PlaylistItemReads);
        Assert.Equal(["s1", "s2", "s0", "s3"], stored.Tracks.Select(t => t.Title));
        Assert.Equal([0, 1, 2, 3], stored.Tracks.Select(t => t.Position!.Value));
        Assert.Equal("snapshot-after-reorder", _library.Snapshot!.Playlists[0].SnapshotId);
    }

    [Fact]
    public async Task A_move_in_a_list_with_gaps_loads_the_playlist_again_next_time()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);

        await _library.MovePlaylistTrackAsync("p", WithPositions(0, 1, 3, 4), 0, 2, TestContext.Current.CancellationToken);
        var reads = _web.PlaylistItemReads;
        await _library.GetAllPlaylistTracksAsync("p", "snapshot-after-reorder", TestContext.Current.CancellationToken);

        Assert.True(_web.PlaylistItemReads > reads);
    }

    [Fact]
    public async Task Removing_a_song_removes_every_copy_and_keeps_positions_right()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);
        var changed = 0;
        _library.PlaylistsChanged += (_, _) => changed++;
        var before = WithPositions(0, 1, 2, 3, 4);
        before[3] = before[3] with { Uri = before[1].Uri };

        await _library.RemoveFromPlaylistAsync("p", before, before[1].Uri!, TestContext.Current.CancellationToken);
        var stored = await _library.GetAllPlaylistTracksAsync("p", "snapshot-after-remove", TestContext.Current.CancellationToken);

        Assert.Equal(["remove p spotify:track:1"], _web.Commands);
        Assert.Equal(["s0", "s2", "s4"], stored.Tracks.Select(t => t.Title));
        Assert.Equal([0, 1, 2], stored.Tracks.Select(t => t.Position!.Value));
        Assert.Equal(3, _library.Snapshot!.Playlists[0].ItemCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Removing_from_a_list_with_gaps_does_not_guess_the_new_positions()
    {
        await _library.RefreshAsync(TestContext.Current.CancellationToken);

        await _library.RemoveFromPlaylistAsync("p", WithPositions(0, 1, 3, 4), "spotify:track:1", TestContext.Current.CancellationToken);
        var reads = _web.PlaylistItemReads;
        await _library.GetAllPlaylistTracksAsync("p", "snapshot-after-remove", TestContext.Current.CancellationToken);

        Assert.True(_web.PlaylistItemReads > reads);
    }

    /// <summary>Songs s0, s1, … at the given positions in the playlist.</summary>
    private static List<TrackInfo> WithPositions(params int[] positions) =>
        positions
            .Select((position, i) => new TrackInfo($"spotify:track:{i}", $"s{i}", "Band", "Record", null, TimeSpan.FromSeconds(100), null, null, false, true) { Position = position })
            .ToList();
}

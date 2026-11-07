using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>A Spotify playlist's local songs play from their files in Local Files, in turns with its Spotify songs.</summary>
public sealed class MixedPlaylistTests : IDisposable
{
    private const string Playlist = "spotify:playlist:p";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _channel = new();
    private readonly FakeWebApi _web = new();
    private readonly FakeAudioEngine _engine = new();
    private readonly Random _random = new(42);
    private readonly FakeFinder _finder = new();
    private readonly PlayerController _spotify;
    private readonly LocalPlayer _files;
    private readonly PlayerRouter _router;
    private readonly List<string> _errors = [];

    public MixedPlaylistTests()
    {
        _spotify = new PlayerController(_channel, _channel, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time, max => _random.Next(max));
        _files = new LocalPlayer(_engine, readCover: _ => null, time: _time, random: _ => 0);
        _router = new PlayerRouter(_spotify, _files, _time, max => _random.Next(max)) { LocalSongs = _finder };
        _router.ErrorOccurred += (_, message) => _errors.Add(message);
    }

    public void Dispose()
    {
        _router.Dispose();
        _files.Dispose();
        _spotify.Dispose();
    }

    [Fact]
    public async Task A_picked_local_song_plays_from_its_file_and_keeps_its_playlist_address()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2), Song(3) };
        _finder.Add(list[1]);
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();

        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 1", _router.State.Title);
        Assert.Equal(list[1].Uri, _router.State.TrackUri);
        Assert.Equal(PathOf(list[1]), _router.State.FilePath);
        Assert.Equal("Mine", _router.State.SourceName);
        Assert.Equal([PathOf(list[1])], _engine.Opened);
        Assert.Empty(_web.PlayBodies);
        Assert.Equal(["Song 2", "Song 3"], Titles(_router.MixedUpcoming!));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task The_files_ending_starts_the_Spotify_songs_after_them()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2), Song(3) };
        _finder.Add(list[1]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();

        _engine.PlayToEnd(PathOf(list[1]));
        await _files.WhenIdleAsync();
        await WaitUntil(() => _web.PlayBodies.Count == 1);

        var body = _web.PlayBodies[0]!;
        Assert.Null(body.ContextUri);
        Assert.Equal(["spotify:track:2", "spotify:track:3"], body.Uris);
        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Equal("Song 2", _router.State.Title);
        Assert.False(_files.State.IsPlaying);
        Assert.Equal(["Song 3"], Titles(_router.MixedUpcoming!));
    }

    [Fact]
    public async Task The_Spotify_songs_ending_starts_the_files_after_them()
    {
        var list = new List<TrackInfo> { Song(0), Song(1), LocalSong(2), Song(3) };
        _finder.Add(list[2]);
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });

        Assert.Equal(["spotify:track:0", "spotify:track:1"], Assert.Single(_web.PlayBodies)!.Uris);
        _time.Advance(TimeSpan.FromSeconds(10));
        Playing(list[0], seconds: 10);
        _time.Advance(TimeSpan.FromSeconds(190));
        Playing(list[1]);
        _time.Advance(TimeSpan.FromSeconds(199.5));

        // Spotify stops at the end of the last song it was given.
        Paused(list[1], seconds: 200);
        await WaitUntil(() => _engine.Opened.Count == 1);
        await _files.WhenIdleAsync();

        Assert.Equal([PathOf(list[2])], _engine.Opened);
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 2", _router.State.Title);
        Assert.True(_router.State.IsPlaying);
    }

    [Fact]
    public async Task Spotify_playing_on_by_itself_after_its_songs_starts_the_files()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1) };
        _finder.Add(list[1]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        _time.Advance(TimeSpan.FromSeconds(10));
        Playing(list[0], seconds: 10);
        _time.Advance(TimeSpan.FromSeconds(190));

        // Autoplay: a song of Spotify's own choosing.
        Playing(Song(500));
        await WaitUntil(() => _engine.Opened.Count == 1);

        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 1", _router.State.Title);
        await WaitUntil(() => _channel.Commands.Contains("pause"));
    }

    [Fact]
    public async Task Pausing_the_last_Spotify_song_does_not_start_the_files()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1) };
        _finder.Add(list[1]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        _time.Advance(TimeSpan.FromSeconds(195));
        Playing(list[0], seconds: 195);

        await _router.PauseAsync();
        _time.Advance(TimeSpan.FromSeconds(4));
        Paused(list[0], seconds: 199);
        _time.Advance(TimeSpan.FromSeconds(20));
        Paused(list[0], seconds: 199);

        Assert.Empty(_engine.Opened);
        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Equal(["Demo 1"], Titles(_router.MixedUpcoming!));
    }

    [Fact]
    public async Task Next_and_Previous_cross_between_the_files_and_Spotify()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2) };
        _finder.Add(list[1]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();

        await _router.NextAsync();
        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Equal(["spotify:track:2"], Assert.Single(_web.PlayBodies)!.Uris);
        Assert.Equal("Song 2", _router.State.Title);

        await _router.PreviousAsync();
        await _files.WhenIdleAsync();
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 1", _router.State.Title);
        Assert.Equal([PathOf(list[1]), PathOf(list[1])], _engine.Opened);

        await _router.PreviousAsync();
        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Equal(["spotify:track:0"], _web.PlayBodies[^1]!.Uris);
        Assert.Equal(["Demo 1", "Song 2"], Titles(_router.MixedUpcoming!));
    }

    [Fact]
    public async Task Shuffle_mixes_the_files_in_with_the_Spotify_songs()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2), LocalSong(3), Song(4), Song(5) };
        _finder.Add(list[1]);
        _finder.Add(list[3]);
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, -1, Playlist, "Mine") { Shuffle = true });
        await _files.WhenIdleAsync();

        Assert.True(_router.State.Shuffle);
        var all = new[] { _router.State.Title! }.Concat(Titles(_router.MixedUpcoming!)).ToList();
        Assert.Equal(Titles(list).Order(), all.Order());
        Assert.NotEqual(Titles(list), all);
    }

    [Fact]
    public async Task Repeat_plays_the_whole_mixed_list_again()
    {
        var list = new List<TrackInfo> { LocalSong(0), Song(1) };
        _finder.Add(list[0]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();
        await _router.SetRepeatAsync(RepeatMode.All);
        Assert.Equal(RepeatMode.All, _router.State.Repeat);
        Assert.Equal(RepeatMode.Off, _files.State.Repeat);

        _engine.PlayToEnd(PathOf(list[0]));
        await _files.WhenIdleAsync();
        await WaitUntil(() => _web.PlayBodies.Count == 1);
        _time.Advance(TimeSpan.FromSeconds(10));
        Playing(list[1], seconds: 1);
        _time.Advance(TimeSpan.FromSeconds(199));
        Paused(list[1], seconds: 200);

        await WaitUntil(() => _engine.Opened.Count == 2);
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 0", _router.State.Title);
    }

    [Fact]
    public async Task After_the_end_Play_starts_the_mixed_list_again()
    {
        var list = new List<TrackInfo> { LocalSong(0), Song(1) };
        _finder.Add(list[0]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();
        _engine.PlayToEnd(PathOf(list[0]));
        await _files.WhenIdleAsync();
        await WaitUntil(() => _web.PlayBodies.Count == 1);
        _time.Advance(TimeSpan.FromSeconds(10));
        Playing(list[1], seconds: 1);
        _time.Advance(TimeSpan.FromSeconds(199));

        Paused(list[1], seconds: 200);
        Assert.False(_router.State.IsPlaying);
        Assert.Null(_router.MixedUpcoming);
        Assert.Single(_engine.Opened);

        await _router.PlayAsync();
        await _files.WhenIdleAsync();
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Equal("Demo 0", _router.State.Title);
        Assert.Equal(2, _engine.Opened.Count);
    }

    [Fact]
    public async Task A_picked_local_song_that_is_not_in_Local_Files_says_so_and_the_next_plays()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), LocalSong(2), Song(3) };
        _finder.Add(list[2]);
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();

        Assert.Contains("Demo 1", Assert.Single(_errors), StringComparison.Ordinal);
        Assert.Equal("Demo 2", _router.State.Title);
        Assert.Equal([PathOf(list[2])], _engine.Opened);
        Assert.Equal(["Song 3"], Titles(_router.MixedUpcoming!));
    }

    [Fact]
    public async Task Without_any_file_found_a_sorted_list_plays_the_next_Spotify_song()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2) };
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, 1, null, "Sorted") { Shuffle = false });

        Assert.Contains("isn't in Local Files", Assert.Single(_errors), StringComparison.Ordinal);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(["spotify:track:0", "spotify:track:2"], body.Uris);
        Assert.Equal(1, body.Offset!.Position);
        Assert.Null(_router.MixedUpcoming);
    }

    [Fact]
    public async Task Without_any_file_found_the_playlist_plays_in_Spotify_as_before()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1), Song(2) };
        await StartAsync();

        await _router.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal(1, body.Offset!.Position);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Queued_songs_play_after_the_current_one_in_a_mixed_list()
    {
        var list = new List<TrackInfo> { LocalSong(0), Song(1) };
        _finder.Add(list[0]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        await _files.WhenIdleAsync();

        await _router.AddToQueueAsync(Song(7));

        Assert.Equal(["Song 7", "Song 1"], Titles(_router.MixedUpcoming!));
        _engine.PlayToEnd(PathOf(list[0]));
        await _files.WhenIdleAsync();
        await WaitUntil(() => _web.PlayBodies.Count == 1);
        Assert.Equal(["spotify:track:7", "spotify:track:1"], _web.PlayBodies[0]!.Uris);
    }

    [Fact]
    public async Task Music_started_elsewhere_ends_the_mixed_list()
    {
        var list = new List<TrackInfo> { Song(0), LocalSong(1) };
        _finder.Add(list[1]);
        await StartAsync();
        await _router.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = false });
        _time.Advance(TimeSpan.FromSeconds(10));
        Playing(list[0], seconds: 10);

        // Another playlist started on the phone, mid-song.
        _time.Advance(TimeSpan.FromSeconds(20));
        var other = Song(500);
        Playing(other);
        _web.Playback = new PlaybackState
        {
            Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
            IsPlaying = true,
            ProgressMs = 1000,
            Context = new PlaybackContext { Uri = "spotify:playlist:other" },
            Item = new PlayableItem { Name = other.Title, Uri = other.Uri, DurationMs = 200_000 },
        };
        await _spotify.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);

        Assert.Null(_router.MixedUpcoming);
        Assert.Empty(_engine.Opened);
        Assert.Equal("Song 500", _router.State.Title);
    }

    private static TrackInfo Song(int i) =>
        new($"spotify:track:{i}", $"Song {i}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true)
        {
            Position = i,
        };

    private static TrackInfo LocalSong(int position) =>
        new($"spotify:local:Me:Demos:Demo+{position}:120", $"Demo {position}", "Me", "Demos", null, TimeSpan.FromSeconds(120), null, null, IsExplicit: false, IsPlayable: false)
        {
            IsLocal = true,
            Position = position,
        };

    private static string PathOf(TrackInfo song) => $"/music/{song.Title.Replace(' ', '-')}.mp3";

    private static List<string> Titles(IEnumerable<TrackInfo> tracks) => [.. tracks.Select(t => t.Title)];

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private void Playing(TrackInfo song, double seconds = 0.5) => Report(song, seconds, playing: true);

    private void Paused(TrackInfo song, double seconds) => Report(song, seconds, playing: false);

    private void Report(TrackInfo song, double seconds, bool playing) =>
        _channel.Report(new LocalMediaSnapshot
        {
            HasSession = true,
            Title = song.Title,
            Artist = song.Artists,
            Album = song.Album,
            IsPlaying = playing,
            Position = TimeSpan.FromSeconds(seconds),
            PositionUpdatedAt = _time.GetUtcNow(),
            Duration = song.Duration,
            CanSeek = true,
            CanSkipNext = true,
            CanSkipPrevious = true,
        });

    private async Task StartAsync()
    {
        Playing(Song(1000), seconds: 60);
        await _spotify.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Local Files, holding the songs added to it.</summary>
    private sealed class FakeFinder : ILocalSongFinder
    {
        private readonly HashSet<string> _found = [];

        public void Add(TrackInfo song) => _found.Add(song.Uri!);

        public Task<IReadOnlyList<string?>> FindAsync(IReadOnlyList<TrackInfo> songs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string?>>([.. songs.Select(s => s.IsLocal && _found.Contains(s.Uri!) ? PathOf(s) : null)]);
    }
}

using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>Up next: editing what plays next, in Resonate's own order for Spotify and in the local files player's queue.</summary>
public sealed class UpNextTests : IDisposable
{
    private const string Playlist = "spotify:playlist:p";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-08T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _local = new();
    private readonly FakeWebApi _web = new();
    private readonly PlayerController _player;
    private readonly List<string> _errors = [];

    public UpNextTests()
    {
        _player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time, _ => 0);
        _player.ErrorOccurred += (_, message) => _errors.Add(message);
    }

    public void Dispose() => _player.Dispose();

    // ---- The edits ----

    [Theory]
    [InlineData(2, 0, "c a b | d", 3)] // Into the queued songs: queued too.
    [InlineData(0, 3, "b | c d a", 1)] // Out of them: no longer queued.
    [InlineData(0, 1, "b a | c d", 2)]
    [InlineData(3, 2, "a b | d c", 2)]
    public void A_move_keeps_track_of_the_queued_songs(int from, int to, string expected, int queued)
    {
        var songs = Songs(4);

        var nowQueued = UpNextEdits.Move(songs, queued: 2, from, to);

        Assert.Equal(queued, nowQueued);
        var shown = songs.Select((s, i) => (i == nowQueued ? "| " : string.Empty) + "abcd"[s.Position!.Value]);
        Assert.Equal(expected, string.Join(' ', shown));
    }

    [Fact]
    public void Removing_and_adding_keep_track_of_the_queued_songs()
    {
        var songs = Songs(5);

        Assert.Equal(1, UpNextEdits.Remove(songs, queued: 2, [0, 3]));
        Assert.Equal(["Song 1", "Song 2", "Song 4"], Titles(songs));

        Assert.Equal(2, UpNextEdits.Insert(songs, 1, Song(9), playNext: false));
        Assert.Equal(3, UpNextEdits.Insert(songs, 2, Song(8), playNext: true));
        Assert.Equal(["Song 8", "Song 1", "Song 9", "Song 2", "Song 4"], Titles(songs));
    }

    // ---- Spotify: Resonate's own order ----

    [Fact]
    public async Task An_edit_shows_at_once_and_Spotify_gets_one_new_window_two_seconds_later_at_the_same_place()
    {
        var songs = Songs(10);
        await PlayAsync(songs);
        var moved = _player.UpNext!.Upcoming[5];

        Assert.True(_player.MoveUpNext(new UpNextPick(5, moved), 0));
        Assert.Same(moved, _player.UpNext!.Upcoming[0]);

        // A second edit within the pause makes it one switch.
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_player.RemoveUpNext([new UpNextPick(1, songs[1])]));
        _time.Advance(TimeSpan.FromSeconds(1.9));
        await AfterQueuedCommandsAsync();
        Assert.Single(_web.PlayBodies);

        _time.Advance(TimeSpan.FromSeconds(0.2));
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        var body = _web.PlayBodies[1]!;
        Assert.Null(body.ContextUri);
        Assert.Null(body.Offset);
        Assert.Equal(Uris([songs[0], songs[6], songs[2], songs[3], songs[4], songs[5], songs[7], songs[8], songs[9]]), body.Uris);
        Assert.InRange(body.PositionMs!.Value, 3_000, 5_000);
        Assert.Equal("Song 0", _player.State.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Nothing_is_sent_while_a_song_is_being_dragged()
    {
        var songs = Songs(6);
        await PlayAsync(songs);

        _player.HoldUpNext(true);
        Assert.True(_player.MoveUpNext(new UpNextPick(0, songs[1]), 3));
        _time.Advance(TimeSpan.FromSeconds(5));
        await AfterQueuedCommandsAsync();
        Assert.Single(_web.PlayBodies);

        _player.HoldUpNext(false);
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        Assert.Equal(Uris([songs[0], songs[2], songs[3], songs[4], songs[1], songs[5]]), _web.PlayBodies[1]!.Uris);
    }

    [Fact]
    public async Task Songs_already_played_stay_in_the_window_so_previous_still_works()
    {
        var songs = Songs(20);
        await PlayAsync(songs);
        for (var i = 1; i <= 12; i++)
        {
            Playing(songs[i]);
        }

        Assert.True(_player.ShuffleUpNext());
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _web.PlayBodies.Count == 2);

        var body = _web.PlayBodies[1]!;
        Assert.Equal(ListSession.SongsKeptBehind, body.Offset!.Position);
        Assert.Equal(Uris(songs.Skip(2).Take(11)), body.Uris!.Take(11));
        Assert.Equal(Uris(songs.Skip(13)).Order(), body.Uris!.Skip(11).Order());
    }

    [Fact]
    public async Task A_playlist_in_its_own_order_becomes_Resonates_order_with_the_first_edit()
    {
        List<TrackInfo> songs = [Song(0), Song(1), LocalFile(2), Song(3), Song(4)];
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 1, Playlist, "Mix") { Shuffle = false });
        Playing(songs[1]);
        _time.Advance(TimeSpan.FromSeconds(10));

        // Spotify's own files can not be named in a list, so they are not offered.
        var upNext = _player.UpNext!;
        Assert.True(upNext.CanEdit);
        Assert.Equal(["Song 3", "Song 4"], Titles(upNext.Upcoming));
        Assert.True(_player.MoveUpNext(new UpNextPick(1, songs[4]), 0));

        // Spotify still plays the playlist until it gets the new order: that is not music from elsewhere.
        _web.Playback = OnTheWeb(songs[1], context: Playlist);
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Mix", _player.State.SourceName);

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        var body = _web.PlayBodies[1]!;
        Assert.Null(body.ContextUri);
        Assert.Equal(Uris([songs[0], songs[1], songs[4], songs[3]]), body.Uris);
        Assert.Equal(1, body.Offset!.Position);
        Assert.Null(_player.State.ContextUri);
    }

    [Fact]
    public async Task Added_songs_play_next_in_the_order_added_and_play_next_goes_first()
    {
        var songs = Songs(5);
        await PlayAsync(songs);

        Assert.True(_player.AddToUpNext(Song(10), playNext: false));
        Assert.True(_player.AddToUpNext(Song(11), playNext: false));
        Assert.True(_player.AddToUpNext(Song(12), playNext: true));
        Assert.Equal(["Song 12", "Song 10", "Song 11", "Song 1"], Titles(_player.UpNext!.Upcoming.Take(4)));

        // Once one of them played, the next one added still joins them, ahead of the list.
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        Playing(Song(12));
        Assert.True(_player.AddToUpNext(Song(13), playNext: false));
        Assert.Equal(["Song 10", "Song 11", "Song 13", "Song 1"], Titles(_player.UpNext!.Upcoming.Take(4)));

        // Files from the computer can not join Spotify's list.
        Assert.False(_player.AddToUpNext(LocalFile(7), playNext: false));
    }

    [Fact]
    public async Task Next_with_edits_waiting_plays_the_edited_orders_next_song_in_one_command()
    {
        var songs = Songs(6);
        await PlayAsync(songs);
        Assert.True(_player.MoveUpNext(new UpNextPick(3, songs[4]), 0));
        _local.Commands.Clear();

        await _player.NextAsync();

        Assert.Equal("Song 4", _player.State.Title);
        Assert.DoesNotContain("next", _local.Commands);
        var body = _web.PlayBodies[1]!;
        Assert.Equal(Uris([songs[0], songs[4], songs[1], songs[2], songs[3], songs[5]]), body.Uris);
        Assert.Equal(1, body.Offset!.Position);
        Assert.Null(body.PositionMs);
    }

    [Fact]
    public async Task Play_with_edits_waiting_starts_the_edited_order_where_the_song_was_paused()
    {
        var songs = Songs(6);
        await PlayAsync(songs);
        _time.Advance(TimeSpan.FromSeconds(10));
        Paused(songs[0], seconds: 30);
        Assert.True(_player.ClearUpNext());
        _time.Advance(TimeSpan.FromSeconds(5));
        await AfterQueuedCommandsAsync();
        Assert.Single(_web.PlayBodies);
        _local.Commands.Clear();

        await _player.PlayAsync();

        Assert.True(_player.State.IsPlaying);
        Assert.DoesNotContain("play", _local.Commands);
        var body = _web.PlayBodies[1]!;
        Assert.Equal(Uris([songs[0]]), body.Uris);
        Assert.Equal(30_000, body.PositionMs);
    }

    [Fact]
    public async Task A_song_Spotify_starts_from_its_old_order_gives_way_to_the_edited_orders_next_song()
    {
        var songs = Songs(6);
        await PlayAsync(songs);
        Assert.True(_player.RemoveUpNext([new UpNextPick(0, songs[1])]));

        // The song ended before Spotify had the new order.
        Playing(songs[1]);

        await WaitUntil(() => _web.PlayBodies.Count == 2);
        Assert.Equal("Song 2", _player.State.Title);
        var body = _web.PlayBodies[1]!;
        Assert.Equal(Uris([songs[0], songs[2], songs[3], songs[4], songs[5]]), body.Uris);
        Assert.Equal(1, body.Offset!.Position);
    }

    [Fact]
    public async Task After_clearing_what_was_next_the_music_stops_with_the_current_song()
    {
        var songs = Songs(4);
        await PlayAsync(songs);
        Assert.True(_player.ClearUpNext());
        Assert.Empty(_player.UpNext!.Upcoming);
        _local.Commands.Clear();

        Playing(songs[1]);

        await WaitUntil(() => _local.Commands.Contains("pause"));
        Assert.False(_player.State.IsPlaying);
        Assert.Single(_web.PlayBodies);
    }

    [Fact]
    public async Task Music_started_elsewhere_has_no_editable_queue()
    {
        await StartAsync();

        Assert.Null(_player.UpNext);
        Assert.False(_player.MoveUpNext(new UpNextPick(0, Song(1)), 1));
        Assert.False(_player.AddToUpNext(Song(1), playNext: true));

        // The router then uses Spotify's own queue.
        using var router = new PlayerRouter(_player);
        await router.AddToUpNextAsync(Song(1), playNext: true);
        Assert.Contains("queue spotify:track:1@here", _web.Commands);
    }

    [Fact]
    public async Task A_playlist_known_only_in_part_can_not_be_edited()
    {
        var songs = Songs(4);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, Playlist, "Mix") { Shuffle = false, IsPartial = true });
        Playing(songs[0]);

        Assert.False(_player.UpNext!.CanEdit);
        Assert.True(_player.UpNext!.PartlyKnown);
        Assert.False(_player.ClearUpNext());
    }

    [Fact]
    public async Task An_edit_that_finds_another_song_in_its_place_changes_nothing()
    {
        var songs = Songs(5);
        await PlayAsync(songs);

        Assert.False(_player.MoveUpNext(new UpNextPick(0, songs[3]), 2));
        Assert.False(_player.RemoveUpNext([new UpNextPick(9, songs[1])]));
        Assert.Equal(Titles(songs.Skip(1)), Titles(_player.UpNext!.Upcoming));
    }

    [Fact]
    public async Task Edits_Spotify_does_not_take_wait_for_another_try()
    {
        var songs = Songs(5);
        await PlayAsync(songs);
        Assert.True(_player.ShuffleUpNext());
        _web.FailNextCommand = new HttpRequestException("offline");

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _errors.Count == 1);

        _time.Advance(PlayerController.UpNextRetryDelay);
        Playing(songs[0], seconds: 40);
        await WaitUntil(() => _web.PlayBodies.Count == 3);
        Assert.Single(_errors);
    }

    // ---- The local files player ----

    [Fact]
    public void The_local_queue_can_be_reordered_and_the_queued_songs_stay_queued()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs(4), 0, shuffle: false);
        queue.AddToQueue(Song(9));

        Assert.True(queue.MoveUpNext(new UpNextPick(3, queue.Upcoming[3]), 1));

        Assert.Equal(["Song 9", "Song 3", "Song 1", "Song 2"], Titles(queue.Upcoming));
        Assert.Equal(["Song 9"], Titles(queue.Queued));
        Assert.Equal("Song 9", queue.MoveNext(skipped: true)?.Title);
        Assert.Equal("Song 3", queue.MoveNext(skipped: true)?.Title);

        // The songs played before are still there for "previous".
        Assert.Equal("Song 0", queue.MovePrevious()?.Title);
    }

    [Fact]
    public void The_local_queue_can_be_cleared_shuffled_and_given_a_song_to_play_next()
    {
        var queue = new LocalPlayQueue(_ => 0);
        queue.Load(Songs(4), 1, shuffle: false);

        Assert.True(queue.PlayNext(Song(9)));
        Assert.Equal(["Song 9", "Song 2", "Song 3"], Titles(queue.Upcoming));

        Assert.True(queue.ShuffleUpNext());
        Assert.Equal(["Song 2", "Song 3", "Song 9"], Titles(queue.Upcoming));
        Assert.Empty(queue.Queued);

        Assert.True(queue.ClearUpNext());
        Assert.Empty(queue.Upcoming);
        Assert.Null(queue.MoveNext(skipped: false));
    }

    [Fact]
    public void With_repeat_on_the_edited_local_order_comes_round_again()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs(4), 2, shuffle: false);
        queue.Repeat = RepeatMode.All;
        Assert.Equal(["Song 3", "Song 0", "Song 1"], Titles(queue.Upcoming));

        Assert.True(queue.RemoveUpNext([new UpNextPick(1, queue.Upcoming[1])]));

        Assert.Equal(["Song 3", "Song 1"], Titles(queue.Upcoming));
        Assert.Equal("Song 3", queue.MoveNext(skipped: false)?.Title);
        Assert.Equal("Song 1", queue.MoveNext(skipped: false)?.Title);
        Assert.Equal("Song 2", queue.MoveNext(skipped: false)?.Title);
        Assert.Equal(["Song 3", "Song 1"], Titles(queue.Upcoming));
    }

    [Fact]
    public async Task The_local_player_tells_the_engine_the_new_next_song()
    {
        var engine = new FakeAudioEngine();
        using var player = new LocalPlayer(engine, readCover: _ => null, time: _time);
        var files = Enumerable.Range(0, 3).Select(i => Song(i) with { Uri = null, FilePath = $"C:\\Music\\{i}.mp3" }).ToList();
        await player.PlayAsync(new PlayRequest(files, 0, null, "Local Files"));

        Assert.True(player.MoveUpNext(new UpNextPick(1, files[2]), 0));

        Assert.Equal(["Song 2", "Song 1"], Titles(player.UpNext!.Upcoming));
        await WaitUntil(() => engine.Next == files[2].FilePath);
    }

    private static TrackInfo Song(int i) =>
        new($"spotify:track:{i}", $"Song {i}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true)
        {
            Position = i,
        };

    private static List<TrackInfo> Songs(int count) => Enumerable.Range(0, count).Select(Song).ToList();

    private static TrackInfo LocalFile(int position) =>
        new($"spotify:local:Me:Demos:Demo+{position}:120", $"Demo {position}", "Me", "Demos", null, TimeSpan.FromSeconds(120), null, null, IsExplicit: false, IsPlayable: false)
        {
            IsLocal = true,
            Position = position,
        };

    private static List<string> Uris(IEnumerable<TrackInfo> tracks) => tracks.Select(t => t.Uri!).ToList();

    private static List<string> Titles(IEnumerable<TrackInfo> tracks) => tracks.Select(t => t.Title).ToList();

    private static PlaybackState OnTheWeb(TrackInfo song, string? context) => new()
    {
        Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
        IsPlaying = true,
        ProgressMs = 1000,
        ShuffleState = false,
        Context = context is null ? null : new PlaybackContext { Uri = context },
        Item = new PlayableItem { Name = song.Title, Uri = song.Uri, DurationMs = (int)song.Duration.TotalMilliseconds },
    };

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Plays <paramref name="songs"/> as Resonate's own list in the order shown, from the first; Spotify reports it playing.</summary>
    private async Task PlayAsync(List<TrackInfo> songs)
    {
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, null, "Search") { Shuffle = false });
        Playing(songs[0], seconds: 1.5);
    }

    private void Playing(TrackInfo song, double seconds = 0.5) => Report(song, seconds, playing: true);

    private void Paused(TrackInfo song, double seconds) => Report(song, seconds, playing: false);

    private void Report(TrackInfo song, double seconds, bool playing) =>
        _local.Report(new LocalMediaSnapshot
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

    /// <summary>Commands run one after another, so once a later one is done, every earlier one is too.</summary>
    private Task AfterQueuedCommandsAsync() => _player.SeekAsync(_player.State.PositionAt(_time.GetUtcNow()));

    private async Task StartAsync()
    {
        Playing(Song(1000), seconds: 60);
        await _player.StartAsync(TestContext.Current.CancellationToken);
    }
}

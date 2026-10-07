using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>Playing whole lists: Resonate's own shuffle, windows of songs, repeat, and files from the computer.</summary>
public sealed class ListPlaybackTests : IDisposable
{
    private const string Playlist = "spotify:playlist:p";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _local = new();
    private readonly FakeWebApi _web = new();
    private readonly Random _random = new(42);
    private readonly PlayerController _player;
    private readonly List<string> _errors = [];

    public ListPlaybackTests()
    {
        _player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time, max => _random.Next(max));
        _player.ErrorOccurred += (_, message) => _errors.Add(message);
    }

    public void Dispose() => _player.Dispose();

    [Fact]
    public async Task Shuffle_play_switches_Spotifys_shuffle_off_then_sends_a_random_order_starting_with_the_picked_song()
    {
        var songs = Songs(30);
        await StartAsync();

        var sent = _player.PlayAsync(new PlayRequest(songs, 7, Playlist, "Mix") { Shuffle = true });

        Assert.True(_player.State.Shuffle);
        Assert.Equal("Song 7", _player.State.Title);
        await sent;

        Assert.Equal(["shuffle off@here", "play@here"], _web.Commands);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Null(body.Offset);
        Assert.Equal(songs[7].Uri, body.Uris![0]);
        Assert.Equal(Uris(songs).Order(), body.Uris.Order());
        Assert.NotEqual(Uris(songs), body.Uris);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Shuffle_play_without_a_picked_song_shuffles_everything()
    {
        var songs = Songs(30);
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(songs, -1, Playlist, "Mix") { Shuffle = true });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Uris(songs).Order(), body.Uris!.Order());
        Assert.NotEqual(Uris(songs), body.Uris);
    }

    [Fact]
    public async Task Spotifys_shuffle_is_not_switched_again_when_it_is_known_to_be_off()
    {
        _web.Playback = OnTheWeb(Songs(1)[0], shuffle: false);
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(Songs(5), 0, null, "Search") { Shuffle = true });

        Assert.Equal(["play@here"], _web.Commands);
    }

    [Fact]
    public async Task Spotify_reporting_its_own_shuffle_off_does_not_turn_Resonates_off()
    {
        var songs = Songs(10);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 3, Playlist, "Mix") { Shuffle = true });
        Playing(songs[3]);

        // Spotify's own shuffle is off on purpose, and says so.
        _web.Playback = OnTheWeb(songs[3], shuffle: false);
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        Assert.True(_player.State.Shuffle);

        _time.Advance(TimeSpan.FromSeconds(30));
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        Assert.True(_player.State.Shuffle);
        Assert.True(_player.State.CanShuffle);
    }

    [Fact]
    public async Task Music_started_elsewhere_hands_shuffle_back_to_Spotify()
    {
        var songs = Songs(10);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 3, Playlist, "Mix") { Shuffle = true });
        Playing(songs[3]);

        // Another playlist started on the phone, without shuffle.
        _time.Advance(TimeSpan.FromSeconds(30));
        var other = Song(500);
        Playing(other);
        _web.Playback = OnTheWeb(other, shuffle: false, context: "spotify:playlist:other");
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        Assert.False(_player.State.Shuffle);

        _web.Commands.Clear();
        await _player.SetShuffleAsync(true);
        Assert.Equal(["shuffle on@here"], _web.Commands);
        Assert.True(_player.State.Shuffle);
    }

    [Fact]
    public async Task Without_a_list_Resonate_knows_shuffle_is_Spotifys_own()
    {
        await StartAsync();

        await _player.SetShuffleAsync(true);

        Assert.True(_player.State.Shuffle);
        Assert.Equal(["shuffle on@here"], _web.Commands);
        Assert.Empty(_web.PlayBodies);
    }

    [Fact]
    public async Task A_playlist_Spotify_will_not_list_shuffles_with_Spotifys_own_shuffle()
    {
        await StartAsync();

        var sent = _player.PlayAsync(new PlayRequest([], -1, "spotify:playlist:hidden", "Someone's playlist") { Shuffle = true });

        Assert.True(_player.State.Shuffle);
        await sent;
        Assert.Equal(["shuffle on@here", "play@here"], _web.Commands);
        Assert.Equal("spotify:playlist:hidden", Assert.Single(_web.PlayBodies)!.ContextUri);
    }

    [Fact]
    public async Task A_lone_song_from_search_plays_inside_its_album()
    {
        await StartAsync();
        var song = Song(1) with { AlbumUri = "spotify:album:a" };

        await _player.PlayAsync(new PlayRequest([song], 0, "spotify:album:a", "Album"));

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal("spotify:album:a", body.ContextUri);
        Assert.Equal(song.Uri, body.Offset!.Uri);
    }

    [Fact]
    public async Task Play_in_order_turns_shuffle_off_and_plays_inside_the_context()
    {
        var songs = Songs(10);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 3, Playlist, "Mix") { Shuffle = true });
        _web.Commands.Clear();
        _web.PlayBodies.Clear();

        await _player.PlayAsync(new PlayRequest(songs, -1, Playlist, "Mix") { Shuffle = false });

        Assert.False(_player.State.Shuffle);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Null(body.Offset);
        Assert.Equal(["play@here"], _web.Commands);
    }

    [Fact]
    public async Task Play_in_order_switches_Spotifys_own_shuffle_off_first()
    {
        _web.Playback = OnTheWeb(Song(99), shuffle: true);
        await StartAsync();
        Assert.True(_player.State.Shuffle);

        await _player.PlayAsync(new PlayRequest(Songs(5), 2, Playlist, "Mix") { Shuffle = false });

        Assert.Equal(["shuffle off@here", "play@here"], _web.Commands);
        Assert.Equal("spotify:track:2", Assert.Single(_web.PlayBodies)!.Offset!.Uri);
        Assert.False(_player.State.Shuffle);
    }

    [Fact]
    public async Task Double_clicking_a_song_keeps_the_shuffle_setting()
    {
        var songs = Songs(10);
        await StartAsync();
        await _player.SetShuffleAsync(true);
        _web.Commands.Clear();

        await _player.PlayAsync(new PlayRequest(songs, 4, Playlist, "Mix"));

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(songs[4].Uri, body.Uris![0]);
        Assert.Equal(10, body.Uris.Count);
        Assert.Equal(["shuffle off@here", "play@here"], _web.Commands);
        Assert.True(_player.State.Shuffle);
    }

    [Fact]
    public async Task Turning_shuffle_on_plans_the_rest_after_the_current_song_which_keeps_playing()
    {
        var songs = Songs(8);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 2, Playlist, "Mix") { Shuffle = false });
        Playing(songs[2]);
        _time.Advance(TimeSpan.FromSeconds(29.5));
        _web.Commands.Clear();
        _web.PlayBodies.Clear();

        var sent = _player.SetShuffleAsync(true);

        Assert.True(_player.State.Shuffle);
        await sent;
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal(songs[2].Uri, body.Uris![0]);
        Assert.Equal(Uris(songs).Order(), body.Uris.Order());
        Assert.Equal(30_000, body.PositionMs);

        // Never Spotify's own shuffle.
        Assert.Equal(["play@here"], _web.Commands);
    }

    [Fact]
    public async Task Turning_shuffle_off_goes_back_to_the_playlists_order_from_the_current_song()
    {
        var songs = Songs(8);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 2, Playlist, "Mix") { Shuffle = true });
        var order = Order();
        Playing(SongBy(order[0]));
        _time.Advance(TimeSpan.FromSeconds(200));
        Playing(SongBy(order[1]), seconds: 40);
        _web.PlayBodies.Clear();

        await _player.SetShuffleAsync(false);

        Assert.False(_player.State.Shuffle);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal(order[1], body.Offset!.Uri);
        Assert.Equal(40_000, body.PositionMs);
    }

    [Fact]
    public async Task Turning_shuffle_off_in_a_list_without_a_context_plays_it_in_order_from_the_current_song()
    {
        var songs = Songs(8);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 2, null, "Sorted") { Shuffle = true });
        var order = Order();
        Playing(SongBy(order[0]));
        Playing(SongBy(order[1]), seconds: 12);
        _web.PlayBodies.Clear();

        await _player.SetShuffleAsync(false);

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Uris(songs), body.Uris);
        Assert.Equal(Uris(songs).IndexOf(order[1]), body.Offset?.Position ?? 0);
        Assert.Equal(12_000, body.PositionMs);
    }

    [Fact]
    public async Task Switching_shuffle_while_paused_keeps_the_music_paused_until_it_plays_again()
    {
        var songs = Songs(8);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 2, Playlist, "Mix") { Shuffle = false });
        Playing(songs[2]);
        _time.Advance(TimeSpan.FromSeconds(29.5));
        await _player.PauseAsync();
        Paused(songs[2], seconds: 30);
        _web.Commands.Clear();
        _web.PlayBodies.Clear();

        await _player.SetShuffleAsync(true);
        await AfterQueuedCommandsAsync();

        // Spotify's play command would start the music, so nothing is sent yet.
        Assert.True(_player.State.Shuffle);
        Assert.False(_player.State.IsPlaying);
        Assert.Empty(_web.PlayBodies);
        Assert.Empty(_web.Commands);

        // Once it plays again, the new order starts with the same song at the same spot.
        await _player.PlayAsync();
        Playing(songs[2], seconds: 30);
        await WaitUntil(() => _web.PlayBodies.Count == 1);
        var body = _web.PlayBodies[0]!;
        Assert.Equal(songs[2].Uri, body.Uris![0]);
        Assert.Equal(Uris(songs).Order(), body.Uris.Order());
        Assert.Equal(30_000, body.PositionMs);
        Assert.True(_player.State.Shuffle);
    }

    [Fact]
    public async Task Switching_repeat_all_while_paused_on_the_last_song_keeps_the_music_paused()
    {
        var songs = Songs(5);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, Playlist, "Mix") { Shuffle = true });
        var order = Order();
        Playing(SongBy(order[0]));
        Playing(SongBy(order[4]), seconds: 3);
        await _player.PauseAsync();
        Paused(SongBy(order[4]), seconds: 3);

        await _player.SetRepeatAsync(RepeatMode.All);
        await AfterQueuedCommandsAsync();

        Assert.Single(_web.PlayBodies);
        Assert.False(_player.State.IsPlaying);

        // Once it plays again, the next pass follows.
        await _player.PlayAsync();
        Playing(SongBy(order[4]), seconds: 3);
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        Assert.Equal(order[4], _web.PlayBodies[1]!.Uris![0]);
    }

    [Fact]
    public async Task A_failed_shuffle_switch_puts_back_the_old_plan()
    {
        var songs = Songs(8);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 2, Playlist, "Mix") { Shuffle = false });
        Playing(songs[2]);
        _web.FailNextCommand = new HttpRequestException("offline");

        await _player.SetShuffleAsync(true);

        Assert.False(_player.State.Shuffle);
        Assert.Single(_errors);

        // The old plan is back: turning shuffle on again plans again.
        _web.PlayBodies.Clear();
        await _player.SetShuffleAsync(true);
        Assert.Equal(songs[2].Uri, Assert.Single(_web.PlayBodies)!.Uris![0]);
    }

    [Fact]
    public async Task A_long_list_plays_in_windows_that_follow_on_as_each_last_song_starts()
    {
        var songs = Songs(250);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, null, "Everything") { Shuffle = true });
        var first = _web.PlayBodies[0]!.Uris!;
        Assert.Equal(PlayerController.MaxUrisPerRequest, first.Count);

        Playing(SongBy(first[0]));
        Playing(SongBy(first[50]));
        Assert.Single(_web.PlayBodies);

        // The window's last song starts: the next window follows, starting with it.
        Playing(SongBy(first[99]), seconds: 2);
        await WaitUntil(() => _web.PlayBodies.Count == 2);
        var second = _web.PlayBodies[1]!;
        Assert.Equal(first[99], second.Uris![0]);
        Assert.Equal(100, second.Uris.Count);
        Assert.Null(second.Offset);
        Assert.Equal(2_000, second.PositionMs);

        Playing(SongBy(second.Uris[99]));
        await WaitUntil(() => _web.PlayBodies.Count == 3);
        var third = _web.PlayBodies[2]!.Uris!;
        Assert.Equal(52, third.Count);

        // Every song once, in one random order across the windows.
        var all = first.Take(99).Concat(second.Uris.Take(99)).Concat(third).ToList();
        Assert.Equal(Uris(songs).Order(), all.Order());
    }

    [Fact]
    public async Task Repeat_all_starts_another_pass_in_a_fresh_random_order()
    {
        var songs = Songs(5);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, Playlist, "Mix") { Shuffle = true });
        await _player.SetRepeatAsync(RepeatMode.All);
        var order = Order();
        Playing(SongBy(order[0]));

        Playing(SongBy(order[4]));
        await WaitUntil(() => _web.PlayBodies.Count == 2);

        var next = _web.PlayBodies[1]!.Uris!;
        Assert.Equal(order[4], next[0]);
        Assert.Equal(Uris(songs).Order(), next.Skip(1).Order());
        Assert.NotEqual(order[4], next[1]);
        await WaitUntil(() => _web.Commands.Count(c => c == "repeat context@here") == 2);
    }

    [Fact]
    public async Task Repeat_all_on_a_list_in_order_is_left_to_Spotify()
    {
        var songs = Songs(5);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, null, "Search") { Shuffle = false });
        await _player.SetRepeatAsync(RepeatMode.All);
        Playing(songs[0]);

        Playing(songs[4]);
        Playing(songs[0]);
        await AfterQueuedCommandsAsync();

        Assert.Single(_web.PlayBodies);
        Assert.Equal(Uris(songs), _web.PlayBodies[0]!.Uris);
    }

    [Fact]
    public async Task Without_repeat_the_lists_end_is_the_end()
    {
        var songs = Songs(5);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 0, Playlist, "Mix") { Shuffle = true });
        var order = Order();
        Playing(SongBy(order[0]));

        Playing(SongBy(order[4]));
        await AfterQueuedCommandsAsync();

        Assert.Single(_web.PlayBodies);
    }

    [Fact]
    public async Task Repeat_one_is_Spotifys_track_repeat()
    {
        await StartAsync();

        await _player.SetRepeatAsync(RepeatMode.One);

        Assert.Equal(RepeatMode.One, _player.State.Repeat);
        Assert.Equal(["repeat track@here"], _web.Commands);
    }

    [Fact]
    public async Task DJ_can_not_be_shuffled_or_repeated()
    {
        _web.Playback = OnTheWeb(Song(1), shuffle: false, context: SpotifyDj.ContextUri);
        _local.Report(LocalMediaSnapshot.None);
        await _player.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(_player.State.CanShuffle);
        Assert.False(_player.State.CanRepeat);

        await _player.SetShuffleAsync(true);
        await _player.SetRepeatAsync(RepeatMode.All);
        Assert.Empty(_web.Commands);
        Assert.False(_player.State.Shuffle);
    }

    [Fact]
    public async Task A_file_from_the_computer_plays_inside_its_playlist_by_position()
    {
        var list = new List<TrackInfo> { Song(0), LocalFile(1), Song(2), Song(3) };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = false });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Null(body.Uris);
        Assert.Null(body.Offset!.Uri);
        Assert.Equal(1, body.Offset.Position);
        Assert.Equal("Demo 1", _player.State.Title);
    }

    [Fact]
    public async Task A_file_from_the_computer_in_a_playlist_of_files_plays_by_position()
    {
        var list = new List<TrackInfo> { LocalFile(0), LocalFile(1) };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(list, 1, Playlist, "Demos"));

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal(1, body.Offset!.Position);
    }

    [Fact]
    public async Task Files_from_the_computer_are_left_out_of_song_lists_and_the_queue()
    {
        var list = new List<TrackInfo> { Song(0), LocalFile(1), Song(2), Song(3) };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(list, 0, Playlist, "Mine") { Shuffle = true });
        await _player.AddToQueueAsync(LocalFile(1));

        var uris = Assert.Single(_web.PlayBodies)!.Uris!;
        Assert.Equal(3, uris.Count);
        Assert.DoesNotContain(uris, u => u.StartsWith("spotify:local:", StringComparison.Ordinal));
        Assert.DoesNotContain(_web.Commands, c => c.StartsWith("queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_picked_while_shuffled_plays_in_its_playlist_then_the_random_order_follows()
    {
        var list = new List<TrackInfo> { Song(0), LocalFile(1), Song(2), Song(3), Song(4) };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(list, 1, Playlist, "Mine") { Shuffle = true });

        Assert.True(_player.State.Shuffle);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal(1, body.Offset!.Position);

        Playing(list[1]);
        Playing(list[2], seconds: 1);
        await WaitUntil(() => _web.PlayBodies.Count == 2);

        var next = _web.PlayBodies[1]!.Uris!;
        Assert.Equal(list[2].Uri, next[0]);
        Assert.Equal(new[] { 0, 2, 3, 4 }.Select(i => $"spotify:track:{i}").Order(), next.Order());
        Assert.True(_player.State.Shuffle);
    }

    [Fact]
    public async Task A_file_picked_outside_its_playlist_order_explains_and_plays_the_next_song()
    {
        var list = new List<TrackInfo> { Song(0), LocalFile(1), Song(2), Song(3) };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(list, 1, null, "Sorted") { Shuffle = false });

        Assert.Contains("inside their playlist", Assert.Single(_errors), StringComparison.Ordinal);
        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(["spotify:track:0", "spotify:track:2", "spotify:track:3"], body.Uris);
        Assert.Equal(1, body.Offset!.Position);
    }

    [Fact]
    public async Task A_refused_context_plays_the_same_songs_as_a_list()
    {
        var songs = Songs(6);
        await StartAsync();
        _web.FailContextPlayback = new SpotifyApiException(HttpStatusCode.BadRequest, null, "Context not playable");

        await _player.PlayAsync(new PlayRequest(songs, 2, "spotify:user:me:collection", "Liked Songs") { Shuffle = false });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal(Uris(songs), body.Uris);
        Assert.Equal(2, body.Offset!.Position);
        Assert.Empty(_errors);
        Assert.Equal("Song 2", _player.State.Title);
    }

    [Fact]
    public async Task The_source_name_is_shown_while_the_list_plays()
    {
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(Songs(4), 0, Playlist, "Late Night Drive") { Shuffle = true });

        Assert.Equal("Late Night Drive", _player.State.SourceName);
    }

    [Fact]
    public async Task A_song_listed_twice_is_not_started_by_an_offset_that_Spotify_refuses()
    {
        // Spotify answers 403 to an offset that points at the first copy of a song listed twice.
        var songs = Songs(6);
        songs[4] = songs[1] with { Position = 4 };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(songs, 1, null, "Mix") { Shuffle = false });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Uris(songs.Skip(1)), body.Uris);
        Assert.Null(body.Offset);
    }

    [Fact]
    public async Task A_song_listed_twice_in_a_playlist_starts_by_its_position()
    {
        var songs = Songs(6);
        songs[4] = songs[1] with { Position = 4 };
        await StartAsync();

        await _player.PlayAsync(new PlayRequest(songs, 4, Playlist, "Mix") { Shuffle = false });

        var body = Assert.Single(_web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Null(body.Offset?.Uri);
        Assert.Equal(4, body.Offset?.Position);
    }

    [Fact]
    public async Task After_Spotify_restarts_the_list_carries_on_from_the_same_song_and_spot()
    {
        var songs = Songs(30);
        await StartAsync();
        await _player.PlayAsync(new PlayRequest(songs, 4, null, "Mix") { Shuffle = false });
        Playing(songs[4], seconds: 30);
        await WaitUntil(() => _player.State.Title == "Song 4");
        var before = _player.State;

        // Spotify was restarted (to take a new equalizer) and has not opened its media session yet.
        _local.Report(LocalMediaSnapshot.None);
        var outcome = await _player.ResumeAsync(before, TimeSpan.FromSeconds(30), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(ResumeOutcome.Playing, outcome);
        var body = _web.PlayBodies[^1]!;
        Assert.Equal(Uris(songs), body.Uris);
        Assert.Equal(4, body.Offset?.Position);
        Assert.Equal(30_000, body.PositionMs);
    }

    [Fact]
    public async Task After_Spotify_restarts_a_song_outside_Resonates_lists_starts_again_in_its_playlist()
    {
        await StartAsync();
        var before = _player.State with { TrackUri = "spotify:track:7", ContextUri = Playlist, IsPlaying = true };

        _local.Report(LocalMediaSnapshot.None);
        var outcome = await _player.ResumeAsync(before, TimeSpan.FromSeconds(12), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(ResumeOutcome.Playing, outcome);
        var body = _web.PlayBodies[^1]!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal("spotify:track:7", body.Offset?.Uri);
        Assert.Equal(12_000, body.PositionMs);
    }

    [Theory]
    [InlineData(ControlChannel.Local)]
    [InlineData(ControlChannel.WebApi)]
    public async Task After_Spotify_restarts_DJ_is_never_started_through_the_Web_API(ControlChannel channel)
    {
        _web.Playback = OnTheWeb(Song(7), shuffle: false, context: SpotifyDj.ContextUri);
        Playing(Song(7), seconds: 12);
        await _player.StartAsync(TestContext.Current.CancellationToken);
        _player.Channel = channel;
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        var before = _player.State;
        Assert.True(SpotifyDj.IsPlaying(before));
        Assert.True(before.IsPlaying);
        Assert.NotNull(before.TrackUri);

        // Spotify has not opened its media session (or reopened another song).
        _local.Report(LocalMediaSnapshot.None);
        var outcome = await _player.ResumeAsync(before, TimeSpan.FromSeconds(12), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(ResumeOutcome.NotResumed, outcome);
        Assert.Empty(_web.PlayBodies);
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

    /// <summary>The song with this address (all test songs are numbered).</summary>
    private static TrackInfo SongBy(string uri) => Song(int.Parse(uri["spotify:track:".Length..], System.Globalization.CultureInfo.InvariantCulture));

    private static PlaybackState OnTheWeb(TrackInfo song, bool shuffle, string? context = null) => new()
    {
        Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
        IsPlaying = true,
        ProgressMs = 1000,
        ShuffleState = shuffle,
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

    /// <summary>The first list sent to Spotify.</summary>
    private List<string> Order() => _web.PlayBodies[0]!.Uris!;

    /// <summary>Spotify's media session reports this song playing.</summary>
    private void Playing(TrackInfo song, double seconds = 0.5) => Report(song, seconds, playing: true);

    /// <summary>Spotify's media session reports this song paused.</summary>
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

using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class PlayerControllerTests : IDisposable
{
    private static readonly LocalMediaSnapshot PlayingSongA = new()
    {
        HasSession = true,
        Title = "Song A",
        Artist = "Artist",
        Album = "Album",
        IsPlaying = true,
        Position = TimeSpan.FromSeconds(60),
        Duration = TimeSpan.FromSeconds(200),
        CanSeek = true,
        CanSkipNext = true,
        CanSkipPrevious = true,
    };

    private static readonly TrackInfo SongB = new(
        "spotify:track:b", "Song B", "Band", "Record", "spotify:album:r",
        TimeSpan.FromSeconds(180), "small-b", "large-b", IsExplicit: false, IsPlayable: true)
    {
        FullImageUrl = "full-b",
    };

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _local = new();
    private readonly FakeWebApi _web = new();
    private readonly PlayerController _player;
    private readonly List<string> _errors = [];

    public PlayerControllerTests()
    {
        _player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time);
        _player.ErrorOccurred += (_, message) => _errors.Add(message);
    }

    public void Dispose() => _player.Dispose();

    [Fact]
    public async Task Pause_shows_at_once_and_goes_through_the_local_channel()
    {
        await StartPlayingSongA();

        var sent = _player.PauseAsync();

        Assert.False(_player.State.IsPlaying);
        await sent;
        Assert.Equal(["pause"], _local.Commands);
        Assert.Empty(_web.Commands);
    }

    [Fact]
    public async Task A_late_report_does_not_undo_a_pause()
    {
        await StartPlayingSongA();
        await _player.PauseAsync();

        // Spotify's media session still says "playing" from before the click.
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });
        Assert.False(_player.State.IsPlaying);

        // Long after the click, Spotify's word counts again.
        _time.Advance(PlayerController.PlayStateHold);
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });
        Assert.True(_player.State.IsPlaying);
    }

    [Fact]
    public async Task Once_Spotify_confirms_a_command_its_reports_count_again()
    {
        await StartPlayingSongA();
        await _player.PauseAsync();
        _local.Report(PlayingSongA with { IsPlaying = false, PositionUpdatedAt = _time.GetUtcNow() });

        // Resumed from the keyboard's media key a moment later.
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });

        Assert.True(_player.State.IsPlaying);
    }

    [Fact]
    public async Task A_blank_media_session_keeps_the_song_and_play_goes_through_the_Web_API()
    {
        await StartPlayingSongA();
        var title = _player.State.Title;

        // Spotify's session stays but says nothing (it went blank, or nothing is loaded yet).
        _local.Report(new LocalMediaSnapshot { HasSession = true, PositionUpdatedAt = _time.GetUtcNow() });
        Assert.Equal(title, _player.State.Title);
        Assert.True(_player.State.IsConnected);

        await _player.PauseAsync();
        await _player.PlayAsync();

        // A local play would be taken and do nothing; Spotify is asked to play through the Web API.
        Assert.Equal(["pause@here", "play@here"], _web.Commands);
    }

    [Fact]
    public async Task A_blank_media_session_is_tried_while_Spotify_refuses_the_Web_API()
    {
        // The developer app's allowance is used up: Spotify says to wait three hours.
        _web.PlaybackFailure = new SpotifyApiException(HttpStatusCode.TooManyRequests, "QUOTA_EXCEEDED", "Quota exceeded", TimeSpan.FromHours(3));
        await StartPlayingSongA();
        _local.Report(new LocalMediaSnapshot { HasSession = true, PositionUpdatedAt = _time.GetUtcNow() });

        await _player.PauseAsync();
        await _player.PlayAsync();

        // Windows' media controls are the only way left to reach Spotify.
        Assert.Equal(["pause", "play"], _local.Commands);
        Assert.Empty(_web.Commands);
    }

    [Fact]
    public async Task The_queue_is_read_once_per_song_for_everything_that_shows_it()
    {
        await StartPlayingSongA();

        await _player.GetQueueAsync();
        await _player.GetQueueAsync();
        await _player.GetQueueAsync();
        Assert.Equal(1, _web.QueueReads);

        // Adding a song changes the queue: the next read asks again.
        await _player.AddToQueueAsync(SongB);
        await _player.GetQueueAsync();
        Assert.Equal(2, _web.QueueReads);
    }

    [Fact]
    public async Task Falls_back_to_the_Web_API_on_this_computer()
    {
        await StartPlayingSongA();
        _local.Accepts = false;

        await _player.PauseAsync();

        Assert.Equal(["pause@here"], _web.Commands);
    }

    [Fact]
    public async Task A_failed_command_is_rolled_back_and_explained()
    {
        await StartPlayingSongA();
        _local.Accepts = false;
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.Forbidden, "PREMIUM_REQUIRED", "Premium required");

        await _player.PauseAsync();

        Assert.True(_player.State.IsPlaying);
        Assert.Contains("Premium", Assert.Single(_errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spotify_refusing_to_pause_paused_music_counts_as_paused()
    {
        await StartPlayingSongA();
        _local.Accepts = false;
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.Forbidden, null, "Player command failed: Restriction violated");

        await _player.PauseAsync();

        Assert.False(_player.State.IsPlaying);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Seeking_ignores_positions_from_before_the_seek()
    {
        await StartPlayingSongA();

        await _player.SeekAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(["seek 120"], _local.Commands);

        _local.Report(PlayingSongA with { Position = TimeSpan.FromSeconds(61), PositionUpdatedAt = _time.GetUtcNow() });
        Assert.Equal(TimeSpan.FromSeconds(120), _player.State.PositionAt(_time.GetUtcNow()));

        _time.Advance(TimeSpan.FromMilliseconds(400));
        _local.Report(PlayingSongA with { Position = TimeSpan.FromSeconds(120.5), PositionUpdatedAt = _time.GetUtcNow() });
        Assert.Equal(TimeSpan.FromSeconds(120.5), _player.State.PositionAt(_time.GetUtcNow()));
    }

    [Fact]
    public async Task Seeking_uses_the_Web_API_when_Spotify_does_not_offer_it_locally()
    {
        _local.Report(PlayingSongA with { CanSeek = false, PositionUpdatedAt = _time.GetUtcNow() });
        await _player.StartAsync(TestContext.Current.CancellationToken);

        await _player.SeekAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(_local.Commands);
        Assert.Equal(["seek 30@here"], _web.Commands);
    }

    [Fact]
    public async Task Playing_a_song_shows_it_at_once_and_ignores_the_previous_song()
    {
        await StartPlayingSongA();

        var sent = _player.PlayTrackAsync(SongB, "spotify:playlist:p1");

        Assert.Equal("Song B", _player.State.Title);
        Assert.Equal("large-b", _player.State.ArtworkUrl);
        Assert.Equal("full-b", _player.State.FullArtworkUrl);
        Assert.Equal(TimeSpan.Zero, _player.State.PositionAt(_time.GetUtcNow()));
        await sent;

        var body = Assert.Single(_web.PlayBodies);
        Assert.Equal("spotify:playlist:p1", body!.ContextUri);
        Assert.Equal("spotify:track:b", body.Offset!.Uri);
        Assert.Equal(["play@here"], _web.Commands);

        // The old song is still reported for a moment.
        _local.Report(PlayingSongA with { Position = TimeSpan.FromSeconds(61), PositionUpdatedAt = _time.GetUtcNow() });
        Assert.Equal("Song B", _player.State.Title);
        Assert.Equal(TimeSpan.Zero, _player.State.PositionAt(_time.GetUtcNow()));

        // Then the new one arrives, with its cover.
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _local.Report(new LocalMediaSnapshot
        {
            HasSession = true,
            Title = "Song B",
            Artist = "Band",
            Album = "Record",
            Artwork = [1, 2, 3],
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(0.2),
            Duration = TimeSpan.FromSeconds(180),
            PositionUpdatedAt = _time.GetUtcNow(),
            CanSeek = true,
        });
        Assert.Equal("Song B", _player.State.Title);
        Assert.Equal("spotify:track:b", _player.State.TrackUri);
        Assert.Equal("large-b", _player.State.ArtworkUrl);
        Assert.Equal("full-b", _player.State.FullArtworkUrl);
        Assert.Equal(TimeSpan.FromSeconds(0.2), _player.State.PositionAt(_time.GetUtcNow()));
    }

    [Fact]
    public async Task A_refused_context_falls_back_to_a_list_of_songs()
    {
        await StartPlayingSongA();
        _web.FailContextPlayback = new SpotifyApiException(HttpStatusCode.BadRequest, null, "Context not playable");

        await _player.PlayTrackAsync(SongB, "spotify:user:me:collection", ["spotify:track:a", "spotify:track:b", "spotify:track:c"]);

        var body = Assert.Single(_web.PlayBodies);
        Assert.Null(body!.ContextUri);
        Assert.Equal(["spotify:track:a", "spotify:track:b", "spotify:track:c"], body.Uris);
        Assert.Equal("spotify:track:b", body.Offset!.Uri);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task A_failed_song_start_puts_back_what_was_playing()
    {
        await StartPlayingSongA();
        _web.FailNextCommand = new HttpRequestException("offline");

        await _player.PlayTrackAsync(SongB, "spotify:playlist:p1");

        Assert.Equal("Song A", _player.State.Title);
        Assert.Contains("internet", Assert.Single(_errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_next_song_from_Spotify_replaces_the_shown_one()
    {
        await StartPlayingSongA();

        _local.Report(PlayingSongA with { Title = "Song C", Artwork = [9], Position = TimeSpan.FromSeconds(1), PositionUpdatedAt = _time.GetUtcNow() });

        Assert.Equal("Song C", _player.State.Title);
        Assert.Equal([9], _player.State.ArtworkBytes!);
        Assert.Null(_player.State.ArtworkUrl);
        Assert.Equal(TimeSpan.FromSeconds(1), _player.State.PositionAt(_time.GetUtcNow()));
    }

    [Fact]
    public async Task Next_restarts_the_clock_at_once()
    {
        await StartPlayingSongA();

        await _player.NextAsync();

        Assert.Equal(TimeSpan.Zero, _player.State.PositionAt(_time.GetUtcNow()));
        Assert.Equal(["next"], _local.Commands);

        // A last report about the old song's position is ignored.
        _local.Report(PlayingSongA with { Position = TimeSpan.FromSeconds(62), PositionUpdatedAt = _time.GetUtcNow() });
        Assert.Equal(TimeSpan.Zero, _player.State.PositionAt(_time.GetUtcNow()));
    }

    [Fact]
    public async Task Volume_goes_to_the_mixer_and_keeps_the_newest_value()
    {
        _local.Volume = 0.5;
        await StartPlayingSongA();
        Assert.Equal(0.5, _player.State.Volume);

        _ = _player.SetVolumeAsync(0.2);
        _ = _player.SetVolumeAsync(0.3);
        await _player.SetVolumeAsync(0.4);

        Assert.Equal(0.4, _player.State.Volume);
        Assert.Equal("volume 0.4", _local.Commands[^1]);
        Assert.Equal(0.4, _local.Volume);
    }

    [Fact]
    public async Task Volume_uses_the_Web_API_without_a_mixer_session()
    {
        await StartPlayingSongA();
        _local.AcceptsVolume = false;

        await _player.SetVolumeAsync(0.25);

        Assert.Equal(["volume 25@here"], _web.Commands);
    }

    [Fact]
    public async Task Without_a_local_session_the_Web_API_drives_the_player()
    {
        _web.Playback = new PlaybackState
        {
            Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer", VolumePercent = 70 },
            IsPlaying = true,
            ProgressMs = 5000,
            Context = new PlaybackContext { Uri = "spotify:playlist:p1" },
            Item = new PlayableItem { Name = "Web Song", Uri = "spotify:track:w", DurationMs = 100000 },
        };

        await _player.StartAsync(TestContext.Current.CancellationToken);

        var state = _player.State;
        Assert.True(state.IsConnected);
        Assert.Equal("Web Song", state.Title);
        Assert.Equal("spotify:playlist:p1", state.ContextUri);
        Assert.Equal(TimeSpan.FromSeconds(5), state.PositionAt(_time.GetUtcNow()));
        Assert.Equal(0.7, state.Volume, 3);
    }

    [Fact]
    public async Task Web_API_only_sends_every_command_through_the_Web_API()
    {
        _local.Volume = 0.5;
        await StartPlayingSongA();
        _player.Channel = ControlChannel.WebApi;

        await _player.PauseAsync();
        await _player.NextAsync();
        await _player.SeekAsync(TimeSpan.FromSeconds(30));
        await _player.SetVolumeAsync(0.4);

        // To whichever device plays: no device named, so nothing is pulled to this computer.
        Assert.Empty(_local.Commands);
        Assert.Equal(["pause@", "next@", "seek 30@", "volume 40@"], _web.Commands);
    }

    [Fact]
    public async Task Web_API_only_shows_what_Spotify_reports_and_ignores_the_media_session()
    {
        _web.Playback = new PlaybackState
        {
            Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer", VolumePercent = 30 },
            IsPlaying = true,
            ProgressMs = 10_000,
            Item = new PlayableItem { Name = "Web Song", Uri = "spotify:track:w", DurationMs = 100_000 },
        };
        _local.Volume = 0.9;
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });
        _player.Channel = ControlChannel.WebApi;

        await _player.StartAsync(TestContext.Current.CancellationToken);
        _local.Report(PlayingSongA with { Title = "Song C", PositionUpdatedAt = _time.GetUtcNow() });

        Assert.Equal("Web Song", _player.State.Title);
        Assert.Equal(0.3, _player.State.Volume, 3);
    }

    [Fact]
    public async Task Switching_back_to_the_media_session_uses_its_latest_report()
    {
        _web.Playback = new PlaybackState
        {
            Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
            IsPlaying = true,
            Item = new PlayableItem { Name = "Web Song", Uri = "spotify:track:w", DurationMs = 100_000 },
        };
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });

        _player.Channel = ControlChannel.Local;

        await WaitUntil(() => _player.State.Title == "Song A");
    }

    [Fact]
    public async Task Web_API_only_never_listens_to_the_media_session_or_reads_the_mixer()
    {
        _local.Volume = 0.9;
        _player.Channel = ControlChannel.WebApi;

        await _player.StartAsync(TestContext.Current.CancellationToken);
        _time.Advance(PlayerController.WebOnlyPollWhilePaused);
        await _player.SetVolumeAsync(0.4);

        Assert.Equal(0, _local.StartCount);
        Assert.False(_local.IsListening);
        Assert.Equal(0, _local.VolumeReads);
        Assert.Equal(["volume 40@"], _web.Commands);
    }

    [Fact]
    public async Task Switching_to_Web_API_only_stops_listening_and_switching_back_starts_again()
    {
        await StartPlayingSongA();
        Assert.True(_local.IsListening);

        _player.Channel = ControlChannel.WebApi;
        await WaitUntil(() => !_local.IsListening);

        _player.Channel = ControlChannel.Local;
        await WaitUntil(() => _local.IsListening && _local.StartCount == 2);
    }

    [Fact]
    public async Task Web_API_only_never_starts_the_Spotify_app()
    {
        var app = new FakeSpotifyApp();
        _web.Devices.Clear();
        using var player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), app, _time);
        var errors = new List<string>();
        player.ErrorOccurred += (_, message) => errors.Add(message);
        player.Channel = ControlChannel.WebApi;
        await player.StartAsync(TestContext.Current.CancellationToken);

        await player.PlayTrackAsync(SongB, "spotify:playlist:p1");
        await player.NextAsync();

        Assert.Equal(0, app.EnsureRunningCalls);
        Assert.Equal(0, app.IsRunningReads);
        Assert.Empty(app.Events);
        Assert.DoesNotContain(_web.Commands, c => c.StartsWith("play", StringComparison.Ordinal));
        Assert.All(errors, e => Assert.StartsWith("No Spotify device is online", e, StringComparison.Ordinal));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Web_API_only_starts_music_on_the_device_that_already_plays()
    {
        _web.Devices.Add(new Device { Id = "phone", Name = "Phone", Type = "Smartphone", IsActive = true });
        _web.Playback = OnTheWeb(isPlaying: false, new Device { Id = "phone", Name = "Phone", Type = "Smartphone", IsActive = true });
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        await _player.PlayTrackAsync(SongB, "spotify:playlist:p1");

        Assert.Equal(["play@"], _web.Commands);
        Assert.Equal("Phone", _player.State.DeviceName);
    }

    [Fact]
    public async Task Web_API_only_starts_music_on_the_device_picked_last_when_nothing_plays()
    {
        _web.Devices.Add(new Device { Id = "kitchen", Name = "Kitchen", Type = "Speaker" });
        _player.Channel = ControlChannel.WebApi;
        _player.PreferredDeviceName = "Kitchen";
        await _player.StartAsync(TestContext.Current.CancellationToken);

        await _player.PlayTrackAsync(SongB, "spotify:playlist:p1");

        Assert.Equal(["play@kitchen"], _web.Commands);
    }

    [Fact]
    public async Task Web_API_only_starts_music_on_Resonates_own_player()
    {
        _web.Devices.Insert(0, new Device { Id = "phone", Name = "Phone", Type = "Smartphone" });
        var page = new FakeWebPlayerPage();
        using var own = new OwnPlayer(() => page, new FakeTokens(), time: _time);
        using var player = new PlayerController(
            _local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), time: _time, webDevices: new WebDeviceResolver(_web, "MY-PC", own));
        player.Channel = ControlChannel.WebApi;
        await player.StartAsync(TestContext.Current.CancellationToken);
        await own.StartAsync();

        var playing = player.PlayTrackAsync(SongB, "spotify:playlist:p1");
        Assert.DoesNotContain(_web.Commands, c => c.StartsWith("play", StringComparison.Ordinal));
        page.Say("""{"type":"ready","deviceId":"own"}""");
        await playing;

        Assert.Equal(["play@own"], _web.Commands);
    }

    [Fact]
    public async Task Web_API_only_starts_music_on_this_computer_when_Spotify_lists_it()
    {
        _web.Devices.Insert(0, new Device { Id = "phone", Name = "Phone", Type = "Smartphone" });
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        await _player.PlayTrackAsync(SongB, "spotify:playlist:p1");

        Assert.Equal(["play@here"], _web.Commands);
    }

    [Fact]
    public async Task Web_API_only_wakes_a_device_before_skipping_when_nothing_plays()
    {
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "No active device");

        await _player.NextAsync();

        Assert.Equal(["transfer@here", "next@here"], _web.Commands);
        Assert.False(_web.LastTransferPlay);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Web_API_only_pausing_with_nothing_playing_wakes_no_device()
    {
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);
        _web.FailNextCommand = new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "No active device");

        await _player.PauseAsync();

        Assert.Empty(_web.Commands);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Playing_on_another_device_moves_the_music_there_and_shows_it_at_once()
    {
        _web.Devices.Add(new Device { Id = "kitchen", Name = "Kitchen", Type = "Speaker" });
        _web.Playback = OnTheWeb(isPlaying: true);
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        var sent = _player.TransferToAsync("kitchen", "Kitchen");

        Assert.Equal("Kitchen", _player.State.DeviceName);
        await sent;
        Assert.Equal(["transfer@kitchen"], _web.Commands);
        Assert.True(_web.LastTransferPlay);
        Assert.Equal("Kitchen", _player.PreferredDeviceName);
    }

    [Fact]
    public async Task Web_API_only_asks_Spotify_again_shortly_after_a_command()
    {
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);
        await _player.PlayAsync();
        var readsBefore = _web.PlaybackStateReads;

        _time.Advance(PlayerController.WebOnlyConfirmDelay);

        await WaitUntil(() => _web.PlaybackStateReads > readsBefore);
    }

    [Fact]
    public async Task A_slow_answer_asked_for_before_a_pause_does_not_undo_it()
    {
        _web.Playback = OnTheWeb(isPlaying: true);
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        // A regular check leaves just before the click and comes back late.
        var slowAnswer = new TaskCompletionSource();
        _web.HoldNextPlaybackAnswer = slowAnswer.Task;
        var slowCheck = _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);

        await _player.PauseAsync();
        _web.Playback = OnTheWeb(isPlaying: false);
        await _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);
        Assert.False(_player.State.IsPlaying);

        slowAnswer.SetResult();
        await slowCheck;

        Assert.False(_player.State.IsPlaying);
    }

    [Fact]
    public async Task An_answer_asked_for_before_a_pause_is_ignored_even_after_the_pause_settles()
    {
        _web.Playback = OnTheWeb(isPlaying: true);
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        var slowAnswer = new TaskCompletionSource();
        _web.HoldNextPlaybackAnswer = slowAnswer.Task;
        var slowCheck = _player.RefreshFromWebApiAsync(TestContext.Current.CancellationToken);

        await _player.PauseAsync();

        // Spotify can not be reached for a while, so nothing confirms the pause.
        _web.PlaybackFailure = new HttpRequestException("offline");
        var readsBefore = _web.PlaybackStateReads;
        _time.Advance(PlayerController.PlayStateHold + TimeSpan.FromSeconds(1));
        await WaitUntil(() => _web.PlaybackStateReads > readsBefore);

        slowAnswer.SetResult();
        await slowCheck;

        Assert.False(_player.State.IsPlaying);
    }

    [Fact]
    public async Task Checking_Spotify_keeps_going_after_an_unreadable_answer()
    {
        // For example a Wi-Fi sign-in page answering instead of Spotify.
        _web.PlaybackFailure = new System.Text.Json.JsonException("Not JSON.");
        await _player.StartAsync(TestContext.Current.CancellationToken);

        _web.PlaybackFailure = null;
        _web.Playback = OnTheWeb(isPlaying: true);

        // Nothing is known to play, so the next check comes after the slow interval.
        _time.Advance(PlayerController.PollWhileNothingPlays);

        await WaitUntil(() => _player.State.Title == "Web Song");
    }

    [Fact]
    public async Task Web_API_only_commands_Resonates_own_player_directly_while_it_plays()
    {
        var own = new FakeDirectPlayer("own-device");
        using var player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time, direct: own);
        _web.Playback = OnTheWeb(isPlaying: true, new Device { Id = "own-device", Name = "Resonate", Type = "Computer" });
        player.Channel = ControlChannel.WebApi;
        await player.StartAsync(TestContext.Current.CancellationToken);

        await player.PauseAsync();
        await player.PlayAsync();
        await player.NextAsync();
        await player.PreviousAsync();
        await player.SeekAsync(TimeSpan.FromSeconds(30));
        await player.SetVolumeAsync(0.4);

        // Straight to the player on this PC; nothing goes through Spotify's servers.
        Assert.Equal(["pause", "resume", "next", "previous", "seek 30000", "volume 0.4"], own.Commands);
        Assert.Empty(_web.Commands);
    }

    [Fact]
    public async Task Web_API_only_commands_another_device_through_the_Web_API()
    {
        var own = new FakeDirectPlayer("own-device");
        using var player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time, direct: own);
        _web.Playback = OnTheWeb(isPlaying: true, new Device { Id = "phone", Name = "Phone", Type = "Smartphone" });
        player.Channel = ControlChannel.WebApi;
        await player.StartAsync(TestContext.Current.CancellationToken);

        await player.PauseAsync();

        Assert.Empty(own.Commands);
        Assert.Equal(["pause@"], _web.Commands);
    }

    [Fact]
    public async Task What_the_own_player_reports_shows_at_once()
    {
        _web.Playback = OnTheWeb(isPlaying: true, new Device { Id = "own-device", Name = "Resonate", Type = "Computer" });
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        _player.ApplyOwnPlayerState(new PlaybackState
        {
            Device = new Device { Id = "own-device", Name = "Resonate", Type = "Computer", IsActive = true },
            IsPlaying = true,
            ProgressMs = 0,
            Item = new PlayableItem { Name = "Next Song", Uri = "spotify:track:n", DurationMs = 120_000 },
        });

        Assert.Equal("Next Song", _player.State.Title);
        Assert.Equal(TimeSpan.FromMinutes(2), _player.State.Duration);
    }

    [Fact]
    public async Task A_paused_own_player_does_not_cover_another_device_that_plays()
    {
        _web.Playback = OnTheWeb(isPlaying: true, new Device { Id = "phone", Name = "Phone", Type = "Smartphone" });
        _player.Channel = ControlChannel.WebApi;
        await _player.StartAsync(TestContext.Current.CancellationToken);

        _player.ApplyOwnPlayerState(new PlaybackState
        {
            Device = new Device { Id = "own-device", Name = "Resonate", Type = "Computer", IsActive = true },
            IsPlaying = false,
            Item = new PlayableItem { Name = "Old Song", Uri = "spotify:track:o", DurationMs = 120_000 },
        });

        Assert.Equal("Web Song", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
    }

    [Fact]
    public void A_window_of_at_most_100_songs_contains_the_chosen_one()
    {
        var uris = Enumerable.Range(0, 250).Select(i => $"spotify:track:{i}").ToList();

        var window = PlayerController.WindowAround(uris, "spotify:track:200");

        Assert.Equal(100, window.Count);
        Assert.Contains("spotify:track:200", window);
    }

    private static PlaybackState OnTheWeb(bool isPlaying, Device? device = null) => new()
    {
        Device = device ?? new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
        IsPlaying = isPlaying,
        ProgressMs = 10_000,
        Item = new PlayableItem { Name = "Web Song", Uri = "spotify:track:w", DurationMs = 100_000 },
    };

    /// <summary>Stands in for Resonate's own player, taking commands directly.</summary>
    private sealed class FakeDirectPlayer(string deviceId) : IDirectPlayer
    {
        public List<string> Commands { get; } = [];

        public string? DeviceId => deviceId;

        public bool TryControl(string action, double value = 0)
        {
            Commands.Add(action is "seek" or "volume" ? $"{action} {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : action);
            return true;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private async Task StartPlayingSongA()
    {
        _local.Report(PlayingSongA with { PositionUpdatedAt = _time.GetUtcNow() });
        await _player.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Song A", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
    }
}

public class LocalDeviceResolverTests
{
    [Fact]
    public void Picks_the_computer_named_like_this_one()
    {
        var picked = LocalDeviceResolver.Pick(
            [
                new Device { Id = "phone", Name = "Phone", Type = "Smartphone", IsActive = true },
                new Device { Id = "other", Name = "LAPTOP", Type = "Computer" },
                new Device { Id = "here", Name = "my-pc", Type = "Computer" },
            ],
            "MY-PC");

        Assert.Equal("here", picked?.Id);
    }

    [Fact]
    public void A_single_computer_is_this_one()
    {
        var picked = LocalDeviceResolver.Pick(
            [
                new Device { Id = "phone", Name = "Phone", Type = "Smartphone" },
                new Device { Id = "pc", Name = "Renamed in Spotify", Type = "Computer" },
            ],
            "MY-PC");

        Assert.Equal("pc", picked?.Id);
    }

    [Fact]
    public void Several_unknown_computers_are_not_guessed()
    {
        var picked = LocalDeviceResolver.Pick(
            [
                new Device { Id = "a", Name = "Office", Type = "Computer" },
                new Device { Id = "b", Name = "Living room", Type = "Computer" },
            ],
            "MY-PC");

        Assert.Null(picked);
    }

    [Fact]
    public void A_long_computer_name_matches_its_15_character_NetBIOS_name() =>
        Assert.True(LocalDeviceResolver.NameMatches("DESKTOP-VERYLONGNAME", "DESKTOP-VERYLON"));
}

public class WebDeviceResolverTests
{
    private static readonly Device Phone = new() { Id = "phone", Name = "Phone", Type = "Smartphone" };
    private static readonly Device Kitchen = new() { Id = "kitchen", Name = "Kitchen", Type = "Speaker" };
    private static readonly Device ThisComputer = new() { Id = "here", Name = "MY-PC", Type = "Computer" };

    [Fact]
    public void The_device_that_plays_comes_first() =>
        Assert.Equal("phone", WebDeviceResolver.Pick([ThisComputer, Kitchen, WithActive(Phone)], "Kitchen", "MY-PC")?.Id);

    [Fact]
    public void Then_the_device_picked_last() =>
        Assert.Equal("kitchen", WebDeviceResolver.Pick([ThisComputer, Phone, Kitchen], "kitchen", "MY-PC")?.Id);

    [Fact]
    public void Then_this_computer_when_Spotify_lists_it() =>
        Assert.Equal("here", WebDeviceResolver.Pick([Phone, ThisComputer, Kitchen], null, "MY-PC")?.Id);

    [Fact]
    public void Then_the_only_device_there_is() =>
        Assert.Equal("kitchen", WebDeviceResolver.Pick([Kitchen], null, "MY-PC")?.Id);

    [Fact]
    public void Several_unknown_devices_are_not_guessed() =>
        Assert.Null(WebDeviceResolver.Pick([Phone, Kitchen], null, "MY-PC"));

    [Fact]
    public void Restricted_devices_and_ones_without_an_ID_are_skipped() =>
        Assert.Null(WebDeviceResolver.Pick(
            [new Device { Id = "tv", Name = "TV", Type = "TV", IsRestricted = true, IsActive = true }, new Device { Name = "Ghost", Type = "Speaker" }],
            null,
            "MY-PC"));

    private static Device WithActive(Device device) => new() { Id = device.Id, Name = device.Name, Type = device.Type, IsActive = true };
}

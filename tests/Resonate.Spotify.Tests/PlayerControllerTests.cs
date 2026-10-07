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
        TimeSpan.FromSeconds(180), "small-b", "large-b", IsExplicit: false, IsPlayable: true);

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

        Assert.Empty(_local.Commands);
        Assert.Equal(["pause@here", "next@here", "seek 30@here", "volume 40@here"], _web.Commands);
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

        Assert.Equal("Song A", _player.State.Title);
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
        _time.Advance(PlayerController.WebPollInterval);

        await WaitUntil(() => _player.State.Title == "Web Song");
    }

    [Fact]
    public void A_window_of_at_most_100_songs_contains_the_chosen_one()
    {
        var uris = Enumerable.Range(0, 250).Select(i => $"spotify:track:{i}").ToList();

        var window = PlayerController.WindowAround(uris, "spotify:track:200");

        Assert.Equal(100, window.Count);
        Assert.Contains("spotify:track:200", window);
    }

    private static PlaybackState OnTheWeb(bool isPlaying) => new()
    {
        Device = new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
        IsPlaying = isPlaying,
        ProgressMs = 10_000,
        Item = new PlayableItem { Name = "Web Song", Uri = "spotify:track:w", DurationMs = 100_000 },
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

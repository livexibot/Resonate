using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class LocalPlayerTests : IDisposable
{
    private static readonly TrackInfo SongA = LocalPlayQueueTests.Song("a");
    private static readonly TrackInfo SongB = LocalPlayQueueTests.Song("b");
    private static readonly TrackInfo SongC = LocalPlayQueueTests.Song("c");
    private static readonly TrackInfo[] Album = [SongA, SongB, SongC];

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeAudioEngine _engine = new();
    private readonly FakeSystemControls _controls = new();
    private readonly Dictionary<string, byte[]> _covers = [];
    private readonly LocalPlayer _player;
    private readonly List<string> _errors = [];

    public LocalPlayerTests()
    {
        _player = new LocalPlayer(_engine, _controls, path => _covers.GetValueOrDefault(path), _time, random: _ => 0);
        _player.ErrorOccurred += (_, message) => _errors.Add(message);
    }

    public void Dispose() => _player.Dispose();

    [Fact]
    public async Task Playing_a_list_shows_the_song_at_once_and_opens_it()
    {
        var sent = _player.PlayAsync(new PlayRequest(Album, 1, null, "Local Files"));

        Assert.Equal("b", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
        Assert.Equal(PlaybackSource.LocalFiles, _player.State.Source);
        Assert.Equal("Local Files", _player.State.SourceName);

        await sent;
        Assert.Equal([SongB.FilePath!], _engine.Opened);
        Assert.Equal(SongC.FilePath, _engine.Next);
        Assert.Equal(["c"], Titles(_player.Upcoming));
    }

    [Fact]
    public async Task Pause_and_play_show_at_once_and_reach_the_engine()
    {
        await Play(SongA);

        var paused = _player.PauseAsync();
        Assert.False(_player.State.IsPlaying);
        await paused;

        var playing = _player.TogglePlayPauseAsync();
        Assert.True(_player.State.IsPlaying);
        await playing;

        Assert.Equal(["pause", "play"], _engine.Commands.Skip(1));
    }

    [Fact]
    public async Task Previous_restarts_a_song_that_has_played_for_a_while()
    {
        await Play(SongB);
        _time.Advance(TimeSpan.FromSeconds(5));

        await _player.PreviousAsync();
        Assert.Equal("b", _player.State.Title);
        Assert.Equal(TimeSpan.Zero, _player.State.Position);
        Assert.Contains("seek 0", _engine.Commands);

        await _player.PreviousAsync();
        Assert.Equal("a", _player.State.Title);
        Assert.Equal(SongA.FilePath, _engine.Opened[^1]);
    }

    [Fact]
    public async Task Next_after_the_last_song_goes_back_to_the_first_paused()
    {
        await Play(SongC);

        await _player.NextAsync();

        Assert.Equal("a", _player.State.Title);
        Assert.False(_player.State.IsPlaying);
        Assert.Equal($"open {SongA.FilePath} 0 paused", _engine.Commands[^1]);
    }

    [Fact]
    public async Task A_song_that_ends_hands_over_to_the_one_the_engine_started_without_a_gap()
    {
        await Play(SongA);

        _engine.End(SongA.FilePath!, SongB.FilePath);
        await _player.WhenIdleAsync();

        Assert.Equal("b", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
        Assert.Equal([SongA.FilePath!], _engine.Opened);
        Assert.Equal(SongC.FilePath, _engine.Next);
    }

    [Fact]
    public async Task A_song_that_ends_without_a_prepared_next_opens_the_next()
    {
        await Play(SongA);

        _engine.End(SongA.FilePath!, null);
        await _player.WhenIdleAsync();

        Assert.Equal("b", _player.State.Title);
        Assert.Equal([SongA.FilePath!, SongB.FilePath!], _engine.Opened);
    }

    [Fact]
    public async Task The_last_song_ending_stops_at_the_start_of_the_list()
    {
        await Play(SongC);

        _engine.End(SongC.FilePath!, null);
        await _player.WhenIdleAsync();

        Assert.Equal("a", _player.State.Title);
        Assert.False(_player.State.IsPlaying);
    }

    [Fact]
    public async Task An_end_reported_after_the_user_moved_on_is_ignored()
    {
        await Play(SongA);
        await _player.NextAsync();

        _engine.End(SongA.FilePath!, null);
        await _player.WhenIdleAsync();

        Assert.Equal("b", _player.State.Title);
        Assert.Equal([SongA.FilePath!, SongB.FilePath!], _engine.Opened);
    }

    [Fact]
    public async Task Repeat_one_loops_inside_the_engine()
    {
        await Play(SongA);

        await _player.SetRepeatAsync(RepeatMode.One);

        Assert.True(_engine.Looping);
        Assert.Null(_engine.Next);
        Assert.Equal(RepeatMode.One, _player.State.Repeat);

        await _player.SetRepeatAsync(RepeatMode.All);
        Assert.False(_engine.Looping);
        Assert.Equal(SongB.FilePath, _engine.Next);
    }

    [Fact]
    public async Task Shuffle_keeps_the_song_playing_and_changes_what_follows()
    {
        await Play(SongA);

        await _player.SetShuffleAsync(true);

        Assert.True(_player.State.Shuffle);
        Assert.Equal("a", _player.State.Title);
        Assert.Equal(["c", "b"], Titles(_player.Upcoming));
        Assert.Equal(SongC.FilePath, _engine.Next);
        Assert.Single(_engine.Opened);
    }

    [Fact]
    public async Task Added_songs_play_next()
    {
        await Play(SongA);
        var extra = LocalPlayQueueTests.Song("x", album: "Other");

        await _player.AddToQueueAsync(extra);

        Assert.Equal(["x", "b", "c"], Titles(_player.Upcoming));
        Assert.Equal(extra.FilePath, _engine.Next);
    }

    [Fact]
    public async Task Adding_to_an_empty_player_gets_the_song_ready_paused()
    {
        await _player.AddToQueueAsync(SongB);

        Assert.Equal("b", _player.State.Title);
        Assert.False(_player.State.IsPlaying);
        Assert.Equal([$"open {SongB.FilePath} 0 paused"], _engine.Commands);
    }

    [Fact]
    public async Task Files_that_can_not_play_are_skipped_and_reported_once()
    {
        _engine.Broken.Add(SongA.FilePath!);
        _engine.Broken.Add(SongB.FilePath!);

        await Play(SongA);
        await _player.WhenIdleAsync();

        Assert.Equal("c", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
        Assert.Single(_errors);
    }

    [Fact]
    public async Task When_nothing_can_play_it_stops()
    {
        foreach (var song in Album)
        {
            _engine.Broken.Add(song.FilePath!);
        }

        await Play(SongA);
        await _player.WhenIdleAsync();

        Assert.False(_player.State.IsPlaying);
        Assert.Equal(3, _engine.Opened.Count);
        Assert.Single(_errors);
    }

    [Fact]
    public async Task Without_a_sound_device_it_pauses_instead_of_skipping_every_song()
    {
        _engine.NoDevice = true;

        await Play(SongA);

        Assert.Equal("a", _player.State.Title);
        Assert.False(_player.State.IsPlaying);
        Assert.Single(_engine.Opened);
        Assert.Equal(["No sound device."], _errors);
    }

    [Fact]
    public async Task Only_the_newest_position_is_sent_while_dragging()
    {
        await Play(SongA);
        _engine.Hold = new TaskCompletionSource();
        var paused = _player.PauseAsync();

        _ = _player.SeekAsync(TimeSpan.FromSeconds(10));
        _ = _player.SeekAsync(TimeSpan.FromSeconds(20));
        var last = _player.SeekAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), _player.State.Position);

        _engine.Hold.SetResult();
        await paused;
        await last;
        Assert.Equal(["seek 30"], _engine.Commands.Where(c => c.StartsWith("seek", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_seek_past_the_end_stops_at_the_end()
    {
        await Play(SongA);

        await _player.SeekAsync(TimeSpan.FromHours(1));

        Assert.Equal(_engine.Length, _player.State.Position);
    }

    [Fact]
    public async Task The_shown_position_follows_the_engine_when_they_drift_apart()
    {
        await Play(SongA);
        _engine.Position = TimeSpan.FromSeconds(42);

        _time.Advance(LocalPlayer.ClockInterval);
        await _player.WhenIdleAsync();

        Assert.Equal(TimeSpan.FromSeconds(42), _player.State.PositionAt(_time.GetUtcNow()));
    }

    [Fact]
    public async Task Volume_shows_at_once_and_the_newest_reaches_the_engine()
    {
        await Play(SongA);

        _ = _player.SetVolumeAsync(0.2);
        var last = _player.SetVolumeAsync(0.5);
        Assert.Equal(0.5, _player.State.Volume);

        await last;
        Assert.Equal(0.5, _engine.Volume);
        Assert.Equal("volume 0.5", _engine.Commands[^1]);
    }

    [Fact]
    public async Task The_equalizer_reaches_the_engine()
    {
        var settings = new Audio.EqualizerSettings { Enabled = true, GainsDb = [3, 0, 0, 0, 0, -2] };

        _player.SetEqualizer(settings);
        await _player.WhenIdleAsync();

        Assert.Same(settings, _engine.Equalizer);
    }

    [Fact]
    public async Task A_lost_sound_device_pauses_and_says_so()
    {
        await Play(SongA);

        _engine.Fail("The sound device is gone.");
        await _player.WhenIdleAsync();

        Assert.False(_player.State.IsPlaying);
        Assert.Equal(["The sound device is gone."], _errors);

        // Play opens the file again, where it stopped.
        await _player.PlayAsync();
        Assert.StartsWith($"open {SongA.FilePath}", _engine.Commands[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cover_is_read_from_the_file()
    {
        _covers[SongA.FilePath!] = [1, 2, 3];

        await Play(SongA);

        await Eventually(() => _player.State.ArtworkBytes is not null);
        Assert.Equal([1, 2, 3], _player.State.ArtworkBytes);
    }

    [Fact]
    public async Task Media_controls_show_the_song_only_while_enabled_and_their_buttons_work()
    {
        await Play(SongA);
        await _player.WhenIdleAsync();
        Assert.All(_controls.Shown, Assert.Null);

        _player.SystemControlsEnabled = true;
        await _player.WhenIdleAsync();
        Assert.Equal("a", _controls.Shown[^1]?.Title);

        _controls.Press(LocalControlButton.Pause);
        await _player.WhenIdleAsync();
        Assert.False(_player.State.IsPlaying);
        Assert.False(_controls.Shown[^1]?.IsPlaying);

        _controls.Press(LocalControlButton.Next);
        _controls.Shuffle(true);
        _controls.Repeat(RepeatMode.All);
        await _player.WhenIdleAsync();
        Assert.Equal("b", _player.State.Title);
        Assert.True(_player.State.Shuffle);
        Assert.Equal(RepeatMode.All, _player.State.Repeat);

        _player.SystemControlsEnabled = false;
        await _player.WhenIdleAsync();
        Assert.Null(_controls.Shown[^1]);
    }

    [Fact]
    public void Dispose_releases_the_engine()
    {
        _player.Dispose();

        Assert.True(_engine.Disposed);
    }

    private async Task Play(TrackInfo song)
    {
        await _player.PlayAsync(new PlayRequest(Album, Array.IndexOf(Album, song), null, "Local Files") { Shuffle = false });
        await _player.WhenIdleAsync();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private static string[] Titles(IEnumerable<TrackInfo> tracks) => [.. tracks.Select(t => t.Title)];
}

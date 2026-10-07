using System.Text;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;

namespace Resonate.Spotify.Tests;

public sealed class SpotifyPrefsFileTests : IDisposable
{
    private const string Prefs =
        "language=\"en\"\n" +
        "audio.play_bitrate_enumeration=5\n" +
        "ui.track_notifications_enabled=false\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "resonate-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Finds_the_account_used_last_and_never_the_global_file()
    {
        var installer = Path.Combine(_root, "Roaming", "Spotify");
        var store = Path.Combine(_root, "Packages", "LocalState", "Spotify");
        var old = Create(Path.Combine(installer, "Users", "old-user", "prefs"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = Create(Path.Combine(installer, "Users", "recent-user", "prefs"), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = Create(Path.Combine(store, "Users", "store-user", "prefs"), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        // The global file (sign-in data) and folders that are not accounts are never picked, however new.
        var newest = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        Create(Path.Combine(installer, "prefs"), newest);
        Create(Path.Combine(installer, "Users", "notes", "prefs"), newest);

        var missing = Path.Combine(_root, "Nowhere", "Spotify");
        Assert.Equal([recent, middle, old], SpotifyPrefsFile.FindAll([installer, store, missing]));
        Assert.Equal(recent, SpotifyPrefsFile.FindNewest([installer, store, missing]));
        Assert.Null(SpotifyPrefsFile.FindNewest([missing]));
    }

    [Fact]
    public void Writing_backs_up_once_and_leaves_no_partial_file()
    {
        var path = Create(Path.Combine(_root, "Users", "me-user", "prefs"), DateTime.UtcNow);
        var rock = EqualizerSettings.Flat.WithPreset(EqualizerPresets.All.Single(p => p.Name == "Rock")) with { Enabled = true };

        Assert.True(SpotifyPrefsFile.WriteEqualizer(path, rock));
        Assert.True(SpotifyPrefsFile.WriteEqualizer(path, rock with { Enabled = false }));

        // The backup is the file as it was before Resonate's first change.
        Assert.Equal(Prefs, File.ReadAllText(path + SpotifyPrefsFile.BackupSuffix));
        Assert.Equal(rock with { Enabled = false }, SpotifyPrefsFile.TryRead(path)!.Equalizer);
        Assert.True(SpotifyPrefsFile.TryRead(path)!.IsLossless);
        Assert.StartsWith(Prefs, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(
            ["prefs", "prefs" + SpotifyPrefsFile.BackupSuffix],
            Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // The same settings again write nothing.
        var written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddDays(-1));
        Assert.False(SpotifyPrefsFile.WriteEqualizer(path, rock with { Enabled = false }));
        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_byte_order_mark_is_kept_and_a_file_that_is_not_text_is_left_alone()
    {
        var withBom = Path.Combine(_root, "bom", "prefs");
        Directory.CreateDirectory(Path.GetDirectoryName(withBom)!);
        File.WriteAllBytes(withBom, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Prefs)]);

        SpotifyPrefsFile.WriteEqualizer(withBom, EqualizerSettings.Flat with { Enabled = true });

        var bytes = File.ReadAllBytes(withBom);
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'l'], bytes[..4]);
        Assert.True(SpotifyPrefsFile.TryRead(withBom)!.Equalizer.Enabled);

        var broken = Path.Combine(_root, "broken", "prefs");
        Directory.CreateDirectory(Path.GetDirectoryName(broken)!);
        byte[] garbage = [(byte)'a', (byte)'=', 0xFF, 0xFE, (byte)'\n'];
        File.WriteAllBytes(broken, garbage);

        Assert.Null(SpotifyPrefsFile.TryRead(broken));
        Assert.Throws<DecoderFallbackException>(() => SpotifyPrefsFile.WriteEqualizer(broken, EqualizerSettings.Flat));
        Assert.Equal(garbage, File.ReadAllBytes(broken));
    }

    private static string Create(string path, DateTime writtenUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Prefs);
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }
}

public sealed class SpotifyEqualizerSyncTests : IDisposable
{
    private const string Prefs = "audio.play_bitrate_enumeration=4\naudio.equalizer_v2=false\n";

    private static readonly LocalMediaSnapshot SongA = new()
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

    private static readonly EqualizerSettings Rock =
        EqualizerSettings.Flat.WithPreset(EqualizerPresets.All.Single(p => p.Name == "Rock")) with { Enabled = true };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "resonate-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _prefs;
    private readonly FakeSpotifyApp _app = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _local = new();
    private readonly FakeWebApi _web = new();
    private readonly FakeLocalPlayer _files = new();
    private readonly PlayerController _player;
    private readonly PlayerRouter _router;
    private readonly SpotifyEqualizerSync _sync;
    private int _pendingChanges;

    public SpotifyEqualizerSyncTests()
    {
        _prefs = Path.Combine(_root, "Spotify", "Users", "me-user", "prefs");
        Directory.CreateDirectory(Path.GetDirectoryName(_prefs)!);
        File.WriteAllText(_prefs, Prefs);

        _player = new PlayerController(_local, _local, _web, new LocalDeviceResolver(_web, "MY-PC", _time), _app, _time);
        _router = new PlayerRouter(_player, _files);
        _sync = new SpotifyEqualizerSync(
            [Path.Combine(_root, "Spotify")],
            _app,
            _app,
            _player,
            time: _time,
            resumeAllowed: () => _router.ActiveSource == PlaybackSource.Spotify);
        _sync.PendingChanged += (_, _) => _pendingChanges++;
        _app.BeforeStart = _sync.ApplyPendingBeforeStart;
    }

    public void Dispose()
    {
        _router.Dispose();
        _player.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(false, false, false, EqualizerWritePlan.NotFound)]
    [InlineData(false, false, true, EqualizerWritePlan.NotFound)]
    [InlineData(true, true, true, EqualizerWritePlan.NothingToDo)]
    [InlineData(true, false, true, EqualizerWritePlan.KeepPending)]
    [InlineData(true, false, false, EqualizerWritePlan.WriteNow)]
    public void Writes_only_while_Spotify_is_closed(bool found, bool alreadyThere, bool running, EqualizerWritePlan expected) =>
        Assert.Equal(expected, SpotifyEqualizerSync.Plan(found, alreadyThere, running));

    [Fact]
    public void A_change_is_written_at_once_while_Spotify_is_closed()
    {
        Assert.Equal(EqualizerApplyResult.Applied, _sync.Apply(Rock));

        Assert.Equal(Rock, SpotifyPrefsFile.TryRead(_prefs)!.Equalizer);
        Assert.Null(_sync.Pending);
        Assert.Equal(0, _pendingChanges);
    }

    [Fact]
    public void A_change_waits_while_Spotify_runs_and_is_written_before_Resonate_starts_it()
    {
        _app.IsRunning = true;

        Assert.Equal(EqualizerApplyResult.Pending, _sync.Apply(Rock));
        Assert.Equal(Rock, _sync.Pending);
        Assert.False(SpotifyPrefsFile.TryRead(_prefs)!.Equalizer.Enabled);

        // Spotify quits; Resonate starts it again later.
        _app.IsRunning = false;
        _app.EnsureRunningAsync(CancellationToken.None);

        Assert.Equal(Rock, SpotifyPrefsFile.TryRead(_prefs)!.Equalizer);
        Assert.Null(_sync.Pending);
        Assert.Equal(2, _pendingChanges);
    }

    [Fact]
    public void Going_back_to_what_Spotify_has_needs_no_restart()
    {
        _app.IsRunning = true;
        _sync.Apply(Rock);

        Assert.Equal(EqualizerApplyResult.Applied, _sync.Apply(EqualizerSettings.Flat));
        Assert.Null(_sync.Pending);
    }

    [Fact]
    public void Refresh_reads_Spotifys_settings_and_writes_a_change_once_Spotify_has_closed()
    {
        File.WriteAllText(_prefs, SpotifyPrefs.WriteEqualizer(Prefs, Rock));
        var status = _sync.Refresh();
        Assert.Equal(Rock, status.Spotify!.Equalizer);
        Assert.False(status.Spotify.IsLossless);
        Assert.Null(status.Pending);

        _app.IsRunning = true;
        _sync.Apply(EqualizerSettings.Flat);
        Assert.Equal(Rock, _sync.Refresh().Spotify!.Equalizer);

        _app.IsRunning = false;
        status = _sync.Refresh();
        Assert.Equal(EqualizerSettings.Flat, status.Spotify!.Equalizer);
        Assert.Null(status.Pending);
    }

    [Fact]
    public void Without_Spotifys_settings_the_change_is_kept_for_later()
    {
        var sync = new SpotifyEqualizerSync([Path.Combine(_root, "Missing")], _app, restarter: null, player: null);

        Assert.Equal(EqualizerApplyResult.SpotifyNotFound, sync.Apply(Rock));
        Assert.Equal(Rock, sync.Pending);
        Assert.Null(sync.Refresh().Spotify);
        Assert.False(sync.CanRestart);
        Assert.Null(sync.Watch(() => { }));
        Assert.Equal(SpotifyEqualizerSync.NotFoundText, SpotifyEqualizerSync.Describe(EqualizerApplyResult.SpotifyNotFound));
    }

    [Fact]
    public async Task Restarting_applies_the_change_and_plays_on_from_the_same_spot()
    {
        await StartPlayingSongA();
        _sync.Apply(Rock);
        var writtenWhileClosed = false;
        _app.Started = () =>
        {
            writtenWhileClosed = SpotifyPrefsFile.TryRead(_prefs)!.Equalizer == Rock;
            _local.Report(LocalMediaSnapshot.None);
            _local.Report(SongA with { IsPlaying = false, Position = TimeSpan.FromSeconds(58), PositionUpdatedAt = _time.GetUtcNow() });
        };

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(new SpotifyRestartOutcome(SpotifyRestartStatus.Restarted, true, ResumeOutcome.Playing, outcome.Before, TimeSpan.FromSeconds(60)), outcome);
        Assert.True(writtenWhileClosed);
        Assert.Equal(["closed", "started"], _app.Events);
        Assert.Equal(["pause", "seek 60", "play"], _local.Commands);
        Assert.Null(_sync.Pending);
        Assert.Equal("Applied to Spotify. Your song carried on where it was.", SpotifyEqualizerSync.Describe(outcome));
    }

    [Fact]
    public async Task A_song_Spotify_does_not_reopen_is_started_again_in_its_playlist_at_the_same_spot()
    {
        await _player.StartAsync(TestContext.Current.CancellationToken);
        await _player.PlayTrackAsync(SongB, "spotify:playlist:p");
        await _player.SeekAsync(TimeSpan.FromSeconds(30));
        _app.IsRunning = true;
        _sync.Apply(Rock);
        _app.Started = () => _local.Report(SongA with { IsPlaying = false });

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(ResumeOutcome.Playing, outcome.Resume);
        var body = _web.PlayBodies[^1]!;
        Assert.Equal("spotify:playlist:p", body.ContextUri);
        Assert.Equal("spotify:track:b", body.Offset?.Uri);
        Assert.Equal(30_000, body.PositionMs);
        Assert.Equal("Song B", _player.State.Title);
        Assert.True(_player.State.IsPlaying);
    }

    [Fact]
    public async Task A_paused_song_that_does_not_come_back_is_named_so_the_user_can_find_it()
    {
        await StartPlayingSongA();
        await _player.PauseAsync();
        _sync.Apply(Rock);
        _app.Started = () => _local.Report(SongA with { Title = "Another song", IsPlaying = false });

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(ResumeOutcome.NotResumed, outcome.Resume);
        Assert.Equal(["pause"], _local.Commands);
        Assert.Empty(_web.PlayBodies);
        Assert.Equal(
            "Applied to Spotify. Spotify may not have reopened “Song A”: if not, play it again and move to 1:00.",
            SpotifyEqualizerSync.Describe(outcome));
    }

    [Fact]
    public async Task A_paused_song_Spotify_reopens_stays_paused_at_the_same_spot()
    {
        await StartPlayingSongA();
        await _player.PauseAsync();
        _sync.Apply(Rock);
        _app.Started = () => _local.Report(SongA with { IsPlaying = false, Position = TimeSpan.Zero, PositionUpdatedAt = _time.GetUtcNow() });

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(ResumeOutcome.Paused, outcome.Resume);
        Assert.Equal(["pause", "seek 60"], _local.Commands);
        Assert.False(_player.State.IsPlaying);
    }

    [Fact]
    public async Task A_song_the_user_picks_during_the_restart_is_not_replaced()
    {
        await StartPlayingSongA();
        _sync.Apply(Rock);
        Task? picked = null;
        _app.Started = () =>
        {
            _local.Report(SongA with { IsPlaying = false, Position = TimeSpan.Zero, PositionUpdatedAt = _time.GetUtcNow() });
            picked = _router.PlayAsync(new PlayRequest([SongB], 0, null, "Search"));
        };

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);
        await picked!;

        Assert.Equal(ResumeOutcome.NothingToResume, outcome.Resume);
        Assert.Equal(["pause"], _local.Commands);
        Assert.Equal(["spotify:track:b"], Assert.Single(_web.PlayBodies)!.Uris);
        Assert.Equal("Song B", _player.State.Title);
        Assert.Equal("Applied to Spotify.", SpotifyEqualizerSync.Describe(outcome));
    }

    [Fact]
    public async Task Spotify_is_not_put_back_once_the_users_own_files_play()
    {
        await StartPlayingSongA();
        _sync.Apply(Rock);
        var file = SongB with { Uri = null, FilePath = Path.Combine(_root, "Music", "song.flac") };
        _app.Started = () =>
        {
            _ = _router.PlayAsync(new PlayRequest([file], 0, null, "Local Files"));
            _local.Report(SongA with { IsPlaying = false, Position = TimeSpan.Zero, PositionUpdatedAt = _time.GetUtcNow() });
        };

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(ResumeOutcome.NothingToResume, outcome.Resume);
        Assert.Equal(["pause"], _local.Commands);
        Assert.Empty(_web.PlayBodies);
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.True(_files.State.IsPlaying);
        Assert.Empty(_files.Commands);
    }

    [Fact]
    public async Task When_Spotify_will_not_close_it_plays_on_and_the_change_still_waits()
    {
        await StartPlayingSongA();
        _sync.Apply(Rock);
        _app.Closes = false;

        var outcome = await _sync.RestartSpotifyAsync(CancellationToken.None);

        Assert.Equal(SpotifyRestartStatus.CouldNotClose, outcome.Restart);
        Assert.False(outcome.Applied);
        Assert.Equal(["pause", "play"], _local.Commands);
        Assert.Equal(Rock, _sync.Pending);
        Assert.False(SpotifyPrefsFile.TryRead(_prefs)!.Equalizer.Enabled);
        Assert.StartsWith("Spotify could not be closed", SpotifyEqualizerSync.Describe(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_made_in_Spotify_is_noticed()
    {
        using var changed = new ManualResetEventSlim();
        using var watch = _sync.Watch(changed.Set);
        Assert.NotNull(watch);

        File.WriteAllText(_prefs, SpotifyPrefs.WriteEqualizer(Prefs, Rock));

        Assert.True(changed.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    private async Task StartPlayingSongA()
    {
        _app.IsRunning = true;
        _local.Report(SongA with { PositionUpdatedAt = _time.GetUtcNow() });
        await _player.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(_player.State.IsPlaying);
    }
}

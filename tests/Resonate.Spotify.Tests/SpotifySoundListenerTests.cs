using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class SpotifySoundListenerTests : IDisposable
{
    private const int Rate = 48_000;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-08T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _channel = new();
    private readonly FakeWebApi _web = new();
    private readonly FakeAudioEngine _engine = new();
    private readonly FakeCapture _capture = new();
    private readonly RecordingSoundSink _output = new();
    private readonly LocalPlayer _local;
    private readonly PlayerController _spotify;
    private readonly PlayerRouter _router;
    private readonly SpotifySoundListener _listener;
    private int? _program = 42;
    private bool _throwOnLookup;
    private int _lookups;
    private int _hearingChanges;

    public SpotifySoundListenerTests()
    {
        _local = new LocalPlayer(_engine, readCover: _ => null, time: _time, random: _ => 0);
        _spotify = new PlayerController(_channel, _channel, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time);
        _router = new PlayerRouter(_spotify, _local);
        _listener = new SpotifySoundListener(_router, _capture, FindProgram, _output, _time, schedule: work => work());
        _listener.HearingChanged += (_, _) => _hearingChanges++;
    }

    public void Dispose()
    {
        _listener.Dispose();
        _router.Dispose();
        _spotify.Dispose();
        _local.Dispose();
    }

    [Fact]
    public async Task Nothing_is_heard_until_the_bars_want_it()
    {
        await StartSpotifyPlaying();

        Assert.Empty(_capture.Starts);
        Assert.Equal(0, _lookups);

        _listener.Wanted = true;

        Assert.Equal([42], _capture.Starts);
        Assert.Same(_listener, _capture.Sink);
        Assert.True(_listener.IsListening);
        Assert.Equal(42, _listener.HeardProcessId);
    }

    [Fact]
    public async Task Pausing_stops_listening_and_playing_starts_it_again()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        Assert.True(_listener.IsListening);

        ReportSpotify(playing: false);

        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);

        ReportSpotify(playing: true);

        Assert.True(_listener.IsListening);
        Assert.Equal([42, 42], _capture.Starts);
    }

    [Fact]
    public async Task Local_files_are_never_heard_this_way()
    {
        _listener.Wanted = true;
        await _router.PlayAsync(new PlayRequest([File("a"), File("b")], 0, null, "Local Files"));
        await _local.WhenIdleAsync();

        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
        Assert.Empty(_capture.Starts);
        Assert.Equal(0, _lookups);
    }

    [Fact]
    public async Task Switching_to_local_files_stops_listening()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        await _router.PlayAsync(new PlayRequest([File("a")], 0, null, "Local Files"));
        await _local.WhenIdleAsync();

        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);
    }

    [Fact]
    public async Task The_users_switch_turns_listening_off_and_on()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _listener.Enabled = false;

        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);

        _listener.Enabled = true;

        Assert.Equal([42, 42], _capture.Starts);
    }

    [Fact]
    public async Task Bars_that_go_stop_listening()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _listener.Wanted = false;

        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);
    }

    [Fact]
    public async Task A_PC_that_cannot_capture_never_tries()
    {
        _capture.IsSupported = false;
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        Assert.Empty(_capture.Starts);
        Assert.False(_listener.IsListening);
    }

    [Fact]
    public async Task With_no_program_playing_nothing_starts()
    {
        _program = null;
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        Assert.Empty(_capture.Starts);
        Assert.False(_listener.IsListening);
    }

    [Fact]
    public async Task Sound_is_heard_until_it_stops_for_a_while()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        Assert.False(_listener.HearsSound);

        _listener.Write(Block(0), 2, Rate);
        Assert.False(_listener.HearsSound);

        _listener.Write(Block(0.5f), 2, Rate);
        Assert.True(_listener.HearsSound);
        Assert.Equal(1, _hearingChanges);

        // A short gap between songs keeps the bars following the sound.
        _time.Advance(TimeSpan.FromSeconds(1));
        _listener.Write([], 2, Rate);
        Assert.True(_listener.HearsSound);

        _time.Advance(TimeSpan.FromSeconds(1.5));
        _listener.Write([], 2, Rate);
        Assert.False(_listener.HearsSound);
        Assert.Equal(2, _hearingChanges);
    }

    [Fact]
    public async Task Quiet_sound_reaches_the_bars_levelled()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _listener.Write(Block(0.1f), 2, Rate);

        Assert.Equal(1, _output.Writes);
        Assert.Equal(SoundLeveller.Target, _output.Loudest, 3);
    }

    [Fact]
    public async Task Writes_while_not_listening_are_dropped()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        _listener.Wanted = false;

        _listener.Write(Block(0.5f), 2, Rate);

        Assert.Equal(0, _output.Writes);
        Assert.False(_listener.HearsSound);
    }

    [Fact]
    public async Task Silence_looks_for_the_program_again_and_follows_a_new_one()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        var lookups = _lookups;

        // Spotify restarted: its sound now comes from another process.
        _program = 77;
        _time.Advance(TimeSpan.FromSeconds(1));
        _listener.Write([], 2, Rate);
        Assert.Equal(lookups, _lookups);

        _time.Advance(SpotifySoundListener.LookAgainAfter);
        _listener.Write([], 2, Rate);

        Assert.Equal(lookups + 1, _lookups);
        Assert.Equal([42, 77], _capture.Starts);
        Assert.Equal(77, _listener.HeardProcessId);
    }

    [Fact]
    public async Task Silence_from_the_same_program_keeps_the_capture()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _time.Advance(SpotifySoundListener.LookAgainAfter);
        _listener.Write([], 2, Rate);

        Assert.Equal([42], _capture.Starts);
        Assert.Equal(0, _capture.Stops);
    }

    [Fact]
    public async Task Sound_that_flows_is_never_interrupted_to_look_again()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        var lookups = _lookups;
        _program = 77;

        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            _listener.Write(Block(0.5f), 2, Rate);
        }

        Assert.Equal(lookups, _lookups);
        Assert.Equal([42], _capture.Starts);
    }

    [Fact]
    public async Task Looking_again_follows_the_program_and_stops_when_none_plays()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _program = 9;
        _listener.LookAgain();
        Assert.Equal([42, 9], _capture.Starts);

        _program = null;
        _listener.LookAgain();
        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);
    }

    [Fact]
    public async Task A_failed_capture_is_tried_again_only_after_a_while()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _capture.Fail();

        Assert.False(_listener.IsListening);
        ReportSpotify(playing: true);
        Assert.Equal([42], _capture.Starts);

        // Nothing else happens meanwhile (the song just plays on): the wait itself brings the retry.
        _time.Advance(SpotifySoundListener.RetryAfterFailure - TimeSpan.FromMilliseconds(1));
        Assert.Equal([42], _capture.Starts);
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal([42, 42], _capture.Starts);
        Assert.True(_listener.IsListening);
    }

    [Fact]
    public async Task Trouble_finding_the_program_is_tried_again_later()
    {
        _throwOnLookup = true;
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        Assert.False(_listener.IsListening);
        Assert.Empty(_capture.Starts);

        _throwOnLookup = false;
        ReportSpotify(playing: true);
        Assert.Empty(_capture.Starts);

        _time.Advance(SpotifySoundListener.RetryAfterFailure);

        Assert.Equal([42], _capture.Starts);
        Assert.True(_listener.IsListening);
    }

    [Fact]
    public async Task Disposing_stops_listening()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();

        _listener.Dispose();

        Assert.False(_listener.IsListening);
        Assert.Equal(1, _capture.Stops);
    }

    private static float[] Block(float level)
    {
        var block = new float[960];
        for (var i = 0; i < block.Length; i++)
        {
            block[i] = i % 2 == 0 ? level : -level;
        }

        return block;
    }

    private static TrackInfo File(string name) =>
        new(null, name, "Me", "Files", null, TimeSpan.FromSeconds(100), null, null, IsExplicit: false, IsPlayable: true)
        {
            FilePath = $"C:\\Music\\{name}.flac",
        };

    private int? FindProgram()
    {
        _lookups++;
        return _throwOnLookup ? throw new InvalidOperationException("The process list could not be read.") : _program;
    }

    private async Task StartSpotifyPlaying()
    {
        ReportSpotify(playing: true);
        await _spotify.StartAsync(TestContext.Current.CancellationToken);
    }

    private void ReportSpotify(bool playing) =>
        _channel.Report(new LocalMediaSnapshot
        {
            HasSession = true,
            Title = "Song A",
            Artist = "Artist",
            Album = "Album",
            IsPlaying = playing,
            Position = TimeSpan.FromSeconds(10),
            PositionUpdatedAt = _time.GetUtcNow(),
            Duration = TimeSpan.FromSeconds(200),
            CanSeek = true,
        });

    private sealed class FakeCapture : IAppSoundCapture
    {
        public event EventHandler? Failed;

        public bool IsSupported { get; set; } = true;

        public List<int> Starts { get; } = [];

        public int Stops { get; private set; }

        public ISoundSink? Sink { get; private set; }

        public void Start(int processId, ISoundSink sink)
        {
            Starts.Add(processId);
            Sink = sink;
        }

        public void Stop()
        {
            Stops++;
            Sink = null;
        }

        public void Fail()
        {
            Sink = null;
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingSoundSink : ISoundSink
    {
        public int Writes { get; private set; }

        public float Loudest { get; private set; }

        public void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
        {
            Writes++;
            Loudest = 0;
            foreach (var sample in interleaved)
            {
                Loudest = Math.Max(Loudest, Math.Abs(sample));
            }
        }
    }
}

public sealed class SoundLevellerTests
{
    private const int Rate = 48_000;

    private readonly SoundLeveller _leveller = new();

    [Fact]
    public void Music_at_full_volume_passes_unchanged()
    {
        var output = Level(0.95f);

        Assert.Equal(1f, _leveller.Gain);
        Assert.Equal(0.95f, output.Max(), 4);
    }

    [Fact]
    public void Quiet_music_is_lifted_towards_the_target()
    {
        var output = Level(0.2f);

        Assert.Equal(SoundLeveller.Target / 0.2f, _leveller.Gain, 3);
        Assert.Equal(SoundLeveller.Target, output.Max(), 3);
    }

    [Fact]
    public void Very_quiet_sound_is_lifted_at_most_eight_times()
    {
        var output = Level(0.01f);

        Assert.Equal(SoundLeveller.MaxGain, _leveller.Gain);
        Assert.Equal(0.08f, output.Max(), 4);
    }

    [Fact]
    public void Silence_stays_silent()
    {
        var output = Level(0);

        Assert.All(output, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void A_loud_moment_is_remembered_for_seconds_not_forever()
    {
        Level(0.8f);

        // A quiet passage right after: still about as loud as it was played.
        Level(0.1f);
        Assert.InRange(_leveller.Gain, 1f, 1.1f);

        // After a long quiet stretch it has been lifted.
        for (var i = 0; i < 1500; i++)
        {
            Level(0.1f);
        }

        Assert.Equal(SoundLeveller.Target / 0.1f, _leveller.Gain, 2);
    }

    [Fact]
    public void A_broken_sample_does_not_stick()
    {
        var broken = new float[960];
        broken[3] = float.PositiveInfinity;
        _leveller.Apply(broken, new float[960], 2, Rate);

        Level(0.4f);

        Assert.Equal(SoundLeveller.Target / 0.4f, _leveller.Gain, 3);
    }

    [Fact]
    public void Reset_forgets_the_loud_moment()
    {
        Level(0.8f);
        _leveller.Reset();

        Level(0.1f);

        Assert.Equal(SoundLeveller.Target / 0.1f, _leveller.Gain, 3);
    }

    [Fact]
    public void A_short_output_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _leveller.Apply(new float[10], new float[5], 2, Rate));
    }

    /// <summary>10 ms of stereo at <paramref name="level"/>, levelled.</summary>
    private float[] Level(float level)
    {
        var input = Enumerable.Range(0, 960).Select(i => i % 2 == 0 ? level : -level).ToArray();
        var output = new float[input.Length];
        _leveller.Apply(input, output, 2, Rate);
        return output;
    }
}

using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class LocalAudioTapSlotTests
{
    private readonly LocalAudioTapSlot _slot = new();
    private readonly RecordingSink _sink = new();
    private readonly List<FakeTap> _taps = [];

    [Fact]
    public void A_sink_is_tapped_only_while_there_is_a_graph()
    {
        _slot.SetSink(_sink);
        Assert.False(_slot.IsAttached);

        _slot.GraphReady(Attach);

        var tap = Assert.Single(_taps);
        Assert.Same(_sink, tap.Sink);
        Assert.True(_slot.IsAttached);

        _slot.GraphGone();

        Assert.True(tap.Disposed);
        Assert.False(_slot.IsAttached);
        Assert.Same(_sink, _slot.Sink);
    }

    [Fact]
    public void A_graph_rebuilt_after_a_device_change_gets_a_new_tap()
    {
        _slot.SetSink(_sink);
        _slot.GraphReady(Attach);

        // The engine's recovery: the old graph is torn down, then a new one is made.
        _slot.GraphGone();
        _slot.GraphReady(Attach);

        Assert.Equal(2, _taps.Count);
        Assert.True(_taps[0].Disposed);
        Assert.False(_taps[1].Disposed);
        Assert.True(_slot.IsAttached);
    }

    [Fact]
    public void A_new_graph_without_the_old_one_going_first_still_drops_its_tap()
    {
        _slot.SetSink(_sink);
        _slot.GraphReady(Attach);
        _slot.GraphReady(Attach);

        Assert.True(_taps[0].Disposed);
        Assert.False(_taps[1].Disposed);
    }

    [Fact]
    public void Without_a_sink_no_graph_gets_a_tap()
    {
        _slot.GraphReady(Attach);
        Assert.Empty(_taps);

        _slot.SetSink(_sink);
        Assert.Single(_taps);

        _slot.SetSink(null);
        _slot.GraphGone();
        _slot.GraphReady(Attach);

        Assert.True(_taps[0].Disposed);
        Assert.Single(_taps);
        Assert.False(_slot.IsAttached);
    }

    [Fact]
    public void The_same_sink_again_keeps_the_tap_and_another_sink_gets_its_own()
    {
        _slot.GraphReady(Attach);
        _slot.SetSink(_sink);
        _slot.SetSink(_sink);
        Assert.Single(_taps);

        var other = new RecordingSink();
        _slot.SetSink(other);

        Assert.True(_taps[0].Disposed);
        Assert.Same(other, _taps[1].Sink);
    }

    [Fact]
    public void A_tap_that_cannot_be_made_never_throws_and_is_tried_again_on_the_next_graph()
    {
        _slot.SetSink(_sink);
        _slot.GraphReady(_ => throw new InvalidOperationException("No frame output node."));
        Assert.False(_slot.IsAttached);

        _slot.GraphReady(_ => null);
        Assert.False(_slot.IsAttached);

        _slot.GraphGone();
        _slot.GraphReady(Attach);
        Assert.True(_slot.IsAttached);
    }

    [Fact]
    public void A_tap_that_fails_to_close_does_not_stop_the_graph_going()
    {
        _slot.SetSink(_sink);
        _slot.GraphReady(_ => new FakeTap(_sink) { FailOnDispose = true });

        _slot.GraphGone();

        Assert.False(_slot.IsAttached);
    }

    private FakeTap Attach(ILocalAudioSink sink)
    {
        var tap = new FakeTap(sink);
        _taps.Add(tap);
        return tap;
    }

    private sealed class FakeTap(ILocalAudioSink sink) : IDisposable
    {
        public ILocalAudioSink Sink { get; } = sink;

        public bool Disposed { get; private set; }

        public bool FailOnDispose { get; init; }

        public void Dispose()
        {
            Disposed = true;
            if (FailOnDispose)
            {
                throw new InvalidOperationException("The graph is gone.");
            }
        }
    }
}

public sealed class LocalPlayerSinkTests : IDisposable
{
    private readonly FakeAudioEngine _engine = new();
    private readonly LocalPlayer _player;

    public LocalPlayerSinkTests() => _player = new LocalPlayer(_engine, readCover: _ => null, random: _ => 0);

    public void Dispose() => _player.Dispose();

    [Fact]
    public async Task The_sink_reaches_the_engine()
    {
        var sink = new RecordingSink();

        _player.SetAudioSink(sink);
        await _player.WhenIdleAsync();
        Assert.Same(sink, _engine.Sink);

        _player.SetAudioSink(null);
        await _player.WhenIdleAsync();
        Assert.Null(_engine.Sink);
    }

    [Fact]
    public async Task Only_the_newest_wish_reaches_a_busy_engine()
    {
        using var hold = new ManualResetEventSlim();
        _engine.SinkHold = hold;
        var first = new RecordingSink();
        var last = new RecordingSink();

        _player.SetAudioSink(first);
        await Eventually(() => _engine.SinkChanges == 1);
        _player.SetAudioSink(null);
        _player.SetAudioSink(last);
        hold.Set();
        await _player.WhenIdleAsync();

        Assert.Same(last, _engine.Sink);
        Assert.Equal(2, _engine.SinkChanges);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}

public sealed class LocalAudioListenerTests : IDisposable
{
    private static readonly float[] Quantum = new float[960];

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeLocalChannel _channel = new();
    private readonly FakeWebApi _web = new();
    private readonly FakeAudioEngine _engine = new();
    private readonly RecordingSink _sink = new();
    private readonly LocalPlayer _local;
    private readonly PlayerController _spotify;
    private readonly PlayerRouter _router;
    private readonly LocalAudioListener _listener;

    public LocalAudioListenerTests()
    {
        _local = new LocalPlayer(_engine, readCover: _ => null, time: _time, random: _ => 0);
        _spotify = new PlayerController(_channel, _channel, _web, new LocalDeviceResolver(_web, "MY-PC", _time), launcher: null, _time);
        _router = new PlayerRouter(_spotify, _local);
        _listener = new LocalAudioListener(_router, _sink);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _router.Dispose();
        _spotify.Dispose();
        _local.Dispose();
    }

    [Fact]
    public async Task Nothing_is_heard_until_a_visualiser_wants_it()
    {
        await PlayFiles();
        _engine.Play(Quantum);

        Assert.Null(_engine.Sink);
        Assert.Equal(0, _sink.Writes);

        _listener.Wanted = true;
        await _local.WhenIdleAsync();
        _engine.Play(Quantum);

        Assert.Same(_sink, _engine.Sink);
        Assert.True(_listener.IsListening);
        Assert.Equal(1, _sink.Writes);
    }

    [Fact]
    public async Task Spotify_songs_are_never_heard()
    {
        _listener.Wanted = true;
        await StartSpotifyPlaying();
        await _local.WhenIdleAsync();

        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Null(_engine.Sink);
        Assert.False(_listener.IsListening);
        Assert.False(_listener.IsLive);
        Assert.Equal(0, _engine.SinkChanges);
    }

    [Fact]
    public async Task Switching_to_Spotify_takes_the_tap_down_and_back_to_files_puts_it_up()
    {
        _listener.Wanted = true;
        await PlayFiles();
        Assert.Same(_sink, _engine.Sink);

        await StartSpotifyPlaying();
        await _router.PlayAsync(new PlayRequest([Song("x")], 0, null, "Search"));
        await _local.WhenIdleAsync();
        _engine.Play(Quantum);

        Assert.Equal(PlaybackSource.Spotify, _router.ActiveSource);
        Assert.Null(_engine.Sink);
        Assert.Equal(0, _sink.Writes);

        await PlayFiles();
        _engine.Play(Quantum);

        Assert.Same(_sink, _engine.Sink);
        Assert.Equal(1, _sink.Writes);
    }

    [Fact]
    public async Task The_tap_comes_down_when_the_visualiser_goes()
    {
        _listener.Wanted = true;
        await PlayFiles();

        _listener.Wanted = false;
        await _local.WhenIdleAsync();
        _engine.Play(Quantum);

        Assert.Null(_engine.Sink);
        Assert.False(_listener.IsListening);
        Assert.Equal(0, _sink.Writes);
    }

    [Fact]
    public async Task Pausing_a_file_keeps_the_tap_but_is_not_live()
    {
        _listener.Wanted = true;
        await PlayFiles();
        Assert.True(_listener.IsLive);

        await _router.PauseAsync();
        await _local.WhenIdleAsync();

        Assert.False(_listener.IsLive);
        Assert.Same(_sink, _engine.Sink);
        Assert.Equal(1, _engine.SinkChanges);
    }

    [Fact]
    public async Task Disposing_the_listener_takes_the_tap_down()
    {
        _listener.Wanted = true;
        await PlayFiles();

        _listener.Dispose();
        await _local.WhenIdleAsync();

        Assert.Null(_engine.Sink);
        Assert.False(_listener.IsListening);
    }

    private static TrackInfo File(string name) =>
        new(null, name, "Me", "Files", null, TimeSpan.FromSeconds(100), null, null, IsExplicit: false, IsPlayable: true)
        {
            FilePath = $"C:\\Music\\{name}.flac",
        };

    private static TrackInfo Song(string id) =>
        new($"spotify:track:{id}", $"Song {id}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true);

    private async Task PlayFiles()
    {
        await _router.PlayAsync(new PlayRequest([File("a"), File("b")], 0, null, "Local Files"));
        await _local.WhenIdleAsync();
        Assert.Equal(PlaybackSource.LocalFiles, _router.ActiveSource);
    }

    private async Task StartSpotifyPlaying()
    {
        _channel.Report(new LocalMediaSnapshot
        {
            HasSession = true,
            Title = "Song A",
            Artist = "Artist",
            Album = "Album",
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(10),
            PositionUpdatedAt = _time.GetUtcNow(),
            Duration = TimeSpan.FromSeconds(200),
            CanSeek = true,
        });
        await _spotify.StartAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>A sink that counts what it hears.</summary>
internal sealed class RecordingSink : ILocalAudioSink
{
    private int _starts;
    private int _writes;

    public int Starts => Volatile.Read(ref _starts);

    public int Writes => Volatile.Read(ref _writes);

    public void Start(int latencySamples) => Interlocked.Increment(ref _starts);

    public void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate) => Interlocked.Increment(ref _writes);
}

using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;

namespace Resonate.Spotify.Tests;

public class SpotifyAppKeeperTests
{
    private readonly FakeSpotifyApp _app = new();
    private readonly FakeSpotifyWindow _window;
    private ControlChannel _channel = ControlChannel.Local;

    public SpotifyAppKeeperTests() => _window = new FakeSpotifyWindow(_app.Events);

    private SpotifyAppKeeper CreateKeeper() => new(_app, _window, () => _channel);

    [Fact]
    public async Task With_Windows_media_controls_Spotify_is_started_hidden()
    {
        _window.Enabled = false;
        _app.Events.Clear();
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.Running, await keeper.FollowAsync(CancellationToken.None));

        Assert.True(_app.IsRunning);

        // Looked after first, so its window is hidden as it appears.
        Assert.Equal(["window looked after", "started"], _app.Events);
        Assert.Equal(0, _app.CloseCalls);
    }

    [Fact]
    public async Task With_Windows_media_controls_a_running_Spotify_is_left_running()
    {
        _app.IsRunning = true;
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.Running, await keeper.FollowAsync(CancellationToken.None));

        Assert.DoesNotContain("started", _app.Events);
    }

    [Fact]
    public async Task A_missing_Spotify_is_reported()
    {
        _app.Installed = false;
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.NotInstalled, await keeper.FollowAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Web_API_only_closes_Spotify_and_then_leaves_it_alone()
    {
        _app.IsRunning = true;
        _channel = ControlChannel.WebApi;
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.Closed, await keeper.FollowAsync(CancellationToken.None));

        Assert.False(_app.IsRunning);
        Assert.Equal(0, _app.EnsureRunningCalls);

        // Kept hidden while it closed, so it never came back onto the taskbar.
        Assert.Equal(["closed", "window let go"], _app.Events);
    }

    [Fact]
    public async Task Web_API_only_with_Spotify_not_running_starts_nothing()
    {
        _channel = ControlChannel.WebApi;
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.Closed, await keeper.FollowAsync(CancellationToken.None));

        Assert.Equal(["window let go"], _app.Events);
        Assert.False(_app.IsRunning);
    }

    [Fact]
    public async Task A_Spotify_that_will_not_close_is_reported_and_given_back()
    {
        _app.IsRunning = true;
        _app.Closes = false;
        _channel = ControlChannel.WebApi;
        using var keeper = CreateKeeper();

        Assert.Equal(SpotifyAppOutcome.CouldNotClose, await keeper.FollowAsync(CancellationToken.None));

        Assert.True(_app.IsRunning);
        Assert.False(_window.Enabled);
    }

    [Fact]
    public async Task Switching_back_to_Windows_media_controls_starts_Spotify_again()
    {
        _app.IsRunning = true;
        _channel = ControlChannel.WebApi;
        using var keeper = CreateKeeper();
        await keeper.FollowAsync(CancellationToken.None);

        _channel = ControlChannel.Local;
        Assert.Equal(SpotifyAppOutcome.Running, await keeper.FollowAsync(CancellationToken.None));

        Assert.Equal(["closed", "window let go", "window looked after", "started"], _app.Events);
    }

    [Fact]
    public async Task Each_step_follows_the_channel_as_it_is_when_it_runs()
    {
        _app.IsRunning = true;
        _channel = ControlChannel.WebApi;
        using var keeper = CreateKeeper();

        // Switched to Web API only and straight back before the first step ran.
        _channel = ControlChannel.Local;
        await Task.WhenAll(keeper.FollowAsync(CancellationToken.None), keeper.FollowAsync(CancellationToken.None));

        Assert.True(_app.IsRunning);
        Assert.DoesNotContain("closed", _app.Events);
        Assert.True(_window.Enabled);
    }

    /// <summary>Writes each change of <see cref="Enabled"/> into the app's events, to show the order.</summary>
    private sealed class FakeSpotifyWindow(List<string> events) : ISpotifyAppWindow
    {
        private bool _enabled = true;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                events.Add(value ? "window looked after" : "window let go");
            }
        }

        public bool KeepHidden { get; set; } = true;

        public bool SaveResources { get; set; } = true;

        public bool ShowSpotify() => true;
    }
}

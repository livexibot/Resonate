using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class OwnPlayerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-08T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeTokens _tokens = new();
    private readonly List<FakeWebPlayerPage> _pages = [];
    private readonly OwnPlayer _player;
    private bool _canPlay = true;
    private Func<FakeWebPlayerPage> _nextPage = () => new FakeWebPlayerPage();

    public OwnPlayerTests() =>
        _player = new OwnPlayer(
            () =>
            {
                var page = _nextPage();
                lock (_pages)
                {
                    _pages.Add(page);
                }

                return page;
            },
            _tokens,
            () => _canPlay,
            time: _time);

    private FakeWebPlayerPage Page
    {
        get
        {
            lock (_pages)
            {
                return _pages[^1];
            }
        }
    }

    private int PageCount
    {
        get
        {
            lock (_pages)
            {
                return _pages.Count;
            }
        }
    }

    public void Dispose() => _player.Dispose();

    [Fact]
    public async Task Opens_the_page_starts_Spotifys_player_and_becomes_ready_with_its_device()
    {
        await _player.StartAsync();

        Assert.True(Page.Loaded);
        Assert.Equal(["start Resonate"], Page.Sent);
        Assert.Equal(OwnPlayerStatus.Starting, _player.Status);
        Assert.Null(_player.DeviceId);

        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        Assert.Equal(OwnPlayerStatus.Ready, _player.Status);
        Assert.Equal("dev-1", _player.DeviceId);
    }

    [Fact]
    public async Task Answers_Spotifys_player_with_the_sign_ins_token()
    {
        await _player.StartAsync();

        Page.Say("""{"type":"token"}""");

        await WaitUntil(() => Page.Sent.Contains("token token-1"));
        Assert.Equal([null], _tokens.Requests);
    }

    [Fact]
    public async Task A_rejected_token_is_renewed_once_then_it_asks_to_sign_in_again()
    {
        await _player.StartAsync();
        Page.Say("""{"type":"token"}""");
        await WaitUntil(() => Page.Sent.Contains("token token-1"));

        Page.Say("""{"type":"error","kind":"authentication","message":"Authentication failed"}""");
        Assert.Contains("reconnect", Page.Sent);
        Page.Say("""{"type":"token"}""");
        await WaitUntil(() => Page.Sent.Contains("token token-2"));
        Assert.Equal([null, "token-1"], _tokens.Requests);

        Page.Say("""{"type":"error","kind":"authentication","message":"Authentication failed"}""");

        await WaitUntil(() => _player.Status == OwnPlayerStatus.NeedsSignIn && Page.Disposed);
    }

    [Fact]
    public async Task A_ready_player_whose_token_is_turned_down_has_no_device_until_it_connects_again()
    {
        await _player.StartAsync();
        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        Page.Say("""{"type":"error","kind":"authentication","message":"Authentication failed"}""");

        Assert.Equal(OwnPlayerStatus.Starting, _player.Status);
        Assert.Null(_player.DeviceId);
        Assert.Contains("reconnect", Page.Sent);

        // Never connects again: it counts as failed.
        _time.Advance(OwnPlayer.ConnectTimeout);
        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed && Page.Disposed);
    }

    [Fact]
    public async Task Without_the_permission_to_play_it_asks_to_sign_in_again_and_opens_nothing()
    {
        _canPlay = false;

        await _player.StartAsync();

        Assert.Equal(OwnPlayerStatus.NeedsSignIn, _player.Status);
        Assert.Equal(0, PageCount);
    }

    [Fact]
    public async Task A_sign_in_that_no_longer_works_asks_to_sign_in_again()
    {
        _tokens.Failure = new SpotifyAuthException("invalid_grant", "The sign-in expired.");
        await _player.StartAsync();

        Page.Say("""{"type":"token"}""");

        await WaitUntil(() => _player.Status == OwnPlayerStatus.NeedsSignIn && Page.Disposed);
    }

    [Fact]
    public async Task Spotifys_sign_in_service_failing_for_a_moment_is_tried_again_later()
    {
        _tokens.Failure = new SpotifyAuthException("http_503", "Spotify could not renew the sign-in.");
        await _player.StartAsync();

        Page.Say("""{"type":"token"}""");
        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed && Page.Disposed);

        _tokens.Failure = null;
        _time.Advance(OwnPlayer.RetryDelays[0]);
        await WaitUntil(() => PageCount == 2 && Page.Sent.Contains("start Resonate"));
    }

    [Theory]
    [InlineData("account", OwnPlayerStatus.NeedsPremium)]
    [InlineData("initialization", OwnPlayerStatus.Unsupported)]
    public async Task Says_why_it_cannot_play_and_does_not_try_again(string kind, OwnPlayerStatus expected)
    {
        await _player.StartAsync();

        Page.Say($$"""{"type":"error","kind":"{{kind}}","message":"No."}""");

        await WaitUntil(() => _player.Status == expected && Page.Disposed);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, PageCount);
    }

    [Fact]
    public async Task Without_WebView2_this_computer_cannot_run_it()
    {
        _nextPage = () => new FakeWebPlayerPage { LoadFailure = new WebPlayerUnavailableException("No WebView2 runtime.") };

        await _player.StartAsync();

        Assert.Equal(OwnPlayerStatus.Unsupported, _player.Status);
    }

    [Fact]
    public async Task After_a_crash_it_opens_a_new_page_a_little_later()
    {
        await _player.StartAsync();
        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        Page.Crash();
        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed);
        Assert.Null(_player.DeviceId);
        Assert.Equal(1, PageCount);

        _time.Advance(OwnPlayer.RetryDelays[0]);

        await WaitUntil(() => PageCount == 2 && Page.Sent.Contains("start Resonate"));
        Assert.Equal(OwnPlayerStatus.Starting, _player.Status);
    }

    [Fact]
    public async Task Spotifys_player_that_never_connects_counts_as_failed()
    {
        await _player.StartAsync();

        _time.Advance(OwnPlayer.ConnectTimeout);

        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed && Page.Disposed);
    }

    [Fact]
    public async Task Stopping_closes_the_page_and_nothing_starts_again()
    {
        await _player.StartAsync();
        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        await _player.StopAsync();

        Assert.Equal(OwnPlayerStatus.Off, _player.Status);
        Assert.Null(_player.DeviceId);
        Assert.True(Page.Disposed);
        Page.Say("""{"type":"ready","deviceId":"dev-2"}""");
        Assert.Equal(OwnPlayerStatus.Off, _player.Status);
    }

    [Fact]
    public async Task A_retry_waiting_when_it_was_stopped_never_starts()
    {
        await _player.StartAsync();
        Page.Crash();
        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed);

        await _player.StopAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, PageCount);
        Assert.Equal(OwnPlayerStatus.Off, _player.Status);
    }

    [Fact]
    public async Task Stopping_while_the_page_still_opens_gives_up_the_start()
    {
        _nextPage = () => new FakeWebPlayerPage { Hangs = true };
        var starting = _player.StartAsync();
        await WaitUntil(() => PageCount == 1);

        await _player.StopAsync();

        Assert.True(starting.IsCompleted);
        Assert.Equal(OwnPlayerStatus.Off, _player.Status);
        Assert.True(Page.Disposed);
    }

    [Fact]
    public async Task A_page_that_never_opens_counts_as_failed()
    {
        _nextPage = () => new FakeWebPlayerPage { Hangs = true };
        _ = _player.StartAsync();
        await WaitUntil(() => PageCount == 1);

        _time.Advance(OwnPlayer.ConnectTimeout);

        await WaitUntil(() => _player.Status == OwnPlayerStatus.Failed && Page.Disposed);
    }

    [Fact]
    public async Task Waiting_for_the_device_ends_once_it_is_ready()
    {
        await _player.StartAsync();

        var waiting = _player.WaitForDeviceAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        Assert.Equal("dev-1", await waiting);
    }

    [Fact]
    public async Task Waiting_for_the_device_gives_up_after_the_timeout()
    {
        await _player.StartAsync();

        var waiting = _player.WaitForDeviceAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(await waiting);
    }

    [Fact]
    public async Task Nothing_to_wait_for_when_it_is_off() =>
        Assert.Null(await _player.WaitForDeviceAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Losing_the_connection_drops_the_device_until_it_is_ready_again()
    {
        await _player.StartAsync();
        Page.Say("""{"type":"ready","deviceId":"dev-1"}""");

        Page.Say("""{"type":"notReady","deviceId":"dev-1"}""");

        Assert.Equal(OwnPlayerStatus.Starting, _player.Status);
        Assert.Null(_player.DeviceId);
        var waiting = _player.WaitForDeviceAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Page.Say("""{"type":"ready","deviceId":"dev-2"}""");
        Assert.Equal("dev-2", await waiting);
    }

    [Fact]
    public async Task Says_when_what_plays_changed()
    {
        var changes = 0;
        _player.PlaybackChanged += (_, _) => Interlocked.Increment(ref changes);
        await _player.StartAsync();

        Page.Say("""{"type":"state","paused":false}""");

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Starting_again_after_signing_in_again_opens_a_new_page()
    {
        _canPlay = false;
        await _player.StartAsync();
        Assert.Equal(OwnPlayerStatus.NeedsSignIn, _player.Status);

        _canPlay = true;
        await _player.StartAsync();

        Assert.Equal(1, PageCount);
        Assert.Equal(OwnPlayerStatus.Starting, _player.Status);
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
}

public class WebPlayerMessageTests
{
    [Fact]
    public void Reads_what_the_page_says() =>
        Assert.Equal(
            new WebPlayerMessage("ready", DeviceId: "abc"),
            WebPlayerMessage.Parse("""{"type":"ready","deviceId":"abc","extra":1}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"deviceId":"abc"}""")]
    [InlineData("""{"type":5}""")]
    public void Ignores_anything_else(string? json) => Assert.Null(WebPlayerMessage.Parse(json));

    [Fact]
    public void A_token_is_sent_as_proper_JSON()
    {
        var json = WebPlayerCommands.Token("a\"b\\c");

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("a\"b\\c", document.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public void The_volume_stays_between_0_and_1()
    {
        using var document = System.Text.Json.JsonDocument.Parse(WebPlayerCommands.Start("Resonate", 3));
        Assert.Equal(1, document.RootElement.GetProperty("volume").GetDouble());
    }
}

public class OwnDeviceChoiceTests
{
    private static readonly Device Phone = new() { Id = "phone", Name = "Phone", Type = "Smartphone" };
    private static readonly Device Kitchen = new() { Id = "kitchen", Name = "Kitchen", Type = "Speaker" };
    private static readonly Device ThisComputer = new() { Id = "here", Name = "MY-PC", Type = "Computer" };
    private static readonly Device Own = new() { Id = "own", Name = "Resonate", Type = "Computer" };

    [Fact]
    public void Resonates_own_player_comes_before_the_Spotify_app_on_this_computer() =>
        Assert.Equal("own", WebDeviceResolver.Pick([ThisComputer, Own, Phone], null, "MY-PC", "own")?.Id);

    [Fact]
    public void The_device_that_plays_still_comes_first() =>
        Assert.Equal("phone", WebDeviceResolver.Pick([Own, new Device { Id = "phone", Name = "Phone", IsActive = true }], null, "MY-PC", "own")?.Id);

    [Fact]
    public void It_comes_before_the_device_picked_last_which_is_used_while_it_is_not_ready()
    {
        Assert.Equal("own", WebDeviceResolver.Pick([Own, Kitchen], "Kitchen", "MY-PC", "own")?.Id);
        Assert.Equal("kitchen", WebDeviceResolver.Pick([Phone, Kitchen], "Kitchen", "MY-PC", ownDeviceId: null)?.Id);
    }

    [Fact]
    public void It_is_used_before_Spotify_lists_it()
    {
        var picked = WebDeviceResolver.Pick([Phone, Kitchen], null, "MY-PC", "own", "Resonate");

        Assert.Equal("own", picked?.Id);
        Assert.Equal("Resonate", picked?.Name);
    }

    [Fact]
    public void Its_device_is_not_mistaken_for_the_Spotify_app_while_it_is_not_ready() =>
        Assert.Equal("kitchen", WebDeviceResolver.Pick([Kitchen], null, "MY-PC", ownDeviceId: null)?.Id);

    [Fact]
    public void Another_Resonate_is_never_taken_for_the_Spotify_app_on_this_computer()
    {
        var elsewhere = new Device { Id = "elsewhere", Name = "Resonate", Type = "Computer" };

        Assert.Null(WebDeviceResolver.Pick([elsewhere, Phone], null, "MY-PC", ownDeviceId: null, "Resonate"));
        Assert.Equal("here", WebDeviceResolver.Pick([elsewhere, ThisComputer], null, "OTHER-NAME", ownDeviceId: null, "Resonate")?.Id);
    }

    [Fact]
    public async Task A_play_command_waits_for_it_while_it_starts()
    {
        var web = new FakeWebApi();
        web.Devices.Clear();
        web.Devices.Add(Phone);
        web.Devices.Add(Kitchen);
        var own = new FakeOwnDevice();
        var resolver = new WebDeviceResolver(web, "MY-PC", own);

        var choosing = resolver.ChooseAsync(TestContext.Current.CancellationToken);
        Assert.False(choosing.IsCompleted);
        own.Ready.SetResult("own");

        Assert.Equal("own", (await choosing)?.Id);
    }

    private sealed class FakeOwnDevice : IOwnDevice
    {
        public TaskCompletionSource<string?> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "Resonate";

        public string? DeviceId => Ready.Task.IsCompletedSuccessfully ? Ready.Task.Result : null;

        public Task<string?> WaitForDeviceAsync(TimeSpan timeout, CancellationToken cancellationToken) => Ready.Task;
    }
}

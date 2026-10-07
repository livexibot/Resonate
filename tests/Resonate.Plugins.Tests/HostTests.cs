using System.Text.Json.Nodes;
using Resonate.Plugins.Protocol;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>The helper's JavaScript side: the plugin API, permissions, timers and limits.</summary>
public sealed class HostTests
{
    private static readonly NowPlaying Song = new()
    {
        Title = "Midnight City",
        Artists = "M83",
        Album = "Hurry Up, We're Dreaming",
        Uri = "spotify:track:1",
        IsPlaying = true,
        Position = 10,
        Duration = 240,
        Volume = 0.8,
        CanSeek = true,
    };

    [Fact]
    public async Task Says_it_is_ready_and_starts_a_plugin()
    {
        await using var host = new HostHarness();
        var ready = await host.WaitForAsync(MessageTypes.Ready);
        Assert.Equal(PluginHost.HostLoop.Version, ready.Version);

        host.LoadScript("hello", "console.log('hi');");
        var log = await host.WaitForAsync(MessageTypes.Log, "hello");
        Assert.Equal("hi", log.Text);
        await host.WaitForAsync(MessageTypes.Loaded, "hello");
    }

    [Fact]
    public async Task A_script_that_does_not_parse_fails_to_start()
    {
        await using var host = new HostHarness();
        host.LoadScript("broken", "this is not javascript (");
        var failed = await host.WaitForAsync(MessageTypes.Failed, "broken");
        Assert.StartsWith("The plugin could not start", failed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commands_reach_the_player_only_with_permission()
    {
        await using var host = new HostHarness();
        const string script = """
            resonate.ui.addCommand('skip', 'Skip', () => resonate.player.next());
            resonate.ui.addCommand('louder', 'Louder', () => resonate.player.setVolume(0.9));
            """;
        host.LoadScript("limited", script, [PluginPermissions.PlayerRead, PluginPermissions.PlayerControl]);
        var commands = await host.WaitForAsync(m => m.Type == MessageTypes.Commands && m.Commands!.Count == 2);
        Assert.Equal(["Skip", "Louder"], commands.Commands!.Select(c => c.Title));

        host.Invoke("limited", "skip");
        var next = await host.WaitForAsync(MessageTypes.Player, "limited");
        Assert.Equal(PlayerActions.Next, next.Action);

        host.Invoke("limited", "louder");
        var error = await host.WaitForAsync(m => m.Type == MessageTypes.Log && m.Level == "error");
        Assert.Contains("player.volume", error.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Seen, m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Volume);
    }

    [Fact]
    public async Task Plugins_see_what_plays_and_the_position_moves_on()
    {
        await using var host = new HostHarness();
        const string script = """
            resonate.player.onTrackChange(state => resonate.ui.setStatus('track ' + state.title));
            resonate.ui.addCommand('where', 'Where', () => resonate.ui.setStatus('at ' + Math.round(resonate.player.state.position)));
            """;
        host.LoadScript("watcher", script);
        await host.WaitForAsync(MessageTypes.Loaded, "watcher");

        host.SendState(Song);
        var status = await host.WaitForAsync(MessageTypes.Status, "watcher");
        Assert.Equal("track Midnight City", status.Text);

        await host.AdvanceAsync(TimeSpan.FromSeconds(5));
        host.Invoke("watcher", "where");
        status = await host.WaitForAsync(MessageTypes.Status, "watcher");
        Assert.Equal("at 15", status.Text);
    }

    [Fact]
    public async Task A_plugin_without_player_permission_sees_nothing_playing()
    {
        await using var host = new HostHarness();
        host.LoadScript("blind", "resonate.player.onChange(() => resonate.ui.setStatus('saw it'));", permissions: []);
        await host.WaitForAsync(MessageTypes.Loaded, "blind");
        host.SendState(Song);
        Assert.DoesNotContain(await host.DrainAsync(), m => m.Type == MessageTypes.Status);
    }

    [Fact]
    public async Task Timers_run_on_the_plugins_clock_and_can_be_cleared()
    {
        await using var host = new HostHarness();
        const string script = """
            setTimeout(() => resonate.ui.setStatus('once'), 1000);
            const cleared = setTimeout(() => resonate.ui.setStatus('never'), 500);
            clearTimeout(cleared);
            let ticks = 0;
            const every = setInterval(() => {
                ticks += 1;
                if (ticks === 3) {
                    clearInterval(every);
                    resonate.ui.setStatus('ticked 3');
                }
            }, 2000);
            """;
        host.LoadScript("timers", script);
        await host.WaitForAsync(MessageTypes.Loaded, "timers");

        await host.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("once", (await host.WaitForAsync(MessageTypes.Status, "timers")).Text);

        await host.AdvanceAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("ticked 3", (await host.WaitForAsync(MessageTypes.Status, "timers")).Text);
        Assert.DoesNotContain(host.Seen, m => m.Text == "never");
    }

    [Fact]
    public async Task A_stuck_plugin_is_stopped_after_two_seconds_and_keeps_working()
    {
        await using var host = new HostHarness();
        const string script = """
            resonate.ui.addCommand('hang', 'Hang', () => { while (true) { } });
            resonate.ui.addCommand('ok', 'OK', () => resonate.ui.setStatus('still here'));
            """;
        host.LoadScript("stuck", script);
        await host.WaitForAsync(MessageTypes.Loaded, "stuck");

        host.Invoke("stuck", "hang");
        var error = await host.WaitForAsync(m => m.Type == MessageTypes.Log && m.Level == "error");
        Assert.Contains("longer than 2 seconds", error.Text, StringComparison.Ordinal);

        host.Invoke("stuck", "ok");
        Assert.Equal("still here", (await host.WaitForAsync(MessageTypes.Status, "stuck")).Text);
    }

    [Fact]
    public async Task Repeated_errors_stop_the_plugin()
    {
        await using var host = new HostHarness();
        host.LoadScript("faulty", "resonate.ui.addCommand('boom', 'Boom', () => { throw new Error('nope'); });");
        await host.WaitForAsync(MessageTypes.Loaded, "faulty");

        for (var i = 0; i < PluginHost.PluginInstance.ErrorLimit; i++)
        {
            host.Invoke("faulty", "boom");
        }

        var failed = await host.WaitForAsync(MessageTypes.Failed, "faulty");
        Assert.Contains("Error: nope", failed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plugins_have_no_dotnet_eval_or_host_access()
    {
        await using var host = new HostHarness();
        const string script = """
            const found = [];
            if (typeof System !== 'undefined') found.push('System');
            if (typeof importNamespace !== 'undefined') found.push('importNamespace');
            if (typeof __host !== 'undefined') found.push('__host');
            if (typeof require !== 'undefined') found.push('require');
            if (typeof fetch !== 'undefined') found.push('fetch');
            try { eval('1'); found.push('eval'); } catch (e) { }
            try { new Function('return 1')(); found.push('Function'); } catch (e) { }
            resonate.ui.setStatus(found.length === 0 ? 'sealed' : found.join(','));
            """;
        host.LoadScript("probe", script);
        Assert.Equal("sealed", (await host.WaitForAsync(MessageTypes.Status, "probe")).Text);
    }

    [Fact]
    public async Task Settings_and_storage_go_through_Resonate()
    {
        await using var host = new HostHarness();
        const string script = """
            resonate.settings.onChange(all => resonate.ui.setStatus('minutes ' + all.minutes));
            resonate.ui.addCommand('more', 'More', () => resonate.settings.set('minutes', resonate.settings.get('minutes') + 5));
            resonate.ui.addCommand('keep', 'Keep', () => resonate.storage.set('count', (resonate.storage.get('count') || 0) + 1));
            resonate.ui.addCommand('unknown', 'Unknown', () => resonate.settings.set('nope', 1));
            resonate.ui.addCommand('huge', 'Huge', () => resonate.storage.set('big', 'x'.repeat(70000)));
            """;
        host.LoadScript("prefs", script, settings: new JsonObject { ["minutes"] = 30 }, storage: new JsonObject { ["count"] = 2 });
        await host.WaitForAsync(MessageTypes.Loaded, "prefs");

        host.Invoke("prefs", "more");
        var setting = await host.WaitForAsync(MessageTypes.Setting, "prefs");
        Assert.Equal("minutes", setting.Key);
        Assert.Equal(35, setting.Data!.GetValue<double>());

        host.App.Send(new HostMessage { Type = MessageTypes.Settings, Plugin = "prefs", Settings = new JsonObject { ["minutes"] = 40 } });
        Assert.Equal("minutes 40", (await host.WaitForAsync(MessageTypes.Status, "prefs")).Text);

        host.Invoke("prefs", "keep");
        var storage = await host.WaitForAsync(MessageTypes.Storage, "prefs");
        Assert.Equal(3, storage.Storage!["count"]!.GetValue<double>());

        host.Invoke("prefs", "unknown");
        Assert.Contains("no setting \"nope\"", (await host.WaitForAsync(m => m.Level == "error")).Text, StringComparison.Ordinal);

        host.Invoke("prefs", "huge");
        Assert.Contains("64 KB", (await host.WaitForAsync(m => m.Level == "error")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unloading_stops_the_plugins_timers()
    {
        await using var host = new HostHarness();
        host.LoadScript("ticker", "setInterval(() => resonate.ui.setStatus('tick'), 1000);");
        await host.WaitForAsync(MessageTypes.Loaded, "ticker");
        await host.AdvanceAsync(TimeSpan.FromSeconds(1));
        await host.WaitForAsync(MessageTypes.Status, "ticker");

        host.App.Send(new HostMessage { Type = MessageTypes.Unload, Plugin = "ticker" });
        await host.DrainAsync();
        await host.AdvanceAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(await host.DrainAsync(), m => m.Type == MessageTypes.Status);
    }
}

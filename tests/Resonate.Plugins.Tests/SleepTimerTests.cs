using System.Text.Json.Nodes;
using Resonate.Plugins.Protocol;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>The Sleep timer plugin in plugins/sleep-timer, run in the real helper loop on a fake clock.</summary>
public sealed class SleepTimerTests
{
    private static readonly NowPlaying Playing = new()
    {
        Title = "Weightless",
        Artists = "Marconi Union",
        Uri = "spotify:track:w",
        IsPlaying = true,
        Position = 100,
        Duration = 480,
        Volume = 0.8,
        CanSeek = true,
    };

    private static async Task<HostHarness> StartAsync(JsonObject? settings = null)
    {
        var host = new HostHarness();
        host.LoadPlugin(HostHarness.RepositoryPlugin("sleep-timer"), settings);
        await host.WaitForAsync(MessageTypes.Loaded, "sleep-timer");
        host.SendState(Playing);
        await host.DrainAsync();
        return host;
    }

    private static List<double> Volumes(IEnumerable<HostMessage> messages) =>
        messages.Where(m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Volume).Select(m => m.Value!.Value).ToList();

    [Fact]
    public async Task Offers_the_usual_lengths_and_the_users_own()
    {
        await using var host = new HostHarness();
        host.LoadPlugin(HostHarness.RepositoryPlugin("sleep-timer"));
        var commands = await host.WaitForAsync(MessageTypes.Commands, "sleep-timer");
        Assert.Equal(
            ["Pause in 15 minutes", "Pause in 30 minutes", "Pause in 1 hour", "Pause in 90 minutes", "Pause at the end of this song"],
            commands.Commands!.Select(c => c.Title));
    }

    [Fact]
    public async Task Fades_out_pauses_and_puts_the_volume_back()
    {
        await using var host = await StartAsync();
        host.Invoke("sleep-timer", "in-15");
        Assert.Equal("Pauses in 15 min", (await host.WaitForAsync(MessageTypes.Status, "sleep-timer")).Text);

        // The fade starts with a minute to go.
        await host.AdvanceAsync(TimeSpan.FromSeconds((14 * 60) - 5), TimeSpan.FromSeconds(30));
        Assert.Empty(Volumes(await host.DrainAsync()));

        await host.AdvanceAsync(TimeSpan.FromSeconds(64));
        var fade = Volumes(await host.DrainAsync());
        Assert.True(fade.Count >= 20, $"Only {fade.Count} fade steps.");
        Assert.True(fade.SequenceEqual(fade.OrderByDescending(v => v)), "The fade should only get quieter.");
        Assert.InRange(fade[0], 0.75, 0.8);
        Assert.InRange(fade[^1], 0, 0.1);

        await host.AdvanceAsync(TimeSpan.FromSeconds(1));
        var end = await host.DrainAsync();
        Assert.Contains(end, m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Pause);
        Assert.Contains(end, m => m.Type == MessageTypes.Status && string.IsNullOrEmpty(m.Text));

        await host.AdvanceAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([0.8], Volumes(await host.DrainAsync()));
    }

    [Fact]
    public async Task Without_the_fade_it_only_pauses()
    {
        await using var host = await StartAsync(new JsonObject { ["fade"] = false });
        host.Invoke("sleep-timer", "in-15");
        await host.WaitForAsync(MessageTypes.Status, "sleep-timer");
        await host.AdvanceAsync(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(10));
        await host.DrainAsync();
        Assert.Empty(Volumes(host.Seen));
        Assert.Contains(host.Seen, m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Pause);
    }

    [Fact]
    public async Task Pauses_at_the_end_of_the_song()
    {
        await using var host = await StartAsync(new JsonObject { ["fade"] = false });
        host.Invoke("sleep-timer", "end-of-song");
        Assert.Equal("Pauses at the end of this song", (await host.WaitForAsync(MessageTypes.Status, "sleep-timer")).Text);

        // 380 seconds are left; a seek moves the end closer.
        host.SendState(Playing with { Position = 470 });
        await host.DrainAsync();
        await host.AdvanceAsync(TimeSpan.FromSeconds(9));
        Assert.DoesNotContain(await host.DrainAsync(), m => m.Action == PlayerActions.Pause);
        await host.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Contains(await host.DrainAsync(), m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Pause);
    }

    [Fact]
    public async Task Cancelling_brings_back_the_choices()
    {
        await using var host = await StartAsync();
        host.Invoke("sleep-timer", "in-30");
        var active = await host.WaitForAsync(m => m.Type == MessageTypes.Commands && m.Commands!.Exists(c => c.Id == "cancel"));
        Assert.Equal(["Add 15 minutes", "Cancel the sleep timer"], active.Commands!.Select(c => c.Title));

        host.Invoke("sleep-timer", "cancel");
        await host.WaitForAsync(m => m.Type == MessageTypes.Commands && m.Commands!.Exists(c => c.Id == "in-30"));
        await host.AdvanceAsync(TimeSpan.FromMinutes(31), TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(await host.DrainAsync(), m => m.Action == PlayerActions.Pause);
    }
}

using System.Text.Json.Nodes;
using Resonate.Plugins.Protocol;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>The Skip rules plugin in plugins/skip-rules, run in the real helper loop.</summary>
public sealed class SkipRulesTests
{
    private static NowPlaying Track(string title, string artists = "Someone", double duration = 200, bool playing = true, string? uri = null) => new()
    {
        Title = title,
        Artists = artists,
        Uri = uri ?? "spotify:track:" + title.GetHashCode(StringComparison.Ordinal),
        IsPlaying = playing,
        Duration = duration,
        Volume = 1,
        CanSeek = true,
    };

    private static async Task<HostHarness> StartAsync(JsonObject settings)
    {
        var host = new HostHarness();
        host.LoadPlugin(HostHarness.RepositoryPlugin("skip-rules"), settings);
        await host.WaitForAsync(MessageTypes.Loaded, "skip-rules");
        return host;
    }

    private static async Task<bool> SkipsAsync(HostHarness host, NowPlaying track)
    {
        host.SendState(track);
        return (await host.DrainAsync()).Exists(m => m.Type == MessageTypes.Player && m.Action == PlayerActions.Next);
    }

    [Fact]
    public async Task Skips_blocked_artists_even_among_several()
    {
        await using var host = await StartAsync(new JsonObject { ["artists"] = new JsonArray("Drake") });
        Assert.True(await SkipsAsync(host, Track("One", "Future, Drake")));
        Assert.False(await SkipsAsync(host, Track("Two", "Drakeo the Ruler")));
        Assert.True(await SkipsAsync(host, Track("Three", "drake")));
    }

    [Fact]
    public async Task Skips_versions_named_in_brackets_or_after_a_dash_only()
    {
        await using var host = await StartAsync(new JsonObject { ["versions"] = new JsonArray("live", "sped up") });
        Assert.True(await SkipsAsync(host, Track("Song (Live)")));
        Assert.True(await SkipsAsync(host, Track("Song - Live at Wembley")));
        Assert.True(await SkipsAsync(host, Track("Song [Sped Up]")));
        Assert.False(await SkipsAsync(host, Track("Live Forever")));
        Assert.False(await SkipsAsync(host, Track("Song (Alive Mix)")));
        Assert.False(await SkipsAsync(host, Track("Song - Remastered 2011")));
    }

    [Fact]
    public async Task Skips_by_length_and_saved_songs()
    {
        await using var host = await StartAsync(new JsonObject
        {
            ["longerThan"] = 8,
            ["shorterThan"] = 30,
            ["songs"] = new JsonArray("Bad Song — The Band"),
        });
        Assert.True(await SkipsAsync(host, Track("Epic", duration: 9 * 60)));
        Assert.True(await SkipsAsync(host, Track("Skit", duration: 12)));
        Assert.False(await SkipsAsync(host, Track("Normal", duration: 200)));
        Assert.True(await SkipsAsync(host, Track("bad song", "the band")));
    }

    [Fact]
    public async Task Does_not_skip_while_paused_but_does_once_it_plays()
    {
        await using var host = await StartAsync(new JsonObject { ["artists"] = new JsonArray("Drake") });
        var paused = Track("One", "Drake", playing: false);
        Assert.False(await SkipsAsync(host, paused));
        Assert.True(await SkipsAsync(host, paused with { IsPlaying = true }));
    }

    [Fact]
    public async Task Offers_to_always_skip_the_song_or_each_artist()
    {
        await using var host = await StartAsync(new JsonObject());
        host.SendState(Track("Hit", "Ana, Ben"));
        var commands = await host.WaitForAsync(m => m.Type == MessageTypes.Commands && m.Commands!.Count == 3);
        Assert.Equal(["Always skip this song", "Always skip Ana", "Always skip Ben"], commands.Commands!.Select(c => c.Title));

        host.Invoke("skip-rules", "artist-1");
        var setting = await host.WaitForAsync(MessageTypes.Setting, "skip-rules");
        Assert.Equal("artists", setting.Key);
        Assert.Equal("[\"Ben\"]", setting.Data!.ToJsonString());
        var next = await host.WaitForAsync(MessageTypes.Player, "skip-rules");
        Assert.Equal(PlayerActions.Next, next.Action);
    }

    [Fact]
    public async Task Stops_for_a_minute_when_every_song_matches()
    {
        await using var host = await StartAsync(new JsonObject { ["versions"] = new JsonArray("live") });
        var skips = 0;
        for (var i = 0; i < 10; i++)
        {
            if (await SkipsAsync(host, Track($"Song {i} (Live)")))
            {
                skips++;
            }
        }

        Assert.Equal(8, skips);
        Assert.Contains(host.Seen, m => m.Type == MessageTypes.Notify);

        await host.AdvanceAsync(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(5));
        Assert.True(await SkipsAsync(host, Track("Later (Live)")));
    }
}

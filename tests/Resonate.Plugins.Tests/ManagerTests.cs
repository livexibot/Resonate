using System.IO.Pipes;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Resonate.Plugins.Installing;
using Resonate.Plugins.Protocol;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>Turning plugins on and off, and what Resonate lets them do.</summary>
public sealed class ManagerTests : IDisposable
{
    private static readonly NowPlaying LiveSong = new()
    {
        Title = "Song (Live)",
        Artists = "Band",
        Uri = "spotify:track:live",
        IsPlaying = true,
        Duration = 200,
        Volume = 1,
    };

    private readonly Packages _packages = new();
    private readonly FakePluginPlayer _player = new();
    private readonly InProcessLauncher _launcher = new();

    public void Dispose() => _packages.Dispose();

    private PluginManager Manager(PluginStateStore? store = null, IPluginHostLauncher? launcher = null, TimeProvider? time = null) => new(
        _packages.Catalog,
        new PluginInstaller(_packages.Installed, new FolderPluginFeed(_packages.Feed)),
        store ?? new PluginStateStore(null),
        launcher ?? _launcher,
        _player,
        time);

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting until " + what);
    }

    [Fact]
    public async Task Turning_a_plugin_on_downloads_it_and_the_helper_then_runs_it()
    {
        using var manager = Manager();
        var size = manager.DownloadSize("skip-rules");
        Assert.Equal(_packages.Catalog.Host!.Size + _packages.Catalog.Find("skip-rules")!.Package.Size, size);
        Assert.False(Directory.Exists(_packages.Installed));

        Assert.True(await manager.EnableAsync("skip-rules", CancellationToken.None));
        await WaitUntilAsync(() => manager.Get("skip-rules")!.Status == PluginStatus.Running, "it runs");
        Assert.Equal(_packages.Catalog.Find("sleep-timer")!.Package.Size, manager.DownloadSize("sleep-timer"));

        manager.SetSetting("skip-rules", "versions", new JsonArray("live"));
        _player.Show(LiveSong);
        await WaitUntilAsync(() => _player.Calls.Contains("next"), "it skips");
    }

    [Fact]
    public async Task Turning_the_last_plugin_off_stops_the_helper_and_deletes_every_file()
    {
        using var manager = Manager();
        await manager.EnableAsync("skip-rules", CancellationToken.None);
        await manager.EnableAsync("sleep-timer", CancellationToken.None);
        await WaitUntilAsync(() => manager.Get("sleep-timer")!.Commands.Count > 0, "the sleep timer offers commands");
        Assert.Single(manager.WithCommands());

        await manager.DisableAsync("sleep-timer");
        Assert.False(_launcher.Current!.IsDisposed);
        Assert.False(Directory.Exists(Path.Combine(_packages.Installed, "plugin-sleep-timer")));
        Assert.Empty(manager.WithCommands());

        await manager.DisableAsync("skip-rules");
        Assert.True(_launcher.Current.IsDisposed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_packages.Installed));
        Assert.Equal(PluginStatus.Off, manager.Get("skip-rules")!.Status);
    }

    [Fact]
    public async Task A_download_that_does_not_match_leaves_the_plugin_off_with_a_reason()
    {
        File.WriteAllText(Path.Combine(_packages.Feed, _packages.Catalog.Find("sleep-timer")!.Package.File), "tampered");
        var store = new PluginStateStore(null);
        using var manager = Manager(store);

        Assert.False(await manager.EnableAsync("sleep-timer", CancellationToken.None));
        var view = manager.Get("sleep-timer")!;
        Assert.Equal(PluginStatus.Off, view.Status);
        Assert.Contains("not the expected file", view.Error, StringComparison.Ordinal);
        Assert.False(store.IsEnabled("sleep-timer"));
        Assert.Empty(_launcher.StartedFolders);
    }

    [Fact]
    public async Task Turning_a_plugin_off_while_it_downloads_stops_the_download()
    {
        var feed = new StalledFeed();
        var store = new PluginStateStore(null);
        using var manager = new PluginManager(_packages.Catalog, new PluginInstaller(_packages.Installed, feed), store, _launcher, _player);

        var enabling = manager.EnableAsync("sleep-timer", CancellationToken.None);
        await feed.Opened.Task;
        Assert.Equal(PluginStatus.Downloading, manager.Get("sleep-timer")!.Status);

        await manager.DisableAsync("sleep-timer").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await enabling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(PluginStatus.Off, manager.Get("sleep-timer")!.Status);
        Assert.False(store.IsEnabled("sleep-timer"));
        Assert.Empty(_launcher.StartedFolders);
    }

    [Fact]
    public async Task Settings_are_checked_kept_and_passed_to_the_plugin()
    {
        var path = Path.Combine(_packages.Root, "plugins.json");
        using (var manager = Manager(new PluginStateStore(path)))
        {
            await manager.EnableAsync("sleep-timer", CancellationToken.None);
            manager.SetSetting("sleep-timer", "customMinutes", 1000);
            manager.SetSetting("sleep-timer", "unknown", 1);
            Assert.Equal(240, manager.Get("sleep-timer")!.Settings["customMinutes"]!.GetValue<double>());
            await WaitUntilAsync(() => manager.Get("sleep-timer")!.Commands.Any(c => c.Title == "Pause in 4 hours"), "the plugin sees the new length");
        }

        var reloaded = new PluginStateStore(path);
        Assert.True(reloaded.IsEnabled("sleep-timer"));
        Assert.Equal("{\"customMinutes\":240}", reloaded.GetSettings("sleep-timer")!.ToJsonString());
    }

    [Fact]
    public async Task On_start_plugins_that_were_on_come_back()
    {
        var path = Path.Combine(_packages.Root, "plugins.json");
        var store = new PluginStateStore(path);
        store.SetEnabled("skip-rules", true);
        store.SetEnabled("retired-plugin", true);

        using var manager = Manager(new PluginStateStore(path));
        await manager.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => manager.Get("skip-rules")!.Status == PluginStatus.Running, "it runs");
        Assert.False(new PluginStateStore(path).IsEnabled("retired-plugin"));
    }

    [Fact]
    public async Task A_crashed_helper_is_started_again()
    {
        using var manager = Manager();
        await manager.EnableAsync("sleep-timer", CancellationToken.None);
        await WaitUntilAsync(() => manager.Get("sleep-timer")!.Status == PluginStatus.Running, "it runs");

        _launcher.Current!.Crash();
        await WaitUntilAsync(() => _launcher.StartedFolders.Count == 2, "the helper restarts");
        await WaitUntilAsync(() => manager.Get("sleep-timer")!.Status == PluginStatus.Running, "it runs again");
    }

    [Fact]
    public async Task Resonate_enforces_permissions_and_rate_limits_itself()
    {
        var time = new FakeTimeProvider();
        var rogue = new RogueLauncher();
        using var manager = Manager(launcher: rogue, time: time);
        await manager.EnableAsync("skip-rules", CancellationToken.None);
        await rogue.ConnectedAsync();
        rogue.Send(new HostMessage { Type = MessageTypes.Loaded, Plugin = "skip-rules" });
        await WaitUntilAsync(() => manager.Get("skip-rules")!.Status == PluginStatus.Running, "it runs");

        // Skip rules may control playback but not the volume, whatever the helper claims.
        rogue.Send(new HostMessage { Type = MessageTypes.Player, Plugin = "skip-rules", Action = PlayerActions.Volume, Value = 1 });
        for (var i = 0; i < 20; i++)
        {
            rogue.Send(new HostMessage { Type = MessageTypes.Player, Plugin = "skip-rules", Action = PlayerActions.Next });
        }

        // Messages for a plugin that is off are ignored.
        rogue.Send(new HostMessage { Type = MessageTypes.Player, Plugin = "sleep-timer", Action = PlayerActions.Pause });
        rogue.Send(new HostMessage { Type = MessageTypes.Status, Plugin = "skip-rules", Text = "done" });
        await WaitUntilAsync(() => manager.Get("skip-rules")!.StatusText == "done", "all messages are handled");

        Assert.Equal(PluginManager.TransportLimit, _player.Calls.Count);
        Assert.All(_player.Calls, call => Assert.Equal("next", call));
        Assert.Contains("too many", manager.Get("skip-rules")!.Error, StringComparison.Ordinal);

        time.Advance(PluginManager.TransportWindow);
        rogue.Send(new HostMessage { Type = MessageTypes.Player, Plugin = "skip-rules", Action = PlayerActions.Next });
        await WaitUntilAsync(() => _player.Calls.Count == PluginManager.TransportLimit + 1, "the limit resets");
    }

    [Fact]
    public async Task Only_real_changes_of_what_plays_are_sent()
    {
        var time = new FakeTimeProvider();
        var rogue = new RogueLauncher();
        using var manager = Manager(launcher: rogue, time: time);
        await manager.EnableAsync("skip-rules", CancellationToken.None);
        await rogue.ConnectedAsync();

        _player.Show(LiveSong with { Position = 10 });
        time.Advance(TimeSpan.FromSeconds(5));
        _player.Show(LiveSong with { Position = 15.2 });
        _player.Show(LiveSong with { Position = 60 });
        _player.Show(LiveSong with { Position = 60, IsPlaying = false });

        var states = await rogue.ReceivedAsync(m => m.Type == MessageTypes.State && m.State is not null, 3);
        Assert.Equal([10, 60, 60], states.Select(m => m.State!.Position));
        Assert.False(states[^1].State!.IsPlaying);
    }

    [Fact]
    public async Task A_preview_shows_settings_without_downloading()
    {
        var store = new PluginStateStore(null);
        using var manager = new PluginManager(_packages.Catalog, installer: null, store, launcher: null, _player);
        Assert.True(manager.IsPreview);
        Assert.True(await manager.EnableAsync("skip-rules", CancellationToken.None));
        Assert.Equal(PluginStatus.Running, manager.Get("skip-rules")!.Status);
        Assert.False(Directory.Exists(_packages.Installed));
        await manager.DisableAsync("skip-rules");
        Assert.Equal(PluginStatus.Off, manager.Get("skip-rules")!.Status);
    }

    /// <summary>A download that never gets anywhere until it is cancelled.</summary>
    private sealed class StalledFeed : IPluginFeed
    {
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(Stream Content, long? Length)> OpenAsync(string file, CancellationToken cancellationToken)
        {
            Opened.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Not reached.");
        }
    }

    /// <summary>A helper the test speaks for, to send what a real one never would.</summary>
    private sealed class RogueLauncher : IPluginHostLauncher
    {
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<HostMessage> _received = [];
        private MessageChannel? _helperSide;

        public IPluginHostProcess Start(string hostFolder)
        {
            var toHost = new AnonymousPipeServerStream(PipeDirection.Out);
            var fromHost = new AnonymousPipeServerStream(PipeDirection.In);
            _helperSide = new MessageChannel(
                new AnonymousPipeClientStream(PipeDirection.In, toHost.ClientSafePipeHandle),
                new AnonymousPipeClientStream(PipeDirection.Out, fromHost.ClientSafePipeHandle));
            _ = Task.Run(async () =>
            {
                await foreach (var message in _helperSide.ReadAllAsync(CancellationToken.None))
                {
                    lock (_received)
                    {
                        _received.Add(message);
                    }
                }
            });
            _helperSide.Send(new HostMessage { Type = MessageTypes.Ready });
            _connected.TrySetResult();
            return new Process(new MessageChannel(fromHost, toHost));
        }

        public async Task ConnectedAsync()
        {
            await _connected.Task;
            await Task.Delay(200);
        }

        public void Send(HostMessage message) => _helperSide!.Send(message);

        public async Task<List<HostMessage>> ReceivedAsync(Func<HostMessage, bool> match, int count)
        {
            for (var i = 0; i < 200; i++)
            {
                lock (_received)
                {
                    var found = _received.Where(match).ToList();
                    if (found.Count >= count)
                    {
                        return found;
                    }
                }

                await Task.Delay(25);
            }

            lock (_received)
            {
                return _received.Where(match).ToList();
            }
        }

        private sealed class Process(MessageChannel channel) : IPluginHostProcess
        {
            public MessageChannel Channel { get; } = channel;

            public long? MemoryBytes => null;

            public string? CrashReport => null;

            public void Dispose() => Channel.Dispose();
        }
    }
}

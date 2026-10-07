using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Resonate.Plugins.Installing;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins;

/// <summary>
/// Proves the whole plugin path on a real computer: installs the helper and
/// every plugin from a folder (checked against the catalog), starts them,
/// and has Skip rules skip a song on a pretend player. CI runs it through
/// the installed app ("--plugin-check"), so a plugin that breaks under
/// Native AOT on Windows shows up in a pull request.
/// </summary>
public static class PluginSelfTest
{
    /// <summary>"OK …" with what was checked, or what went wrong.</summary>
    public static async Task<string> RunAsync(PluginCatalog catalog, string feedFolder, string workFolder, IPluginHostLauncher launcher, TimeSpan timeout)
    {
        if (catalog.Host is null || catalog.Plugins.Count == 0)
        {
            return "FAILED this build has no plugin catalog";
        }

        using var cancel = new CancellationTokenSource(timeout);
        var player = new PretendPlayer();
        var installer = new PluginInstaller(workFolder, new FolderPluginFeed(feedFolder));
        using var manager = new PluginManager(catalog, installer, new PluginStateStore(null), launcher, player);
        try
        {
            // From turning the first plugin on to it running: download (here from a folder), checks, and starting the helper.
            var clock = Stopwatch.StartNew();
            long? firstRunning = null;
            foreach (var plugin in catalog.Plugins)
            {
                var id = plugin.Manifest.Id;
                if (!await manager.EnableAsync(id, cancel.Token).ConfigureAwait(false))
                {
                    return $"FAILED {id} did not install: {manager.Get(id)?.Error}";
                }

                if (!await WaitAsync(() => manager.Get(id)?.Status is PluginStatus.Running or PluginStatus.Failed, cancel.Token).ConfigureAwait(false)
                    || manager.Get(id)?.Status != PluginStatus.Running)
                {
                    return $"FAILED {id} did not start: {manager.Get(id)?.Error ?? "timed out"}";
                }

                firstRunning ??= clock.ElapsedMilliseconds;
            }

            var checks = new List<string>();
            if (catalog.Find("skip-rules") is not null)
            {
                manager.SetSetting("skip-rules", "versions", new JsonArray("live"));
                player.Play(new NowPlaying { Title = "Self-test (Live)", Artists = "Resonate", Uri = "spotify:track:selftest", IsPlaying = true, Duration = 200, Volume = 1 });
                if (!await WaitAsync(() => player.Skips > 0, cancel.Token).ConfigureAwait(false))
                {
                    return "FAILED skip-rules did not skip a matching song" + Problem(manager, "skip-rules");
                }

                checks.Add("skip-rules skipped");
            }

            if (catalog.Find("sleep-timer") is not null)
            {
                if (!await WaitAsync(() => manager.Get("sleep-timer")?.Commands.Count > 0, cancel.Token).ConfigureAwait(false))
                {
                    return "FAILED sleep-timer offered no commands" + Problem(manager, "sleep-timer");
                }

                checks.Add("sleep-timer ready");
            }

            var memory = manager.HostMemoryBytes is { } bytes
                ? (bytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture) + " MB"
                : "unknown";

            foreach (var plugin in catalog.Plugins)
            {
                await manager.DisableAsync(plugin.Manifest.Id).ConfigureAwait(false);
            }

            if (Directory.Exists(workFolder) && Directory.EnumerateFileSystemEntries(workFolder).Any())
            {
                return "FAILED files were left behind after turning every plugin off";
            }

            return $"OK {catalog.Plugins.Count} plugins started; {string.Join(", ", checks)}; helper memory {memory}; first plugin running after {firstRunning} ms";
        }
        catch (OperationCanceledException)
        {
            return "FAILED timed out";
        }
    }

    private static string Problem(PluginManager manager, string id) =>
        manager.Get(id)?.Error is { } error ? ": " + error : string.Empty;

    private static async Task<bool> WaitAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return condition();
            }
        }

        return true;
    }

    /// <summary>A player that only counts what plugins ask of it.</summary>
    private sealed class PretendPlayer : IPluginPlayer
    {
        private int _skips;

        public event EventHandler? StateChanged;

        public NowPlaying? Current { get; private set; }

        public int Skips => Volatile.Read(ref _skips);

        public void Play(NowPlaying state)
        {
            Current = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task PlayAsync() => Task.CompletedTask;

        public Task PauseAsync() => Task.CompletedTask;

        public Task NextAsync()
        {
            Interlocked.Increment(ref _skips);
            return Task.CompletedTask;
        }

        public Task PreviousAsync() => Task.CompletedTask;

        public Task SeekAsync(TimeSpan position) => Task.CompletedTask;

        public Task SetVolumeAsync(double volume) => Task.CompletedTask;
    }
}

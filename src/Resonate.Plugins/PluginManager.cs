using System.Text.Json.Nodes;
using Resonate.Plugins.Installing;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins;

public enum PluginStatus
{
    /// <summary>Turned off; nothing is downloaded.</summary>
    Off,

    Downloading,

    /// <summary>Downloaded; waiting for the helper to start it.</summary>
    Starting,

    Running,

    /// <summary>On, but it could not start or stopped after errors.</summary>
    Failed,
}

/// <summary>A plugin as Settings and the player bar show it. A snapshot; ask again after <see cref="PluginManager.Changed"/>.</summary>
public sealed record PluginView(
    PluginManifest Manifest,
    PluginStatus Status,
    double Progress,
    string? StatusText,
    string? Error,
    IReadOnlyList<PluginCommand> Commands,
    JsonObject Settings)
{
    public bool IsOn => Status is not PluginStatus.Off;
}

/// <summary>Something a plugin wants the user to know now.</summary>
public sealed record PluginNotification(string PluginName, string Text);

/// <summary>
/// Turns plugins on and off. Turning one on downloads it (and, the first
/// time, the helper that runs plugins), checks both against the app's
/// catalog, and starts it; turning it off stops it and deletes its files.
/// The helper runs only while at least one plugin is on. Everything a
/// plugin asks for is checked here against its permissions and rate
/// limits, so a misbehaving plugin can not flood the player or the user.
/// </summary>
public sealed class PluginManager : IDisposable
{
    public const string HostFolderName = "host";
    public const int MaxCommands = 20;
    public const int MaxTitleLength = 60;
    public const int MaxStatusLength = 120;
    public const int MaxNotifyLength = 240;

    internal static readonly TimeSpan TransportWindow = TimeSpan.FromSeconds(30);
    internal const int TransportLimit = 12;
    internal static readonly TimeSpan VolumeWindow = TimeSpan.FromSeconds(1);
    internal const int VolumeLimit = 5;
    internal static readonly TimeSpan NotifyWindow = TimeSpan.FromSeconds(10);
    internal const int NotifyLimit = 2;
    internal static readonly TimeSpan PositionTolerance = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    internal const int MaxRestarts = 2;

    /// <summary>Far past any song's end, and well inside what a TimeSpan holds.</summary>
    internal const double MaxSeekSeconds = 1e7;

    private readonly PluginCatalog _catalog;
    private readonly PluginInstaller? _installer;
    private readonly PluginStateStore _store;
    private readonly IPluginHostLauncher? _launcher;
    private readonly IPluginPlayer _player;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly Dictionary<string, Runtime> _runtimes = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<DateTimeOffset> _restarts = [];

    private IPluginHostProcess? _host;
    private bool _hostReady;
    private long _hostGeneration;
    private NowPlaying? _lastSent;
    private long _lastSentTimestamp;
    private bool _disposed;

    /// <param name="installer">Null for a preview (demo mode): plugins can be turned on to see their settings, but nothing downloads or runs.</param>
    /// <param name="launcher">Null for a preview.</param>
    public PluginManager(
        PluginCatalog catalog,
        PluginInstaller? installer,
        PluginStateStore store,
        IPluginHostLauncher? launcher,
        IPluginPlayer player,
        TimeProvider? time = null)
    {
        _catalog = catalog;
        _installer = installer;
        _store = store;
        _launcher = launcher;
        _player = player;
        _time = time ?? TimeProvider.System;

        foreach (var plugin in catalog.Plugins)
        {
            _runtimes[plugin.Manifest.Id] = new Runtime(plugin);
        }

        player.StateChanged += OnPlayerStateChanged;
    }

    /// <summary>Raised on any thread with the ID of a plugin whose <see cref="Get"/> changed.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Raised on any thread when a plugin has something to tell the user.</summary>
    public event EventHandler<PluginNotification>? Notified;

    /// <summary>Nothing downloads or runs (demo mode).</summary>
    public bool IsPreview => _installer is null || _launcher is null;

    /// <summary>The plugins this release offers, in the catalog's order.</summary>
    public IReadOnlyList<PluginManifest> Available => _catalog.Plugins.Select(p => p.Manifest).ToList();

    /// <summary>The helper's memory in use while it runs (for CI's report).</summary>
    public long? HostMemoryBytes
    {
        get
        {
            lock (_gate)
            {
                return _host?.MemoryBytes;
            }
        }
    }

    public PluginView? Get(string id)
    {
        lock (_gate)
        {
            return _runtimes.TryGetValue(id, out var runtime) ? ViewLocked(runtime) : null;
        }
    }

    /// <summary>Every plugin that is on and offers commands, for the player bar.</summary>
    public IReadOnlyList<PluginView> WithCommands()
    {
        lock (_gate)
        {
            return _runtimes.Values
                .Where(r => r.Status == PluginStatus.Running && r.Commands.Count > 0)
                .Select(ViewLocked)
                .ToList();
        }
    }

    /// <summary>
    /// At start-up: tidies left-over files, downloads plugins that are on but
    /// missing (after Resonate updated, each release brings new copies), and
    /// starts the helper. Runs in the background; nothing waits for it.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = _store.EnabledPlugins;
        foreach (var id in enabled)
        {
            if (_catalog.Find(id) is null)
            {
                // No longer offered by this release.
                _store.SetEnabled(id, false);
            }
        }

        enabled = _store.EnabledPlugins;
        if (IsPreview)
        {
            foreach (var id in enabled)
            {
                SetPreviewOn(id);
            }

            return;
        }

        _installer!.RemoveAllExcept(enabled.Count == 0 ? [] : [HostFolderName, .. enabled.Select(PackageName)]);
        foreach (var id in enabled)
        {
            await InstallAndStartAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Downloads and starts a plugin. False (with the reason in <see cref="PluginView.Error"/>) when the download failed.</summary>
    public async Task<bool> EnableAsync(string id, CancellationToken cancellationToken)
    {
        if (_catalog.Find(id) is null)
        {
            return false;
        }

        if (IsPreview)
        {
            _store.SetEnabled(id, true);
            SetPreviewOn(id);
            return true;
        }

        _store.SetEnabled(id, true);
        var started = await InstallAndStartAsync(id, cancellationToken).ConfigureAwait(false);
        if (!started)
        {
            // A plugin that never arrived stays off, so nothing is left half-installed.
            _store.SetEnabled(id, false);
            Update(id, r =>
            {
                r.Status = PluginStatus.Off;
                r.Progress = 0;
            });
        }

        return started;
    }

    /// <summary>Stops a plugin and deletes its files (its settings are kept for next time).</summary>
    public async Task DisableAsync(string id)
    {
        _store.SetEnabled(id, false);
        IPluginHostProcess? stopping = null;
        CancellationTokenSource? download;
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(id, out var runtime))
            {
                return;
            }

            download = runtime.Download;
            runtime.Download = null;
            runtime.Reset();
            runtime.Error = null;
            if (_host is not null && !_store.EnabledPlugins.Any())
            {
                // The last one: the helper is not needed any more.
                stopping = _host;
                _host = null;
                _hostReady = false;
                _hostGeneration++;
            }
        }

        RaiseChanged(id);
        if (IsPreview)
        {
            return;
        }

        try
        {
            // A download still running stops here, so it never holds up turning the plugin off.
            download?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It had just finished.
        }

        if (stopping is null)
        {
            Send(new HostMessage { Type = MessageTypes.Unload, Plugin = id });
        }

        await _installGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopping is not null)
            {
                await Task.Run(stopping.Dispose).ConfigureAwait(false);

                // Windows can hold on to a program's file for a moment after it exits.
                for (var attempt = 0; attempt < 20 && !_store.EnabledPlugins.Any() && !_installer!.Remove(HostFolderName); attempt++)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }

            if (!_store.IsEnabled(id))
            {
                _installer!.Remove(PackageName(id));
            }
        }
        finally
        {
            _installGate.Release();
        }
    }

    /// <summary>The user changed a setting in Settings.</summary>
    public void SetSetting(string id, string key, JsonNode? value)
    {
        var setting = _catalog.Find(id)?.Manifest.FindSetting(key);
        if (setting is null)
        {
            return;
        }

        _store.SetSetting(id, key, setting.Normalize(value));
        SendSettings(id);
        RaiseChanged(id);
    }

    /// <summary>The user picked one of a plugin's commands in the player bar.</summary>
    public void Invoke(string id, string commandId)
    {
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(id, out var runtime) || runtime.Status != PluginStatus.Running || !runtime.Commands.Exists(c => c.Id == commandId))
            {
                return;
            }
        }

        Send(new HostMessage { Type = MessageTypes.Invoke, Plugin = id, Command = commandId });
    }

    public void Dispose()
    {
        IPluginHostProcess? host;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            host = _host;
            _host = null;
            _hostGeneration++;
        }

        _player.StateChanged -= OnPlayerStateChanged;

        // Cancelled but not disposed: downloads and readers still running may look at its token.
        _lifetime.Cancel();
        host?.Dispose();
    }

    internal static string PackageName(string id) => "plugin-" + id;

    private async Task<bool> InstallAndStartAsync(string id, CancellationToken cancellationToken)
    {
        var plugin = _catalog.Find(id)!;
        var host = _catalog.Host;
        if (host is null)
        {
            Update(id, r => r.Error = "This copy of Resonate was built without plugins.");
            return false;
        }

        Update(id, r =>
        {
            r.Status = PluginStatus.Downloading;
            r.Progress = 0;
            r.Error = null;
        });

        // Turning the plugin off cancels the download (see DisableAsync).
        string folder;
        using var download = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lock (_gate)
        {
            _runtimes[id].Download = download;
        }

        try
        {
            await _installGate.WaitAsync(download.Token).ConfigureAwait(false);
            try
            {
                var hostNeeded = !_installer!.IsInstalled(HostFolderName, host);
                var total = (double)plugin.Package.Size + (hostNeeded ? host.Size : 0);
                var done = 0.0;
                if (hostNeeded)
                {
                    await _installer.InstallAsync(HostFolderName, host, Progress(id, done, host.Size, total), download.Token).ConfigureAwait(false);
                    done += host.Size;
                }

                folder = await _installer
                    .InstallAsync(PackageName(id), plugin.Package, Progress(id, done, plugin.Package.Size, total), download.Token)
                    .ConfigureAwait(false);
                _installer.RemoveOtherVersions(HostFolderName, host);
                _installer.RemoveOtherVersions(PackageName(id), plugin.Package);
            }
            finally
            {
                _installGate.Release();
            }
        }
        catch (PluginInstallException ex)
        {
            Update(id, r =>
            {
                r.Status = _store.IsEnabled(id) ? PluginStatus.Failed : PluginStatus.Off;
                r.Error = ex.Message;
            });
            return false;
        }
        catch (OperationCanceledException)
        {
            Update(id, r => r.Status = PluginStatus.Off);
            return false;
        }
        finally
        {
            lock (_gate)
            {
                if (_runtimes[id].Download == download)
                {
                    _runtimes[id].Download = null;
                }
            }
        }

        if (!_store.IsEnabled(id))
        {
            // Turned off while it downloaded.
            return false;
        }

        bool loadNow;
        lock (_gate)
        {
            if (!_store.IsEnabled(id))
            {
                // Turned off just now; DisableAsync has already put it back to Off.
                return false;
            }

            var runtime = _runtimes[id];
            runtime.Folder = folder;
            runtime.Status = PluginStatus.Starting;
            runtime.Progress = 1;
            loadNow = _hostReady;
        }

        RaiseChanged(id);
        if (loadNow)
        {
            SendLoad(id);
        }
        else
        {
            EnsureHost();
        }

        return true;
    }

    private Progress<double> Progress(string id, double done, double size, double total) =>
        new(fraction => Update(id, r => r.Progress = total <= 0 ? 1 : (done + (fraction * size)) / total));

    private void EnsureHost()
    {
        IPluginHostProcess process;
        long generation;
        lock (_gate)
        {
            if (_host is not null || _disposed || _catalog.Host is null)
            {
                return;
            }

            try
            {
                process = _launcher!.Start(_installer!.FolderFor(HostFolderName, _catalog.Host));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                foreach (var runtime in _runtimes.Values.Where(r => r.Status == PluginStatus.Starting))
                {
                    runtime.Status = PluginStatus.Failed;
                    runtime.Error = "The plugin helper could not start. Windows security software may have blocked it.";
                }

                RaiseAllChanged();
                return;
            }

            _host = process;
            _hostReady = false;
            generation = ++_hostGeneration;
        }

        _ = Task.Run(() => ReadAsync(process, generation));
    }

    private async Task ReadAsync(IPluginHostProcess process, long generation)
    {
        try
        {
            await foreach (var message in process.Channel.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _hostGeneration) != generation)
                {
                    return;
                }

                try
                {
                    Handle(message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One odd message must never stop Resonate reading the helper (it would then hang once its pipe fills).
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        OnHostExited(process, generation);
    }

    private void OnHostExited(IPluginHostProcess process, long generation)
    {
        bool restart;
        lock (_gate)
        {
            if (_hostGeneration != generation || _disposed)
            {
                return;
            }

            _host = null;
            _hostReady = false;
            _hostGeneration++;

            var now = _time.GetUtcNow();
            _restarts.RemoveAll(t => now - t > RestartWindow);
            restart = _restarts.Count < MaxRestarts;
            if (restart)
            {
                _restarts.Add(now);
            }

            var report = process.CrashReport;
            foreach (var runtime in _runtimes.Values.Where(r => r.Status is PluginStatus.Running or PluginStatus.Starting))
            {
                runtime.Reset();
                runtime.Status = restart ? PluginStatus.Starting : PluginStatus.Failed;
                runtime.Error = restart
                    ? null
                    : "The plugin helper stopped. Turn the plugin off and on to try again." + (report is null ? string.Empty : "\n" + report);
            }
        }

        process.Dispose();
        RaiseAllChanged();
        if (restart)
        {
            EnsureHost();
        }
    }

    private void Handle(HostMessage message)
    {
        if (message.Type == MessageTypes.Ready)
        {
            List<string> toLoad;
            lock (_gate)
            {
                _hostReady = true;
                _lastSent = null;
                toLoad = _runtimes.Values.Where(r => r.Status == PluginStatus.Starting && r.Folder is not null).Select(r => r.Plugin.Manifest.Id).ToList();
            }

            foreach (var starting in toLoad)
            {
                SendLoad(starting);
            }

            SendState(force: true);
            return;
        }

        if (message.Plugin is not { } id || !_store.IsEnabled(id))
        {
            return;
        }

        Runtime runtime;
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(id, out runtime!) || runtime.Status is PluginStatus.Off or PluginStatus.Downloading)
            {
                return;
            }
        }

        switch (message.Type)
        {
            case MessageTypes.Loaded:
                Update(id, r =>
                {
                    r.Status = PluginStatus.Running;
                    r.Error = null;
                });
                break;

            case MessageTypes.Failed:
                Update(id, r =>
                {
                    r.Reset();
                    r.Status = PluginStatus.Failed;
                    r.Error = Shorten(message.Text, 600) ?? "The plugin stopped.";
                });
                break;

            case MessageTypes.Player:
                HandlePlayer(runtime, message);
                break;

            case MessageTypes.Commands:
                var commands = (message.Commands ?? [])
                    .Where(c => c.Id.Length is > 0 and <= MaxTitleLength && c.Title.Length > 0)
                    .Take(MaxCommands)
                    .Select(c => new PluginCommand { Id = c.Id, Title = Shorten(c.Title, MaxTitleLength)! })
                    .ToList();
                Update(id, r => r.Commands = commands);
                break;

            case MessageTypes.Status:
                Update(id, r => r.StatusText = string.IsNullOrWhiteSpace(message.Text) ? null : Shorten(message.Text, MaxStatusLength));
                break;

            case MessageTypes.Notify:
                if (!string.IsNullOrWhiteSpace(message.Text) && Allow(runtime.Notifications, NotifyWindow, NotifyLimit))
                {
                    Notified?.Invoke(this, new PluginNotification(runtime.Plugin.Manifest.Name, Shorten(message.Text, MaxNotifyLength)!));
                }

                break;

            case MessageTypes.Setting:
                if (message.Key is { } key && runtime.Plugin.Manifest.FindSetting(key) is { } setting)
                {
                    _store.SetSetting(id, key, setting.Normalize(message.Data));
                    SendSettings(id);
                    RaiseChanged(id);
                }

                break;

            case MessageTypes.Storage:
                if (message.Storage is { } storage && !_store.SetStorage(id, storage))
                {
                    AddLog(runtime, "error", "What the plugin tried to keep is larger than 64 KB, so it was not saved.");
                }

                break;

            case MessageTypes.Log:
                AddLog(runtime, message.Level ?? "info", message.Text ?? string.Empty);
                break;
        }
    }

    private void HandlePlayer(Runtime runtime, HostMessage message)
    {
        var permission = PlayerActions.RequiredPermission(message.Action);
        if (permission is null || !runtime.Plugin.Manifest.Allows(permission))
        {
            AddLog(runtime, "error", $"Resonate ignored \"{message.Action}\": the plugin does not have permission.");
            return;
        }

        var allowed = message.Action == PlayerActions.Volume
            ? Allow(runtime.VolumeCalls, VolumeWindow, VolumeLimit)
            : Allow(runtime.TransportCalls, TransportWindow, TransportLimit);
        if (!allowed)
        {
            AddLog(runtime, "error", $"Resonate ignored \"{message.Action}\": too many in a short time.");
            return;
        }

        var value = message.Value is { } v && double.IsFinite(v) ? v : 0;
        Task command = message.Action switch
        {
            PlayerActions.Play => _player.PlayAsync(),
            PlayerActions.Pause => _player.PauseAsync(),
            PlayerActions.Next => _player.NextAsync(),
            PlayerActions.Previous => _player.PreviousAsync(),
            PlayerActions.Seek => _player.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(value, 0, MaxSeekSeconds))),
            PlayerActions.Volume => _player.SetVolumeAsync(Math.Clamp(value, 0, 1)),
            _ => Task.CompletedTask,
        };
        _ = Observe(command);
    }

    private static async Task Observe(Task command)
    {
        try
        {
            await command.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The player reports its own failures to the user.
        }
    }

    private bool Allow(Queue<DateTimeOffset> calls, TimeSpan window, int limit)
    {
        var now = _time.GetUtcNow();
        lock (calls)
        {
            while (calls.Count > 0 && now - calls.Peek() >= window)
            {
                calls.Dequeue();
            }

            if (calls.Count >= limit)
            {
                return false;
            }

            calls.Enqueue(now);
            return true;
        }
    }

    private void AddLog(Runtime runtime, string level, string text)
    {
        var line = Shorten(text, 500) ?? string.Empty;
        lock (_gate)
        {
            if (level == "error")
            {
                runtime.LastProblem = line;
            }
        }

        if (level == "error")
        {
            RaiseChanged(runtime.Plugin.Manifest.Id);
        }
    }

    private void SendLoad(string id)
    {
        HostMessage message;
        lock (_gate)
        {
            var runtime = _runtimes[id];
            if (runtime.Folder is null || runtime.Status != PluginStatus.Starting)
            {
                return;
            }

            var manifest = runtime.Plugin.Manifest;
            message = new HostMessage
            {
                Type = MessageTypes.Load,
                Plugin = id,
                Folder = runtime.Folder,
                Main = manifest.Main,
                Permissions = [.. manifest.Permissions],
                Settings = manifest.EffectiveSettings(_store.GetSettings(id)),
                Storage = _store.GetStorage(id),
            };
        }

        Send(message);
        if (_catalog.Find(id)!.Manifest.Allows(PluginPermissions.PlayerRead))
        {
            // Plugins started after the helper still get what is playing at once.
            SendState(force: true);
        }
    }

    private void SendSettings(string id)
    {
        var manifest = _catalog.Find(id)?.Manifest;
        if (manifest is not null && !IsPreview)
        {
            Send(new HostMessage { Type = MessageTypes.Settings, Plugin = id, Settings = manifest.EffectiveSettings(_store.GetSettings(id)) });
        }
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e) => SendState(force: false);

    /// <summary>Sends what is playing when it changed in a way plugins care about (not the clock moving on).</summary>
    private void SendState(bool force)
    {
        NowPlaying? state;
        lock (_gate)
        {
            if (!_hostReady)
            {
                return;
            }

            state = _player.Current;
            var now = _time.GetTimestamp();
            if (!force && SameAsSent(state, _time.GetElapsedTime(_lastSentTimestamp, now)))
            {
                return;
            }

            _lastSent = state;
            _lastSentTimestamp = now;
        }

        Send(new HostMessage { Type = MessageTypes.State, State = state });
    }

    private bool SameAsSent(NowPlaying? state, TimeSpan sinceSent)
    {
        var sent = _lastSent;
        if (state is null || sent is null)
        {
            return state is null && sent is null;
        }

        if (state.Title != sent.Title
            || state.Artists != sent.Artists
            || state.Album != sent.Album
            || state.Uri != sent.Uri
            || state.ContextUri != sent.ContextUri
            || state.IsPlaying != sent.IsPlaying
            || state.CanSeek != sent.CanSeek
            || Math.Abs(state.Duration - sent.Duration) > 0.5
            || Math.Abs(state.Volume - sent.Volume) > 0.005)
        {
            return false;
        }

        var expected = sent.Position + (sent.IsPlaying ? sinceSent.TotalSeconds : 0);
        return Math.Abs(state.Position - expected) <= PositionTolerance.TotalSeconds;
    }

    private void Send(HostMessage message)
    {
        IPluginHostProcess? host;
        lock (_gate)
        {
            host = _hostReady ? _host : null;
        }

        host?.Channel.Send(message);
    }

    private void SetPreviewOn(string id) => Update(id, r =>
    {
        r.Status = PluginStatus.Running;
        r.StatusText = "Demo mode: plugins are not downloaded or run.";
    });

    private void Update(string id, Action<Runtime> change)
    {
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(id, out var runtime))
            {
                return;
            }

            change(runtime);
        }

        RaiseChanged(id);
    }

    private void RaiseChanged(string id) => Changed?.Invoke(this, id);

    private void RaiseAllChanged()
    {
        foreach (var id in _runtimes.Keys)
        {
            RaiseChanged(id);
        }
    }

    private PluginView ViewLocked(Runtime runtime) => new(
        runtime.Plugin.Manifest,
        runtime.Status,
        runtime.Progress,
        runtime.StatusText,
        runtime.Error ?? runtime.LastProblem,
        [.. runtime.Commands],
        runtime.Plugin.Manifest.EffectiveSettings(_store.GetSettings(runtime.Plugin.Manifest.Id)));

    private static string? Shorten(string? text, int length)
    {
        if (text is null)
        {
            return null;
        }

        text = text.Trim();
        return text.Length > length ? text[..(length - 1)] + "…" : text;
    }

    private sealed class Runtime(CatalogPlugin plugin)
    {
        public CatalogPlugin Plugin { get; } = plugin;

        public PluginStatus Status { get; set; }

        public double Progress { get; set; }

        public string? Folder { get; set; }

        public string? StatusText { get; set; }

        /// <summary>Why it is off or failed.</summary>
        public string? Error { get; set; }

        /// <summary>The last error it reported while running.</summary>
        public string? LastProblem { get; set; }

        public List<PluginCommand> Commands { get; set; } = [];

        /// <summary>Cancels the download while it runs.</summary>
        public CancellationTokenSource? Download { get; set; }

        public Queue<DateTimeOffset> TransportCalls { get; } = new();

        public Queue<DateTimeOffset> VolumeCalls { get; } = new();

        public Queue<DateTimeOffset> Notifications { get; } = new();

        /// <summary>Back to off: no commands, no status.</summary>
        public void Reset()
        {
            Status = PluginStatus.Off;
            Progress = 0;
            StatusText = null;
            LastProblem = null;
            Commands = [];
        }
    }
}

using System.IO.Pipes;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Resonate.PluginHost;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins.Tests.Fakes;

/// <summary>
/// The real plugin helper loop, run in this process over a pair of pipes,
/// with a fake clock for timers. The test plays Resonate's side.
/// </summary>
internal sealed class HostHarness : IAsyncDisposable
{
    private readonly AnonymousPipeServerStream _toHost = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream _fromHost = new(PipeDirection.In);
    private readonly Channel<HostMessage> _received = Channel.CreateUnbounded<HostMessage>();
    private readonly List<HostMessage> _seen = [];
    private readonly Task _loop;
    private readonly Task _reader;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-plugin-test-" + Guid.NewGuid().ToString("N"));

    public HostHarness(FakeTimeProvider? time = null)
    {
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 22, 0, 0, TimeSpan.Zero));
        var hostChannel = new MessageChannel(
            new AnonymousPipeClientStream(PipeDirection.In, _toHost.ClientSafePipeHandle),
            new AnonymousPipeClientStream(PipeDirection.Out, _fromHost.ClientSafePipeHandle));
        Loop = new HostLoop(hostChannel, Time);
        App = new MessageChannel(_fromHost, _toHost);
        _loop = Task.Run(() => Loop.RunAsync(CancellationToken.None));
        _reader = Task.Run(async () =>
        {
            await foreach (var message in App.ReadAllAsync(CancellationToken.None))
            {
                await _received.Writer.WriteAsync(message);
            }
        });
    }

    public FakeTimeProvider Time { get; }

    public HostLoop Loop { get; }

    /// <summary>Resonate's end.</summary>
    public MessageChannel App { get; }

    /// <summary>Every message received so far (by the Wait methods).</summary>
    public IReadOnlyList<HostMessage> Seen => _seen;

    public static string RepositoryPlugin(string id) => Path.Combine(RepositoryRoot(), "plugins", id);

    public static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Resonate.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository was not found.");
    }

    /// <summary>Starts a plugin from a script.</summary>
    public void LoadScript(string id, string script, IEnumerable<string>? permissions = null, JsonObject? settings = null, JsonObject? storage = null)
    {
        var folder = Path.Combine(_folder, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "main.js"), script);
        LoadFolder(id, folder, permissions ?? [PluginPermissions.PlayerRead, PluginPermissions.PlayerControl, PluginPermissions.PlayerVolume], settings, storage);
    }

    /// <summary>Starts a plugin from its folder, with the settings its manifest gives (and any overrides).</summary>
    public PluginManifest LoadPlugin(string folder, JsonObject? settings = null)
    {
        var manifest = Packaging.PluginPackager.ReadManifest(folder);
        LoadFolder(manifest.Id, folder, manifest.Permissions, manifest.EffectiveSettings(settings), null);
        return manifest;
    }

    public void LoadFolder(string id, string folder, IEnumerable<string> permissions, JsonObject? settings, JsonObject? storage) =>
        App.Send(new HostMessage
        {
            Type = MessageTypes.Load,
            Plugin = id,
            Folder = folder,
            Main = "main.js",
            Permissions = [.. permissions],
            Settings = settings,
            Storage = storage,
        });

    public void SendState(NowPlaying? state) => App.Send(new HostMessage { Type = MessageTypes.State, State = state });

    public void Invoke(string id, string command) => App.Send(new HostMessage { Type = MessageTypes.Invoke, Plugin = id, Command = command });

    /// <summary>Waits for the next message that matches; earlier ones are kept in <see cref="Seen"/>.</summary>
    public async Task<HostMessage> WaitForAsync(Func<HostMessage, bool> match, TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                var message = await _received.Reader.ReadAsync(cancel.Token);
                _seen.Add(message);
                if (match(message))
                {
                    return message;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("No matching message. Seen: " + string.Join(" | ", _seen.Select(Describe)));
        }
    }

    public Task<HostMessage> WaitForAsync(string type, string? plugin = null) =>
        WaitForAsync(m => m.Type == type && (plugin is null || m.Plugin == plugin));

    /// <summary>The messages that arrive within a short while (for checking that something did not happen).</summary>
    public async Task<List<HostMessage>> DrainAsync(TimeSpan? quiet = null)
    {
        var result = new List<HostMessage>();
        while (true)
        {
            using var cancel = new CancellationTokenSource(quiet ?? TimeSpan.FromMilliseconds(300));
            try
            {
                var message = await _received.Reader.ReadAsync(cancel.Token);
                _seen.Add(message);
                result.Add(message);
            }
            catch (OperationCanceledException)
            {
                return result;
            }
        }
    }

    /// <summary>Moves the fake clock on in steps, giving the plugin's thread time to run what came due.</summary>
    public async Task AdvanceAsync(TimeSpan by, TimeSpan? step = null)
    {
        var stepSize = step ?? TimeSpan.FromSeconds(1);
        var left = by;
        while (left > TimeSpan.Zero)
        {
            var move = left < stepSize ? left : stepSize;
            Time.Advance(move);
            left -= move;
            await Task.Delay(5);
        }

        await Task.Delay(50);
    }

    public async ValueTask DisposeAsync()
    {
        App.Send(new HostMessage { Type = MessageTypes.Shutdown });
        await Task.WhenAny(_loop, Task.Delay(TimeSpan.FromSeconds(5)));
        App.Dispose();
        await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(5)));
        _toHost.Dispose();
        _fromHost.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static string Describe(HostMessage m) => $"{m.Type} {m.Plugin} {m.Action} {m.Value} {m.Text}".Trim();
}

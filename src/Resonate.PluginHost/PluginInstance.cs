using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Resonate.Plugins;
using Resonate.Plugins.Protocol;

namespace Resonate.PluginHost;

/// <summary>
/// One plugin: its own JavaScript engine on its own thread. Everything the
/// plugin runs (its script, events, timers, commands) is queued to that
/// thread one at a time, with a time and memory limit on each run, so a
/// slow or stuck plugin can only stall itself. The engine has no .NET,
/// file or network access; the plugin reaches only the functions below.
/// </summary>
internal sealed class PluginInstance : IDisposable
{
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);
    internal const long MemoryLimit = 64L * 1024 * 1024;
    internal const int MaxTimers = 100;
    internal const int MaxScriptLength = 1024 * 1024;
    internal const int ErrorLimit = 5;
    internal static readonly TimeSpan ErrorWindow = TimeSpan.FromMinutes(1);
    internal const int LogLimitPerSecond = 20;
    internal const int MaxTextLength = 500;
    internal const int MaxQueuedWork = 1000;

    private readonly string _id;
    private readonly string _folder;
    private readonly string _main;
    private readonly HashSet<string> _permissions;
    private readonly JsonObject _settings;
    private readonly JsonObject _storage;
    private readonly Action<HostMessage> _send;
    private readonly TimeProvider _time;
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly Dictionary<int, ITimer> _timers = [];
    private readonly Queue<DateTimeOffset> _errors = new();
    private readonly Queue<long> _logs = new();
    private Engine? _engine;
    private JsValue _dispatch = JsValue.Undefined;
    private volatile bool _stopped;

    public PluginInstance(
        string id,
        string folder,
        string main,
        IEnumerable<string> permissions,
        JsonObject? settings,
        JsonObject? storage,
        Action<HostMessage> send,
        TimeProvider time)
    {
        _id = id;
        _folder = folder;
        _main = main;
        _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
        _settings = settings ?? [];
        _storage = storage ?? [];
        _send = send;
        _time = time;
        _thread = new Thread(Run) { IsBackground = true, Name = "Plugin " + id };
    }

    public bool CanReadPlayer => _permissions.Contains(PluginPermissions.PlayerRead);

    public void Start() => _thread.Start();

    public void OnState(NowPlaying? state)
    {
        if (CanReadPlayer)
        {
            var json = ProtocolJson.Serialize(state);
            Post(() => Dispatch("state", json));
        }
    }

    public void OnSettings(JsonObject settings)
    {
        var json = settings.ToJsonString();
        Post(() => Dispatch("settings", json));
    }

    public void OnInvoke(string command) => Post(() => Dispatch("invoke", command));

    /// <summary>Stops the plugin; its thread ends after what it is running now.</summary>
    public void Dispose()
    {
        _stopped = true;
        _work.CompleteAdding();
    }

    /// <summary>Waits for the plugin's thread to finish (for tests).</summary>
    internal bool WaitForExit(TimeSpan timeout) => !_thread.IsAlive || _thread.Join(timeout);

    private void Post(Action work)
    {
        if (_stopped || _work.Count >= MaxQueuedWork)
        {
            return;
        }

        try
        {
            _work.Add(work);
        }
        catch (InvalidOperationException)
        {
            // Stopped meanwhile.
        }
    }

    private void Run()
    {
        try
        {
            if (!Load())
            {
                return;
            }

            foreach (var work in _work.GetConsumingEnumerable())
            {
                if (_stopped)
                {
                    break;
                }

                work();
            }
        }
        finally
        {
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }

            _timers.Clear();
        }
    }

    private bool Load()
    {
        string script;
        try
        {
            var folder = Path.GetFullPath(_folder);
            var path = Path.GetFullPath(Path.Combine(folder, _main));
            if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Fail("The plugin's script is outside its folder.");
            }

            if (new FileInfo(path).Length > MaxScriptLength)
            {
                return Fail("The plugin's script is larger than 1 MB.");
            }

            script = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail("The plugin's script could not be read: " + ex.Message);
        }

        try
        {
            _engine = new Engine(options =>
            {
                options.Strict = true;
                options.Host.StringCompilationAllowed = false;
                options.LimitMemory(MemoryLimit);
                options.TimeoutInterval(CallTimeout);
                options.LimitRecursion(256);
            });

            var factory = _engine.Evaluate(Prelude.Source, "prelude.js");
            _dispatch = _engine.Invoke(factory, CreateHostObject(_engine));

            var init = new JsonObject
            {
                ["settings"] = _settings.DeepClone(),
                ["storage"] = _storage.DeepClone(),
            };
            _engine.Constraints.Reset();
            _engine.Invoke(_dispatch, "init", init.ToJsonString());

            _engine.Constraints.Reset();
            _engine.Execute(script, _main);
        }
        catch (Exception ex)
        {
            return Fail("The plugin could not start: " + Describe(ex));
        }

        _send(new HostMessage { Type = MessageTypes.Loaded, Plugin = _id });
        return true;
    }

    private void Dispatch(string kind, string payload)
    {
        if (_stopped || _engine is null)
        {
            return;
        }

        try
        {
            _engine.Constraints.Reset();
            _engine.Invoke(_dispatch, kind, payload);
        }
        catch (Exception ex)
        {
            ReportError(Describe(ex));
        }
    }

    private JsObject CreateHostObject(Engine engine)
    {
        var host = new JsObject(engine);
        Add(engine, host, "player", args =>
        {
            var action = args.At(0).ToString();
            var permission = PlayerActions.RequiredPermission(action);
            if (permission is null)
            {
                throw new JavaScriptException(engine.Intrinsics.TypeError, "Unknown player action \"" + action + "\".");
            }

            if (!_permissions.Contains(permission))
            {
                throw new JavaScriptException(engine.Intrinsics.TypeError, "This plugin does not have the \"" + permission + "\" permission.");
            }

            var value = args.At(1).IsNumber() ? args.At(1).AsNumber() : 0;
            _send(new HostMessage { Type = MessageTypes.Player, Plugin = _id, Action = action, Value = double.IsFinite(value) ? value : 0 });
            return JsValue.Undefined;
        });
        Add(engine, host, "commands", args =>
        {
            var commands = new List<PluginCommand>();
            if (args.At(0) is JsArray array)
            {
                for (uint i = 0; i < array.Length && commands.Count < Resonate.Plugins.PluginManager.MaxCommands; i++)
                {
                    var item = array.Get(i);
                    if (item.IsObject())
                    {
                        var command = item.AsObject();
                        commands.Add(new PluginCommand { Id = Shorten(command.Get("id").ToString()), Title = Shorten(command.Get("title").ToString()) });
                    }
                }
            }

            _send(new HostMessage { Type = MessageTypes.Commands, Plugin = _id, Commands = commands });
            return JsValue.Undefined;
        });
        Add(engine, host, "status", args =>
        {
            _send(new HostMessage { Type = MessageTypes.Status, Plugin = _id, Text = Shorten(args.At(0).ToString()) });
            return JsValue.Undefined;
        });
        Add(engine, host, "notify", args =>
        {
            _send(new HostMessage { Type = MessageTypes.Notify, Plugin = _id, Text = Shorten(args.At(0).ToString()) });
            return JsValue.Undefined;
        });
        Add(engine, host, "setting", args =>
        {
            _send(new HostMessage { Type = MessageTypes.Setting, Plugin = _id, Key = Shorten(args.At(0).ToString()), Data = JsonNode.Parse(args.At(1).ToString()) });
            return JsValue.Undefined;
        });
        Add(engine, host, "storage", args =>
        {
            var json = args.At(0).ToString();
            if (json.Length > PluginStateStore.MaxStorageLength)
            {
                throw new JavaScriptException(engine.Intrinsics.TypeError, "A plugin can keep at most 64 KB.");
            }

            if (JsonNode.Parse(json) is JsonObject storage)
            {
                _send(new HostMessage { Type = MessageTypes.Storage, Plugin = _id, Storage = storage });
            }

            return JsValue.Undefined;
        });
        Add(engine, host, "log", args =>
        {
            Log(args.At(0).ToString() == "error" ? "error" : "info", args.At(1).ToString());
            return JsValue.Undefined;
        });
        Add(engine, host, "error", args =>
        {
            ReportError(args.At(0).ToString());
            return JsValue.Undefined;
        });
        Add(engine, host, "now", _ => JsNumber.Create(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency));
        Add(engine, host, "setTimer", args =>
        {
            if (_timers.Count >= MaxTimers)
            {
                throw new JavaScriptException(engine.Intrinsics.TypeError, "A plugin can have at most " + MaxTimers + " timers.");
            }

            var id = (int)args.At(0).AsNumber();
            var repeat = args.At(2).AsBoolean();
            var milliseconds = args.At(1).AsNumber();
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(double.IsFinite(milliseconds) ? milliseconds : 0, repeat ? 250 : 0, int.MaxValue));
            _timers[id] = _time.CreateTimer(_ => Post(() => FireTimer(id, repeat)), null, delay, repeat ? delay : Timeout.InfiniteTimeSpan);
            return JsValue.Undefined;
        });
        Add(engine, host, "clearTimer", args =>
        {
            if (_timers.Remove((int)args.At(0).AsNumber(), out var timer))
            {
                timer.Dispose();
            }

            return JsValue.Undefined;
        });
        return host;
    }

    private static void Add(Engine engine, ObjectInstance target, string name, Func<JsValue[], JsValue> body) =>
        target.Set(name, new ClrFunction(engine, name, (_, args) => body(args)));

    private void FireTimer(int id, bool repeat)
    {
        if (!_timers.TryGetValue(id, out var timer))
        {
            // Cleared after it was due.
            return;
        }

        if (!repeat)
        {
            _timers.Remove(id);
            timer.Dispose();
        }

        Dispatch("timer", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void Log(string level, string text)
    {
        var now = _time.GetTimestamp();
        while (_logs.Count > 0 && _time.GetElapsedTime(_logs.Peek(), now) > TimeSpan.FromSeconds(1))
        {
            _logs.Dequeue();
        }

        if (_logs.Count >= LogLimitPerSecond)
        {
            return;
        }

        _logs.Enqueue(now);
        _send(new HostMessage { Type = MessageTypes.Log, Plugin = _id, Level = level, Text = Shorten(text) });
    }

    /// <summary>Reports an error; after <see cref="ErrorLimit"/> in a minute the plugin is stopped.</summary>
    private void ReportError(string message)
    {
        Log("error", message);
        var now = _time.GetUtcNow();
        _errors.Enqueue(now);
        while (now - _errors.Peek() > ErrorWindow)
        {
            _errors.Dequeue();
        }

        if (_errors.Count >= ErrorLimit)
        {
            Fail("Stopped after repeated errors. The last one: " + message);
        }
    }

    private bool Fail(string message)
    {
        _stopped = true;
        _send(new HostMessage { Type = MessageTypes.Failed, Plugin = _id, Text = Shorten(message) });
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }

        _timers.Clear();
        _work.CompleteAdding();
        return false;
    }

    private static string Describe(Exception ex) => ex switch
    {
        JavaScriptException js => js.Location.Start.Line > 0 ? $"{js.Message} (line {js.Location.Start.Line})" : js.Message,
        TimeoutException => "it took longer than " + CallTimeout.TotalSeconds + " seconds.",
        MemoryLimitExceededException => "it used too much memory.",
        RecursionDepthOverflowException => "it called itself too deeply.",
        _ => ex.Message,
    };

    private static string Shorten(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;
}

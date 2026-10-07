using System.Text.Json;
using System.Text.Json.Nodes;

namespace Resonate.Plugins;

/// <summary>Which plugins are on, their settings, and what they keep between runs.</summary>
public sealed class PluginStateFile
{
    public List<string> Enabled { get; set; } = [];

    /// <summary>Kept when a plugin is turned off, so turning it on again brings them back.</summary>
    public Dictionary<string, JsonObject> Settings { get; set; } = [];

    public Dictionary<string, JsonObject> Storage { get; set; } = [];
}

/// <summary>
/// Reads and writes <see cref="PluginStateFile"/> as JSON next to Resonate's
/// settings. Safe to use from any thread; writes happen at once and replace
/// the file in one step.
/// </summary>
public sealed class PluginStateStore
{
    /// <summary>The most a plugin may keep, as JSON text.</summary>
    public const int MaxStorageLength = 64 * 1024;

    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly PluginStateFile _state;

    /// <param name="path">The file, or null to keep everything in memory (demo mode, tests).</param>
    public PluginStateStore(string? path)
    {
        _path = path;
        _state = Load(path);
    }

    public bool IsEnabled(string id)
    {
        lock (_gate)
        {
            return _state.Enabled.Contains(id);
        }
    }

    public IReadOnlyList<string> EnabledPlugins
    {
        get
        {
            lock (_gate)
            {
                return [.. _state.Enabled];
            }
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var changed = enabled ? !_state.Enabled.Contains(id) : _state.Enabled.Remove(id);
            if (enabled && changed)
            {
                _state.Enabled.Add(id);
            }

            if (changed)
            {
                SaveLocked();
            }
        }
    }

    /// <summary>A copy of the plugin's saved settings (only those the user or plugin changed).</summary>
    public JsonObject? GetSettings(string id)
    {
        lock (_gate)
        {
            return _state.Settings.TryGetValue(id, out var settings) ? (JsonObject)settings.DeepClone() : null;
        }
    }

    public void SetSetting(string id, string key, JsonNode value)
    {
        lock (_gate)
        {
            if (!_state.Settings.TryGetValue(id, out var settings))
            {
                settings = [];
                _state.Settings[id] = settings;
            }

            settings[key] = value.DeepClone();
            SaveLocked();
        }
    }

    public JsonObject GetStorage(string id)
    {
        lock (_gate)
        {
            return _state.Storage.TryGetValue(id, out var storage) ? (JsonObject)storage.DeepClone() : [];
        }
    }

    /// <summary>Keeps what the plugin asked to keep; false when it is too large.</summary>
    public bool SetStorage(string id, JsonObject storage)
    {
        if (storage.ToJsonString().Length > MaxStorageLength)
        {
            return false;
        }

        lock (_gate)
        {
            _state.Storage[id] = (JsonObject)storage.DeepClone();
            SaveLocked();
        }

        return true;
    }

    private static PluginStateFile Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(stream, PluginJsonContext.Default.PluginStateFile) ?? new PluginStateFile();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file: every plugin starts off again.
        }

        return new PluginStateFile();
    }

    private void SaveLocked()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, _state, PluginJsonContext.Default.PluginStateFile);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: kept for this session.
        }
    }
}

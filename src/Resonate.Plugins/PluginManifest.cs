using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Resonate.Plugins;

/// <summary>
/// What a plugin is (its <c>plugin.json</c>): its name, what it may do, and
/// the settings Resonate draws for it in Settings.
/// </summary>
public sealed partial class PluginManifest
{
    /// <summary>Lower-case letters, digits and dashes, such as "sleep-timer".</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>One or two sentences for Settings.</summary>
    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    /// <summary>The script that runs, relative to the plugin's folder.</summary>
    public string Main { get; set; } = "main.js";

    /// <summary>See <see cref="PluginPermissions"/>.</summary>
    public List<string> Permissions { get; set; } = [];

    public List<PluginSetting> Settings { get; set; } = [];

    public bool Allows(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public PluginSetting? FindSetting(string key) => Settings.Find(s => s.Key == key);

    /// <summary>Every setting at its current value: the saved one when valid, else the default.</summary>
    public JsonObject EffectiveSettings(JsonObject? saved)
    {
        var result = new JsonObject();
        foreach (var setting in Settings)
        {
            var value = saved is not null && saved.TryGetPropertyValue(setting.Key, out var node) ? node : null;
            result[setting.Key] = setting.Normalize(value);
        }

        return result;
    }

    /// <summary>Problems that would stop the plugin from being offered; empty when it is fine.</summary>
    public IReadOnlyList<string> Validate(Func<string, bool>? fileExists = null)
    {
        var errors = new List<string>();
        if (!IdPattern().IsMatch(Id))
        {
            errors.Add($"The id \"{Id}\" must be 2 to 40 lower-case letters, digits or dashes.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("The name is missing.");
        }

        if (!System.Version.TryParse(Version, out _))
        {
            errors.Add($"The version \"{Version}\" is not like 1.0.0.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            errors.Add("The description is missing.");
        }

        if (string.IsNullOrWhiteSpace(Main) || Main.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(Main))
        {
            errors.Add($"The main script \"{Main}\" must be a file inside the plugin's folder.");
        }
        else if (fileExists is not null && !fileExists(Main))
        {
            errors.Add($"The main script \"{Main}\" does not exist.");
        }

        foreach (var permission in Permissions)
        {
            if (!PluginPermissions.IsKnown(permission))
            {
                errors.Add($"Unknown permission \"{permission}\".");
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in Settings)
        {
            if (!SettingKeyPattern().IsMatch(setting.Key))
            {
                errors.Add($"The setting key \"{setting.Key}\" must be letters and digits.");
            }
            else if (!keys.Add(setting.Key))
            {
                errors.Add($"The setting \"{setting.Key}\" appears twice.");
            }

            errors.AddRange(setting.Validate());
        }

        return errors;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,39}$")]
    private static partial Regex SettingKeyPattern();
}

/// <summary>The kinds of setting a plugin can ask for.</summary>
public static class PluginSettingTypes
{
    /// <summary>On or off.</summary>
    public const string Toggle = "toggle";

    /// <summary>A number between <see cref="PluginSetting.Min"/> and <see cref="PluginSetting.Max"/>.</summary>
    public const string Number = "number";

    /// <summary>A line of text.</summary>
    public const string Text = "text";

    /// <summary>Several lines of text, one item per line (such as artist names).</summary>
    public const string List = "list";

    /// <summary>One of <see cref="PluginSetting.Options"/>.</summary>
    public const string Choice = "choice";

    public static bool IsKnown(string type) => type is Toggle or Number or Text or List or Choice;
}

/// <summary>One setting of a plugin, drawn by Resonate with its own controls.</summary>
public sealed class PluginSetting
{
    public const int MaxTextLength = 500;
    public const int MaxListItems = 500;

    public string Key { get; set; } = string.Empty;

    /// <summary>See <see cref="PluginSettingTypes"/>.</summary>
    public string Type { get; set; } = PluginSettingTypes.Toggle;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public JsonNode? Default { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    /// <summary>Shown after a number, such as "minutes".</summary>
    public string? Unit { get; set; }

    /// <summary>Shown in an empty text or list box.</summary>
    public string? Placeholder { get; set; }

    public List<PluginChoice>? Options { get; set; }

    /// <summary>
    /// <paramref name="value"/> as this setting's type, within its limits, or
    /// the default when it can not be read as one. Every value a plugin or
    /// the user sets goes through here before it is kept.
    /// </summary>
    public JsonNode Normalize(JsonNode? value)
    {
        switch (Type)
        {
            case PluginSettingTypes.Toggle:
                return JsonValue.Create(ReadBool(value) ?? ReadBool(Default) ?? false);

            case PluginSettingTypes.Number:
                var number = ReadNumber(value) ?? ReadNumber(Default) ?? Min ?? 0;
                if (Min is { } min && number < min)
                {
                    number = min;
                }

                if (Max is { } max && number > max)
                {
                    number = max;
                }

                return JsonValue.Create(number);

            case PluginSettingTypes.Text:
                var text = ReadString(value) ?? ReadString(Default) ?? string.Empty;
                return JsonValue.Create(text.Length > MaxTextLength ? text[..MaxTextLength] : text);

            case PluginSettingTypes.List:
                var items = ReadList(value) ?? ReadList(Default) ?? [];
                var array = new JsonArray();
                foreach (var item in items.Take(MaxListItems))
                {
                    array.Add((JsonNode?)JsonValue.Create(item));
                }

                return array;

            case PluginSettingTypes.Choice:
                var choice = ReadString(value);
                if (choice is not null && Options?.Exists(o => o.Value == choice) == true)
                {
                    return JsonValue.Create(choice);
                }

                var fallback = ReadString(Default);
                return JsonValue.Create(fallback ?? Options?.FirstOrDefault()?.Value ?? string.Empty);

            default:
                return JsonValue.Create(string.Empty);
        }
    }

    internal IEnumerable<string> Validate()
    {
        if (!PluginSettingTypes.IsKnown(Type))
        {
            yield return $"The setting \"{Key}\" has an unknown type \"{Type}\".";
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            yield return $"The setting \"{Key}\" has no title.";
        }

        if (Type == PluginSettingTypes.Choice && (Options is null || Options.Count == 0))
        {
            yield return $"The setting \"{Key}\" is a choice without options.";
        }

        if (Min is { } min && Max is { } max && min > max)
        {
            yield return $"The setting \"{Key}\" has a minimum above its maximum.";
        }
    }

    private static bool? ReadBool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;

    private static double? ReadNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        // A number may be held as an int, a double or parsed JSON; its JSON text reads the same for all.
        var text = value.GetValueKind() switch
        {
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null,
        };

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static List<string>? ReadList(JsonNode? node)
    {
        IEnumerable<string>? raw = node switch
        {
            JsonArray array => array.Select(ReadString).OfType<string>(),
            JsonValue value when value.TryGetValue<string>(out var text) => text.Split('\n'),
            _ => null,
        };

        if (raw is null)
        {
            return null;
        }

        return raw
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Select(item => item.Length > MaxTextLength ? item[..MaxTextLength] : item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class PluginChoice
{
    public string Value { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// What a plugin may ask Resonate to do. A plugin gets only what its
/// manifest lists, and Settings shows the list before it is turned on.
/// Plugins never see the Spotify sign-in, files or the internet.
/// </summary>
public static class PluginPermissions
{
    /// <summary>The song that is playing, its position, and changes to them.</summary>
    public const string PlayerRead = "player.read";

    /// <summary>Play, pause, skip and seek.</summary>
    public const string PlayerControl = "player.control";

    /// <summary>Spotify's volume.</summary>
    public const string PlayerVolume = "player.volume";

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [PlayerRead] = "see what is playing",
        [PlayerControl] = "control playback",
        [PlayerVolume] = "change the volume",
    };

    public static bool IsKnown(string permission) => Descriptions.ContainsKey(permission);

    /// <summary>The permissions in words for Settings, such as "see what is playing and control playback".</summary>
    public static string Describe(IEnumerable<string> permissions)
    {
        var parts = permissions.Where(Descriptions.ContainsKey).Select(p => Descriptions[p]).ToList();
        return parts.Count switch
        {
            0 => "nothing beyond its own settings",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }
}

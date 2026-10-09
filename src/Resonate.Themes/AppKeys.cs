using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>What a keyboard shortcut in the window does. Saved by name: add new ones at the end, never rename.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppCommand>))]
public enum AppCommand
{
    PlayPause,
    Next,
    Previous,
    SeekForward,
    SeekBack,
    VolumeUp,
    VolumeDown,
    Mute,
    Shuffle,
    Repeat,
    Like,
    Search,
    QuickSearch,
    Home,
    LikedSongs,
    Queue,
    Lyrics,
    Settings,
    NewPlaylist,
    MiniPlayer,
    Back,
    AppSizeUp,
    AppSizeDown,
    AppSizeReset,
}

/// <summary>
/// A key with its modifiers, as the shortcuts list saves and shows it:
/// "Ctrl+Shift+S", "Space", "Shift+Right". <see cref="Key"/> is the key's
/// name (a letter, a digit, a punctuation key's name such as "Equal", or
/// Windows' name such as "Right", "Add" or "F5").
/// </summary>
public readonly record struct KeyCombo(bool Control, bool Alt, bool Shift, string Key)
{
    /// <summary>Reads "Ctrl+Alt+Shift+Key"; null for anything else (a Windows key combination included).</summary>
    public static KeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[^1].Length == 0)
        {
            return null;
        }

        var combo = new KeyCombo(false, false, false, parts[^1]);
        foreach (var part in parts[..^1])
        {
            combo = part.ToUpperInvariant() switch
            {
                "CTRL" => combo with { Control = true },
                "ALT" => combo with { Alt = true },
                "SHIFT" => combo with { Shift = true },
                _ => default,
            };

            if (combo.Key is null)
            {
                return null;
            }
        }

        return combo;
    }

    public override string ToString() =>
        (Control ? "Ctrl+" : string.Empty) + (Alt ? "Alt+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + Key;

    public bool Equals(KeyCombo other) =>
        Control == other.Control && Alt == other.Alt && Shift == other.Shift && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Control, Alt, Shift, Key?.ToUpperInvariant());
}

/// <summary>
/// The window's keyboard shortcuts (Settings, About, Help): what each one
/// does, the keys it starts with, the user's own keys on top of those
/// (saved by command name; an empty text means none), and which keys are
/// left alone while typing. Space plays and pauses unless the user picks
/// other keys.
/// </summary>
public static class AppKeys
{
    /// <summary>The commands in the order the list shows them, with their names.</summary>
    public static IReadOnlyList<(AppCommand Command, string Name)> Commands { get; } =
    [
        (AppCommand.PlayPause, "Play or pause"),
        (AppCommand.Next, "Next song"),
        (AppCommand.Previous, "Previous song"),
        (AppCommand.SeekForward, "Forward 10 seconds"),
        (AppCommand.SeekBack, "Back 10 seconds"),
        (AppCommand.VolumeUp, "Volume up"),
        (AppCommand.VolumeDown, "Volume down"),
        (AppCommand.Mute, "Mute"),
        (AppCommand.Shuffle, "Shuffle"),
        (AppCommand.Repeat, "Repeat"),
        (AppCommand.Like, "Like the song"),
        (AppCommand.Search, "Search"),
        (AppCommand.QuickSearch, "Quick search"),
        (AppCommand.Home, "Home"),
        (AppCommand.LikedSongs, "Liked Songs"),
        (AppCommand.Queue, "Queue"),
        (AppCommand.Lyrics, "Lyrics"),
        (AppCommand.Settings, "Settings"),
        (AppCommand.NewPlaylist, "New playlist"),
        (AppCommand.MiniPlayer, "Mini player"),
        (AppCommand.Back, "Back"),
        (AppCommand.AppSizeUp, "Bigger"),
        (AppCommand.AppSizeDown, "Smaller"),
        (AppCommand.AppSizeReset, "Usual size"),
    ];

    private static readonly Dictionary<AppCommand, KeyCombo[]> DefaultKeys = new()
    {
        [AppCommand.PlayPause] = [Plain("Space")],
        [AppCommand.Next] = [Ctrl("Right")],
        [AppCommand.Previous] = [Ctrl("Left")],
        [AppCommand.SeekForward] = [new(false, false, true, "Right")],
        [AppCommand.SeekBack] = [new(false, false, true, "Left")],
        [AppCommand.VolumeUp] = [Ctrl("Up")],
        [AppCommand.VolumeDown] = [Ctrl("Down")],
        [AppCommand.Shuffle] = [Ctrl("S")],
        [AppCommand.Repeat] = [Ctrl("R")],
        [AppCommand.Search] = [Ctrl("F"), Ctrl("L")],
        [AppCommand.QuickSearch] = [Ctrl("K")],
        [AppCommand.Settings] = [Ctrl("Comma")],
        [AppCommand.NewPlaylist] = [Ctrl("N")],
        [AppCommand.MiniPlayer] = [Ctrl("M")],
        [AppCommand.Back] = [new(false, true, false, "Left")],

        // The plus key beside Backspace (Shift with it types the plus on US keyboards) and the number pad's.
        [AppCommand.AppSizeUp] = [Ctrl("Equal"), new(true, false, true, "Equal"), Ctrl("Add")],
        [AppCommand.AppSizeDown] = [Ctrl("Minus"), Ctrl("Subtract")],
        [AppCommand.AppSizeReset] = [Ctrl("0"), Ctrl("NumberPad0")],
    };

    // Keys a text field uses with Ctrl: moving and selecting by word, deleting, undo, copy and paste.
    private static readonly HashSet<string> TextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Left", "Right", "Up", "Down", "Home", "End", "Back", "Delete", "A", "C", "V", "X", "Y", "Z", "Insert",
    };

    private static KeyCombo Plain(string key) => new(false, false, false, key);

    private static KeyCombo Ctrl(string key) => new(true, false, false, key);

    public static string NameOf(AppCommand command) =>
        Commands.FirstOrDefault(c => c.Command == command).Name ?? command.ToString();

    /// <summary>The keys a command starts with (none for some).</summary>
    public static IReadOnlyList<KeyCombo> Defaults(AppCommand command) =>
        DefaultKeys.TryGetValue(command, out var keys) ? keys : [];

    /// <summary>The keys a command has now: the user's own, or else its defaults.</summary>
    public static IReadOnlyList<KeyCombo> For(AppCommand command, IReadOnlyDictionary<string, string>? own)
    {
        if (own is not null && own.TryGetValue(command.ToString(), out var text))
        {
            return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(KeyCombo.Parse)
                .OfType<KeyCombo>()
                .ToArray();
        }

        return Defaults(command);
    }

    /// <summary>The user's own keys differ from the command's defaults.</summary>
    public static bool IsChanged(AppCommand command, IReadOnlyDictionary<string, string>? own) =>
        !For(command, own).SequenceEqual(Defaults(command));

    /// <summary>Every key in use and its command; the first command in the list wins if two share keys.</summary>
    public static Dictionary<KeyCombo, AppCommand> Table(IReadOnlyDictionary<string, string>? own)
    {
        var table = new Dictionary<KeyCombo, AppCommand>();
        foreach (var (command, _) in Commands)
        {
            foreach (var combo in For(command, own))
            {
                table.TryAdd(combo, command);
            }
        }

        return table;
    }

    /// <summary>
    /// Gives <paramref name="command"/> the keys <paramref name="combo"/>
    /// (null for none) in <paramref name="own"/>, taking them from any
    /// command that had them. Returns that command, if any.
    /// </summary>
    public static AppCommand? Assign(Dictionary<string, string> own, AppCommand command, KeyCombo? combo)
    {
        var taken = combo is { } keys ? TakeFromOthers(own, command, keys) : null;
        own[command.ToString()] = combo?.ToString() ?? string.Empty;
        return taken;
    }

    /// <summary>
    /// Puts a command's default keys back, taking them from any command that
    /// has them now. Returns that command, if any.
    /// </summary>
    public static AppCommand? Reset(Dictionary<string, string> own, AppCommand command)
    {
        own.Remove(command.ToString());
        AppCommand? taken = null;
        foreach (var combo in Defaults(command))
        {
            taken ??= TakeFromOthers(own, command, combo);
        }

        return taken;
    }

    private static AppCommand? TakeFromOthers(Dictionary<string, string> own, AppCommand command, KeyCombo combo)
    {
        AppCommand? taken = null;
        foreach (var (other, _) in Commands)
        {
            if (other == command)
            {
                continue;
            }

            var keys = For(other, own);
            if (keys.Contains(combo))
            {
                own[other.ToString()] = string.Join(", ", keys.Where(k => !k.Equals(combo)));
                taken ??= other;
            }
        }

        return taken;
    }

    /// <summary>Why the keys cannot be used, or null when they can.</summary>
    public static string? Problem(KeyCombo combo)
    {
        var key = combo.Key;
        var plain = !combo.Control && !combo.Alt;
        if (string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            return "Tab moves between controls. Pick other keys.";
        }

        if (plain && !combo.Shift && key is "Enter" or "Escape")
        {
            return key + " is kept for buttons and lists. Pick other keys.";
        }

        if (combo.Alt && !combo.Control && key is "F4" or "Space")
        {
            return "Windows uses Alt+" + key + ". Pick other keys.";
        }

        if (string.Equals(key, "F12", StringComparison.OrdinalIgnoreCase))
        {
            return "F12 is kept for debuggers. Pick other keys.";
        }

        return null;
    }

    /// <summary>
    /// While a text field has the keyboard, these keys belong to it: any key
    /// without Ctrl or Alt (Space and Shift+Right type and select), and Ctrl
    /// with a key that moves, selects, deletes, copies or pastes.
    /// </summary>
    public static bool BelongsToText(KeyCombo combo) =>
        (!combo.Control && !combo.Alt) || (combo.Control && !combo.Alt && TextKeys.Contains(combo.Key));

    /// <summary>How the keys read in the list: "Ctrl+Plus", "Shift+Right", "Num 0".</summary>
    public static string Display(KeyCombo combo)
    {
        var key = combo.Key switch
        {
            "Equal" => "Plus",
            "Add" => "Num Plus",
            "Subtract" => "Num Minus",
            "Back" => "Backspace",
            "PageUp" => "Page Up",
            "PageDown" => "Page Down",
            _ when combo.Key.StartsWith("NumberPad", StringComparison.Ordinal) => "Num " + combo.Key["NumberPad".Length..],
            _ => combo.Key,
        };

        return (combo.Control ? "Ctrl+" : string.Empty) + (combo.Alt ? "Alt+" : string.Empty) + (combo.Shift ? "Shift+" : string.Empty) + key;
    }

    /// <summary>The keys as the list shows them: the first, which is the one to remember (the others do the same).</summary>
    public static string Display(IReadOnlyList<KeyCombo> keys) => keys.Count == 0 ? "None" : Display(keys[0]);
}

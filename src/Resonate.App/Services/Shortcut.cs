using System.Globalization;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Services;

/// <summary>
/// A key combination for a shortcut that works from any app (the summon
/// bar's), such as Ctrl+Shift+K. Saved as that text. It needs Ctrl, Alt or
/// the Windows key, so it never takes a key used for typing.
/// </summary>
internal readonly record struct Shortcut(bool Control, bool Alt, bool Shift, bool Windows, VirtualKey Key)
{
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    /// <summary>The modifiers as RegisterHotKey takes them.</summary>
    public uint Modifiers => (Alt ? ModAlt : 0) | (Control ? ModControl : 0) | (Shift ? ModShift : 0) | (Windows ? ModWin : 0);

    /// <summary>Why the combination cannot be used, or null when it can.</summary>
    public string? Problem =>
        IsModifier(Key) ? "Add a key after Ctrl, Alt, Shift or Windows."
        : !(Control || Alt || Windows) ? "Use Ctrl, Alt or the Windows key with it."
        : Key == VirtualKey.F12 ? "F12 is kept for debuggers. Pick another key."
        : null;

    public static bool IsModifier(VirtualKey key) => key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
        or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
        or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>Reads "Ctrl+Shift+K" and the like; null for anything else.</summary>
    public static Shortcut? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || KeyFrom(parts[^1]) is not { } key)
        {
            return null;
        }

        var shortcut = new Shortcut(false, false, false, false, key);
        foreach (var part in parts[..^1])
        {
            shortcut = part.ToUpperInvariant() switch
            {
                "CTRL" => shortcut with { Control = true },
                "ALT" => shortcut with { Alt = true },
                "SHIFT" => shortcut with { Shift = true },
                "WIN" => shortcut with { Windows = true },
                _ => shortcut,
            };
        }

        return shortcut.Problem is null ? shortcut : null;
    }

    /// <summary>"Ctrl+Alt+Shift+Win+Key", as saved and shown.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Control)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Windows)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    /// <summary>Letters, digits and F-keys by their face; the rest by Windows' name (Space, Home, PageUp).</summary>
    private static string KeyName(VirtualKey key) => key switch
    {
        >= VirtualKey.A and <= VirtualKey.Z => ((char)key).ToString(),
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)(key - VirtualKey.Number0)).ToString(CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };

    private static VirtualKey? KeyFrom(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return VirtualKey.A + (char.ToUpperInvariant(name[0]) - 'A');
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return VirtualKey.Number0 + (name[0] - '0');
        }

        return Enum.TryParse<VirtualKey>(name, ignoreCase: true, out var key) && Enum.IsDefined(key) ? key : null;
    }
}

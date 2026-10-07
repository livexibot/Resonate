using Windows.UI;

namespace Resonate.App.Themes;

public enum ThemeMode
{
    Dark,
    Light,
}

/// <summary>
/// A complete look: every colour the interface uses. Controls never name a
/// colour themselves; they read the brushes in Themes/Tokens.xaml, which
/// <see cref="ThemeService"/> fills from a preset.
/// </summary>
public sealed record ThemePreset(
    string Id,
    string Name,
    ThemeMode Mode,
    Color Background,
    Color Sidebar,
    Color Surface,
    Color SurfaceHover,
    Color SurfacePressed,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color TextTertiary,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color OnAccent)
{
    public static readonly ThemePreset Midnight = new(
        "midnight",
        "Midnight",
        ThemeMode.Dark,
        Background: Hex(0x0F1014),
        Sidebar: Hex(0x14151A),
        Surface: Hex(0x1A1C22),
        SurfaceHover: Hex(0x23252D),
        SurfacePressed: Hex(0x2B2E37),
        Border: Hex(0x262830),
        TextPrimary: Hex(0xF2F3F5),
        TextSecondary: Hex(0xA9ADB8),
        TextTertiary: Hex(0x6F7480),
        Accent: Hex(0x8B7CFF),
        AccentHover: Hex(0x9D90FF),
        AccentPressed: Hex(0x7868F2),
        OnAccent: Hex(0x0F1014));

    public static readonly ThemePreset PureBlack = Midnight with
    {
        Id = "pure-black",
        Name = "Pure black",
        Background = Hex(0x000000),
        Sidebar = Hex(0x000000),
        Surface = Hex(0x0C0C0E),
        SurfaceHover = Hex(0x18181C),
        SurfacePressed = Hex(0x222228),
        Border = Hex(0x1C1C21),
    };

    public static readonly ThemePreset Daylight = new(
        "daylight",
        "Daylight",
        ThemeMode.Light,
        Background: Hex(0xF7F7F9),
        Sidebar: Hex(0xEFEFF3),
        Surface: Hex(0xFFFFFF),
        SurfaceHover: Hex(0xE8E8EE),
        SurfacePressed: Hex(0xDEDEE6),
        Border: Hex(0xE2E2E8),
        TextPrimary: Hex(0x15161A),
        TextSecondary: Hex(0x585C66),
        TextTertiary: Hex(0x8A8E99),
        Accent: Hex(0x5B4CE0),
        AccentHover: Hex(0x6A5CEA),
        AccentPressed: Hex(0x4E3FD0),
        OnAccent: Hex(0xFFFFFF));

    public static IReadOnlyList<ThemePreset> All { get; } = [Midnight, PureBlack, Daylight];

    public static ThemePreset ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Midnight;

    public static Color Hex(uint rgb, byte alpha = 0xFF) =>
        Microsoft.UI.ColorHelper.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

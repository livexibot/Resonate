namespace Resonate.Themes;

/// <summary>
/// The built-in looks, in three groups: dark, light and true black (OLED). Each one is a different idea, not a recolour:
/// clean everyday themes (one in Windows' own Mica), a glass one, a
/// true-black one and artistic ones, with the player in every position.
/// Users start from any of them and customise from there.
/// </summary>
public static class ThemePresets
{
    /// <summary>Clean and dark, with a soft violet accent. The default.</summary>
    public static ThemeDefinition Midnight { get; } = new()
    {
        Id = "midnight",
        Name = "Midnight",
        Background = ThemeColor.FromRgb(0x0F1014),
        Background2 = ThemeColor.FromRgb(0x1C1730),
        Sidebar = ThemeColor.FromRgb(0x14151A),
        Surface = ThemeColor.FromRgb(0x1A1C22),
        Player = ThemeColor.FromRgb(0x0F1014),
        Text = ThemeColor.FromRgb(0xF2F3F5),
        Accent = ThemeColor.FromRgb(0x8B7CFF),
        Accent2 = ThemeColor.FromRgb(0x5CC8FF),
        CornerRadius = 12,
        PanelGap = 8,
        Shadow = ShadowStyle.Soft,
        PlayerLayout = PlayerLayout.Docked,
        Progress = ProgressStyle.Line,
    };

    /// <summary>Bright and airy: white cards on soft grey, a floating player.</summary>
    public static ThemeDefinition Daylight { get; } = new()
    {
        Id = "daylight",
        Name = "Daylight",
        Background = ThemeColor.FromRgb(0xE9ECF2),
        Background2 = ThemeColor.FromRgb(0xDCE3F5),
        Sidebar = ThemeColor.FromRgb(0xF6F7FA),
        Surface = ThemeColor.FromRgb(0xFFFFFF),
        Player = ThemeColor.FromRgb(0xFFFFFF),
        Text = ThemeColor.FromRgb(0x16181D),
        Accent = ThemeColor.FromRgb(0x3E63DD),
        Accent2 = ThemeColor.FromRgb(0x12A4C9),
        CornerRadius = 16,
        PanelGap = 10,
        Shadow = ShadowStyle.Soft,
        PlayerLayout = PlayerLayout.Floating,
        Progress = ProgressStyle.Bold,
    };

    /// <summary>
    /// Clear glass over the playing song's blurred cover: barely tinted panes
    /// with a bright rim, large soft corners and a hovering pill player; the
    /// accent follows the cover.
    /// </summary>
    public static ThemeDefinition Glass { get; } = new()
    {
        Id = "glass",
        Name = "Liquid Glass",
        Background = ThemeColor.FromRgb(0x0B0E16),
        Background2 = ThemeColor.FromRgb(0x1A2236),
        Sidebar = ThemeColor.White,
        Surface = ThemeColor.White,
        Player = ThemeColor.White,
        Text = ThemeColor.White,
        Accent = ThemeColor.FromRgb(0xA5E4FF),
        Accent2 = ThemeColor.FromRgb(0xE3C4FF),
        Border = ThemeColor.White.WithAlpha(0.3),
        Backdrop = WindowBackdrop.Artwork,
        BackdropTint = 0.18,
        PanelOpacity = 0.12,
        AdaptiveAccent = true,
        CornerRadius = 28,
        Buttons = ButtonShape.Round,
        BorderWidth = 1,
        PanelGap = 14,
        Shadow = ShadowStyle.Soft,
        DisplayFont = "Inter",
        TextFont = "Inter",
        PlayerLayout = PlayerLayout.Hovering,
        Progress = ProgressStyle.Line,
    };

    /// <summary>The OLED default: true black everywhere, panels marked only by hairlines, soft modern corners.</summary>
    public static ThemeDefinition Oled { get; } = new()
    {
        Id = "oled",
        Name = "OLED",
        Background = ThemeColor.Black,
        Background2 = ThemeColor.Black,
        Sidebar = ThemeColor.Black,
        Surface = ThemeColor.Black,
        Player = ThemeColor.Black,
        Text = ThemeColor.FromRgb(0xF5F5F7),
        Accent = ThemeColor.FromRgb(0x7C9CFF),
        Accent2 = ThemeColor.FromRgb(0xF472B6),
        Border = ThemeColor.FromRgb(0x1C1C1E),
        CornerRadius = 14,
        Buttons = ButtonShape.Round,
        BorderWidth = 1,
        PanelGap = 8,
        Shadow = ShadowStyle.None,
        DisplayFont = "Geist",
        TextFont = "Geist",
        PlayerLayout = PlayerLayout.Floating,
        Progress = ProgressStyle.Line,
    };
    /// <summary>True black for OLED screens: hairlines, square corners, one red accent.</summary>
    public static ThemeDefinition PureBlack { get; } = new()
    {
        Id = "pure-black",
        Name = "Pure Black",
        Background = ThemeColor.Black,
        Background2 = ThemeColor.FromRgb(0x0A0A0A),
        Sidebar = ThemeColor.Black,
        Surface = ThemeColor.Black,
        Player = ThemeColor.Black,
        Text = ThemeColor.White,
        Accent = ThemeColor.FromRgb(0xFF453A),
        Accent2 = ThemeColor.FromRgb(0xFF9F0A),
        Border = ThemeColor.FromRgb(0x262626),
        CornerRadius = 2,
        Buttons = ButtonShape.Square,
        BorderWidth = 1,
        PanelGap = 0,
        Shadow = ShadowStyle.None,
        PlayerLayout = PlayerLayout.Docked,
        Progress = ProgressStyle.Minimal,
        PlayButton = PlayButtonStyle.Outline,
        Cover = CoverStyle.Square,
    };

    /// <summary>Neon on a purple dusk gradient: glowing outlines and the cover as a record.</summary>
    public static ThemeDefinition Synthwave { get; } = new()
    {
        Id = "synthwave",
        Name = "Synthwave",
        Background = ThemeColor.FromRgb(0x13061F),
        Background2 = ThemeColor.FromRgb(0x46104F),
        Sidebar = ThemeColor.FromRgb(0x1A0A2B),
        Surface = ThemeColor.FromRgb(0x1E0C33),
        Player = ThemeColor.FromRgb(0x160826),
        Text = ThemeColor.FromRgb(0xFDEBFF),
        Accent = ThemeColor.FromRgb(0xFF3EA5),
        Accent2 = ThemeColor.FromRgb(0x2DE2E6),
        Border = ThemeColor.FromRgb(0xFF3EA5).WithAlpha(0.35),
        Backdrop = WindowBackdrop.Gradient,
        GradientAngle = 160,
        PanelOpacity = 0.72,
        CornerRadius = 6,
        Buttons = ButtonShape.Rounded,
        BorderWidth = 1,
        PanelGap = 10,
        Shadow = ShadowStyle.Glow,
        DisplayFont = "Bahnschrift",
        TextFont = "Bahnschrift",
        PlayerLayout = PlayerLayout.Floating,
        Progress = ProgressStyle.Gradient,
        PlayButton = PlayButtonStyle.Outline,
        Cover = CoverStyle.Vinyl,
    };

    /// <summary>A printed page: warm paper, ink outlines, serif type and hard shadows.</summary>
    public static ThemeDefinition Paper { get; } = new()
    {
        Id = "paper",
        Name = "Paper",
        Background = ThemeColor.FromRgb(0xEDE6D8),
        Background2 = ThemeColor.FromRgb(0xE3D9C6),
        Sidebar = ThemeColor.FromRgb(0xF5F0E6),
        Surface = ThemeColor.FromRgb(0xFBF8F1),
        Player = ThemeColor.FromRgb(0xF5F0E6),
        Text = ThemeColor.FromRgb(0x1F1A14),
        Accent = ThemeColor.FromRgb(0xC4532D),
        Accent2 = ThemeColor.FromRgb(0x2F6B5A),
        Border = ThemeColor.FromRgb(0x1F1A14),
        CornerRadius = 0,
        Buttons = ButtonShape.Square,
        BorderWidth = 1.5,
        PanelGap = 12,
        Shadow = ShadowStyle.Hard,
        DisplayFont = "Sitka Display, Georgia",
        TextFont = "Sitka Text, Georgia",
        PlayerLayout = PlayerLayout.Docked,
        Progress = ProgressStyle.Wave,
        Cover = CoverStyle.Square,
    };

    /// <summary>Windows 11's own material: Mica behind calm, slightly see-through panels.</summary>
    public static ThemeDefinition Fluent { get; } = new()
    {
        Id = "fluent",
        Name = "Fluent",
        Background = ThemeColor.FromRgb(0x202020),
        Background2 = ThemeColor.FromRgb(0x2B2B2B),
        Sidebar = ThemeColor.FromRgb(0x272727),
        Surface = ThemeColor.FromRgb(0x2C2C2C),
        Player = ThemeColor.FromRgb(0x242424),
        Text = ThemeColor.FromRgb(0xFFFFFF),
        Accent = ThemeColor.FromRgb(0x4CC2FF),
        Accent2 = ThemeColor.FromRgb(0x99EBFF),
        Border = ThemeColor.White.WithAlpha(0.08),
        Backdrop = WindowBackdrop.Mica,
        BackdropTint = 0.5,
        PanelOpacity = 0.7,
        CornerRadius = 8,
        Buttons = ButtonShape.Rounded,
        BorderWidth = 1,
        PanelGap = 6,
        Shadow = ShadowStyle.Soft,
        PlayerLayout = PlayerLayout.Docked,
        Progress = ProgressStyle.Line,
    };

    /// <summary>A mixing desk: graphite panels, orange like a console's lights, the player along the top.</summary>
    public static ThemeDefinition Studio { get; } = new()
    {
        Id = "studio",
        Name = "Studio",
        Background = ThemeColor.FromRgb(0x141517),
        Background2 = ThemeColor.FromRgb(0x222327),
        Sidebar = ThemeColor.FromRgb(0x1C1D20),
        Surface = ThemeColor.FromRgb(0x202124),
        Player = ThemeColor.FromRgb(0x2A2B2F),
        Text = ThemeColor.FromRgb(0xECEAE6),
        Accent = ThemeColor.FromRgb(0xFF7A1A),
        Accent2 = ThemeColor.FromRgb(0xFFD166),
        Border = ThemeColor.FromRgb(0x34363B),
        CornerRadius = 4,
        Buttons = ButtonShape.Rounded,
        BorderWidth = 1,
        PanelGap = 8,
        Shadow = ShadowStyle.Strong,
        DisplayFont = "Space Grotesk",
        TextFont = "Inter",
        PlayerLayout = PlayerLayout.FloatingTop,
        Progress = ProgressStyle.Bold,
        Cover = CoverStyle.Square,
    };

    /// <summary>Pastel and soft: a pink and lilac gradient, very round shapes and a small player in the corner.</summary>
    public static ThemeDefinition Bubblegum { get; } = new()
    {
        Id = "bubblegum",
        Name = "Bubblegum",
        Background = ThemeColor.FromRgb(0xFFE3F1),
        Background2 = ThemeColor.FromRgb(0xE2E0FF),
        Sidebar = ThemeColor.FromRgb(0xFFF6FB),
        Surface = ThemeColor.FromRgb(0xFFFFFF),
        Player = ThemeColor.FromRgb(0xFFFFFF),
        Text = ThemeColor.FromRgb(0x2A1631),
        Accent = ThemeColor.FromRgb(0xE8337F),
        Accent2 = ThemeColor.FromRgb(0x7C5CFF),
        Backdrop = WindowBackdrop.Gradient,
        GradientAngle = 135,
        CornerRadius = 28,
        PanelGap = 14,
        Shadow = ShadowStyle.Glow,
        DisplayFont = "Nunito",
        TextFont = "Nunito",
        PlayerLayout = PlayerLayout.Corner,
        Progress = ProgressStyle.Gradient,
        Cover = CoverStyle.Vinyl,
    };

    /// <summary>A phosphor screen on true black: glowing green type and outlines, amber highlights, monospace and the player across the top.</summary>
    public static ThemeDefinition Terminal { get; } = new()
    {
        Id = "terminal",
        Name = "Terminal",
        Background = ThemeColor.Black,
        Background2 = ThemeColor.FromRgb(0x021006),
        Sidebar = ThemeColor.Black,
        Surface = ThemeColor.Black,
        Player = ThemeColor.Black,
        Text = ThemeColor.FromRgb(0xB8FFC9),
        Accent = ThemeColor.FromRgb(0x33FF77),
        Accent2 = ThemeColor.FromRgb(0xFFB547),
        Border = ThemeColor.FromRgb(0x33FF77).WithAlpha(0.45),
        CornerRadius = 2,
        Buttons = ButtonShape.Square,
        BorderWidth = 1,
        PanelGap = 10,
        Shadow = ShadowStyle.Glow,
        DisplayFont = "Geist Mono",
        TextFont = "Geist Mono",
        PlayerLayout = PlayerLayout.Top,
        Progress = ProgressStyle.Bold,
        PlayButton = PlayButtonStyle.Outline,
        Cover = CoverStyle.Square,
    };

    /// <summary>Dark looks, the default first.</summary>
    public static IReadOnlyList<ThemeDefinition> Dark { get; } = [Midnight, Glass, Fluent, Studio, Synthwave];

    /// <summary>Light looks, the default first.</summary>
    public static IReadOnlyList<ThemeDefinition> Light { get; } = [Daylight, Paper, Bubblegum];

    /// <summary>True-black looks for OLED screens, the default first.</summary>
    public static IReadOnlyList<ThemeDefinition> Black { get; } = [Oled, PureBlack, Terminal];
    public static IReadOnlyList<ThemeDefinition> All { get; } = [.. Dark, .. Light, .. Black];

    public static ThemeDefinition Default => Midnight;

    public static ThemeDefinition? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    public static bool IsPreset(string? id) => Find(id) is not null;
}

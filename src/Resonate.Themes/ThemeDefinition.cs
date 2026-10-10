using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>What fills the window behind the panels.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WindowBackdrop>))]
public enum WindowBackdrop
{
    /// <summary>The background colour.</summary>
    Solid,

    /// <summary>A gradient from the background colour to the second background colour.</summary>
    Gradient,

    /// <summary>
    /// The playing song's cover under the background colour: a soft wash of
    /// its colours, or the cover itself, blurred, once the user allows that
    /// (an app setting, not part of the look).
    /// </summary>
    Artwork,

    /// <summary>Windows' Mica material (tinted by the desktop wallpaper).</summary>
    Mica,

    /// <summary>Windows' acrylic material (the desktop shows through, blurred).</summary>
    Acrylic,
}

[JsonConverter(typeof(JsonStringEnumConverter<ButtonShape>))]
public enum ButtonShape
{
    Round,
    Rounded,
    Square,
}

[JsonConverter(typeof(JsonStringEnumConverter<ShadowStyle>))]
public enum ShadowStyle
{
    None,

    /// <summary>A soft drop shadow.</summary>
    Soft,

    /// <summary>A deeper drop shadow, so panels float.</summary>
    Strong,

    /// <summary>A coloured glow in the accent colour.</summary>
    Glow,

    /// <summary>A solid offset shadow with no blur (neo-brutalist print look).</summary>
    Hard,
}

[JsonConverter(typeof(JsonStringEnumConverter<PlayerLayout>))]
public enum PlayerLayout
{
    /// <summary>Across the bottom of the window.</summary>
    Docked,

    /// <summary>A rounded bar floating above the bottom edge.</summary>
    Floating,

    /// <summary>
    /// A centred pill hovering over the bottom of the page, which scrolls on
    /// underneath it (pages leave room at their end, so nothing stays hidden).
    /// </summary>
    Hovering,

    // Saved by name: new layouts go at the end, and none is ever renamed.

    /// <summary>Across the top of the window, under the title bar.</summary>
    Top,

    /// <summary>A rounded bar floating along the top, under the title bar.</summary>
    FloatingTop,

    /// <summary>
    /// A small pill hovering in the page's bottom-right corner: the mini bar,
    /// over the page like <see cref="Hovering"/>.
    /// </summary>
    Corner,

    /// <summary>A column on the left of the page: the cover, the song and the mini player.</summary>
    Left,

    /// <summary>A column on the right of the page: the cover, the song and the mini player.</summary>
    Right,

    /// <summary>Like <see cref="Left"/>, as a card with a gap all round.</summary>
    InsetLeft,

    /// <summary>Like <see cref="Right"/>, as a card with a gap all round.</summary>
    InsetRight,

    /// <summary>A centred pill hovering over the top of the page.</summary>
    HoveringTop,

    /// <summary>A small pill hovering in the page's bottom-left corner, like <see cref="Corner"/>.</summary>
    CornerLeft,

    /// <summary>
    /// A minimal player at the foot of the sidebar, under the playlists: the
    /// song, then previous, play, next and the progress. Like
    /// <see cref="Floating"/> while the sidebar shows only covers.
    /// </summary>
    Sidebar,
}

/// <summary>How a visualizer draws the sound (saved by name: append, never rename).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VisualizerStyle>))]
public enum VisualizerStyle
{
    /// <summary>Bars rising from the bottom.</summary>
    Bars,

    /// <summary>Bars growing both ways from a middle line.</summary>
    Mirror,

    /// <summary>Retired on 9 October 2026 (the owner found it dated): read as <see cref="Pills"/>.</summary>
    Dots,

    /// <summary>Hairline bars.</summary>
    Lines,

    /// <summary>No visualizer (for the player bar).</summary>
    Off,

    /// <summary>A hi-fi meter: columns of lit segments, with peaks that fall back slowly.</summary>
    Retro,

    /// <summary>Retired on 9 October 2026: read as <see cref="Silk"/>.</summary>
    Wave,

    /// <summary>Retired on 9 October 2026: read as <see cref="Silk"/>.</summary>
    Helix,

    /// <summary>Retired on 9 October 2026: read as <see cref="Pulse"/>.</summary>
    Radial,

    /// <summary>Rings around the cover that swell with the lows, mids and highs, over a glow (Home only).</summary>
    Pulse,

    /// <summary>Retired on 9 October 2026: read as <see cref="Aurora"/>.</summary>
    Embers,

    /// <summary>Rounded capsules growing both ways from a middle line, like a modern voice waveform.</summary>
    Pills,

    /// <summary>A soft ribbon of light, mirrored about a middle line, smooth from band to band.</summary>
    Silk,

    /// <summary>Large soft glows along the bottom that swell and drift with the lows, mids and highs.</summary>
    Aurora,
}

/// <summary>
/// The scenery a special look draws (saved by name: append, never rename):
/// art behind the panels and weather drifting over the window.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemeScene>))]
public enum ThemeScene
{
    /// <summary>No scenery.</summary>
    None,

    /// <summary>A Japanese night: the moon, Mount Fuji, a pagoda, a torii gate, a cherry branch and falling sakura petals.</summary>
    Japan,

    /// <summary>A snowy night: stars, snow-capped mountains, a pine forest, snowdrifts and falling snow.</summary>
    Snow,

    /// <summary>An outrun sunset: a striped sun, neon mountains, palms and a glowing grid racing towards you, with shooting stars and rising neon dust.</summary>
    Synthwave,

    /// <summary>Liquid metal: a rolling chrome sea, mirrored blobs drifting over it, beads rising and mercury dripping from the panels.</summary>
    LiquidChrome,

    /// <summary>A neon megacity in the rain: towers, flickering signs, searchlights, flying cars, a blimp and corner brackets on the panels.</summary>
    Cyberpunk,

    /// <summary>A city after midnight through a rainy window: soft city lights, a skyline, a highway of tail lights and drops running down the glass.</summary>
    Afterhours,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProgressStyle>))]
public enum ProgressStyle
{
    /// <summary>A slim line; the handle shows on hover.</summary>
    Line,

    /// <summary>A thick bar with a handle.</summary>
    Bold,

    /// <summary>A glowing gradient from the accent to the second accent.</summary>
    Gradient,

    /// <summary>A moving wave while playing.</summary>
    Wave,

    /// <summary>A hairline with square ends and no handle.</summary>
    Minimal,

    /// <summary>A bar that a soft light runs along while playing.</summary>
    Shimmer,

    /// <summary>Retired (too like Wave, the owner found): saved looks read it as <see cref="Comet"/>.</summary>
    Liquid,

    /// <summary>A row of dots that march along while playing.</summary>
    Dots,

    /// <summary>A line whose handle sends out rings while playing.</summary>
    Ripple,

    /// <summary>A heartbeat trace that runs along while playing.</summary>
    Heartbeat,

    /// <summary>A comet: a fading tail behind a glowing head, with sparks that twinkle while playing.</summary>
    Comet,
}

[JsonConverter(typeof(JsonStringEnumConverter<PlayButtonStyle>))]
public enum PlayButtonStyle
{
    Filled,
    Outline,
    Plain,
}

[JsonConverter(typeof(JsonStringEnumConverter<CoverStyle>))]
public enum CoverStyle
{
    Rounded,
    Square,

    /// <summary>
    /// A round cover with a record's centre. It stays still; it turns while
    /// a song plays only when the user switches on the spinning cover (an
    /// app setting, which makes every look's cover a record).
    /// </summary>
    Vinyl,
}

/// <summary>A starting set of neutral colours for a custom look.</summary>
public enum ThemeBase
{
    Light,
    Dark,
    Black,
}

/// <summary>
/// A complete look: a few colours the user picks, plus materials, shapes,
/// fonts and how the player is drawn. Everything else (hover colours,
/// secondary text, the colour on the accent) is derived in
/// <see cref="ThemePalette"/>, so any combination stays readable. Presets
/// are in <see cref="ThemePresets"/>; saved looks are the user's own.
/// </summary>
public sealed record ThemeDefinition
{
    public const double MaxCornerRadius = 32;
    public const double MaxBorderWidth = 4;
    public const double MaxPanelGap = 24;
    public const int MaxNameLength = 40;
    public const int MaxFontLength = 100;

    // The player's own size and offset, as Settings, Layout, Advanced allows them.
    public const double MinPlayerWidth = 200;
    public const double MaxPlayerWidth = 4000;
    public const double MinPlayerHeight = 48;
    public const double MaxPlayerHeight = 400;
    public const double MaxPlayerOffset = 4000;

    public const string DefaultDisplayFont = "Segoe UI Variable Display";
    public const string DefaultTextFont = "Segoe UI Variable Text";

    /// <summary>A preset's id, or "look-..." for a saved look, or "custom".</summary>
    public string Id { get; init; } = "custom";

    public string Name { get; init; } = "Custom";

    /// <summary>The preset a custom or saved look was made from (null for a preset, or a look shared from elsewhere): what Settings' reset buttons put back.</summary>
    public string? BasedOn { get; init; }

    // Colours

    /// <summary>The window behind the panels.</summary>
    public ThemeColor Background { get; init; } = ThemeColor.FromRgb(0x0F1014);

    /// <summary>The end of the background gradient.</summary>
    public ThemeColor Background2 { get; init; } = ThemeColor.FromRgb(0x1C1730);

    /// <summary>The library panel on the left.</summary>
    public ThemeColor Sidebar { get; init; } = ThemeColor.FromRgb(0x14151A);

    /// <summary>The page panel.</summary>
    public ThemeColor Surface { get; init; } = ThemeColor.FromRgb(0x1A1C22);

    /// <summary>The player bar.</summary>
    public ThemeColor Player { get; init; } = ThemeColor.FromRgb(0x0F1014);

    public ThemeColor Text { get; init; } = ThemeColor.FromRgb(0xF2F3F5);

    public ThemeColor Accent { get; init; } = ThemeColor.FromRgb(0x8B7CFF);

    /// <summary>The partner of the accent in gradients.</summary>
    public ThemeColor Accent2 { get; init; } = ThemeColor.FromRgb(0x5CC8FF);

    /// <summary>Panel outlines; when not set, a faint line in the text colour.</summary>
    public ThemeColor? Border { get; init; }

    // Materials

    public WindowBackdrop Backdrop { get; init; } = WindowBackdrop.Solid;

    /// <summary>How strongly the background colour covers the cover art or material (0 to 1).</summary>
    public double BackdropTint { get; init; } = 0.6;

    /// <summary>1 for solid panels; lower lets the background show through like glass.</summary>
    public double PanelOpacity { get; init; } = 1;

    /// <summary>Take the accent colour from the playing song's cover.</summary>
    public bool AdaptiveAccent { get; init; }

    /// <summary>Direction of the background gradient, in degrees (0 is left to right).</summary>
    public double GradientAngle { get; init; } = 135;

    // Shapes

    /// <summary>Roundness of panels; smaller parts use a share of it.</summary>
    public double CornerRadius { get; init; } = 12;

    public ButtonShape Buttons { get; init; } = ButtonShape.Round;

    public double BorderWidth { get; init; }

    /// <summary>Space between and around the panels.</summary>
    public double PanelGap { get; init; } = 8;

    public ShadowStyle Shadow { get; init; } = ShadowStyle.Soft;

    // Type

    /// <summary>Headings. Any installed font; a missing one falls back to Segoe UI.</summary>
    public string DisplayFont { get; init; } = DefaultDisplayFont;

    public string TextFont { get; init; } = DefaultTextFont;

    // The player

    public PlayerLayout PlayerLayout { get; init; } = PlayerLayout.Docked;

    /// <summary>The player's own width (Settings, Layout, Advanced); null for the layout's.</summary>
    public double? PlayerWidth { get; init; }

    /// <summary>The player bar's own height; null for the layout's.</summary>
    public double? PlayerHeight { get; init; }

    /// <summary>How far the player is moved right from its place (negative: left).</summary>
    public double? PlayerOffsetX { get; init; }

    /// <summary>How far the player is moved down from its place (negative: up).</summary>
    public double? PlayerOffsetY { get; init; }

    /// <summary>A glow of the accent around the player, 0 (none) to 1.</summary>
    public double PlayerGlow { get; init; }

    /// <summary>A glow of the accent under the played part of the progress bar, 0 (none) to 1.</summary>
    public double ProgressGlow { get; init; }

    /// <summary>How Home's visualizer draws the sound.</summary>
    public VisualizerStyle StageVisualizer { get; init; } = VisualizerStyle.Bars;

    /// <summary>A visualizer along the player bar; Off for none.</summary>
    public VisualizerStyle PlayerVisualizer { get; init; } = VisualizerStyle.Off;

    /// <summary>The scenery of a special look; None for the others.</summary>
    public ThemeScene Scene { get; init; }

    public ProgressStyle Progress { get; init; } = ProgressStyle.Line;

    public PlayButtonStyle PlayButton { get; init; } = PlayButtonStyle.Filled;

    public CoverStyle Cover { get; init; } = CoverStyle.Rounded;

    /// <summary>
    /// Brings every value into its allowed range, so a look pasted from
    /// anywhere (or a damaged settings file) can never break the window.
    /// </summary>
    public ThemeDefinition Normalize() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "custom" : Clean(Id, 64),
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Clean(Name, MaxNameLength),
        BackdropTint = Clamp(BackdropTint, 0, 1, 0.6),
        PanelOpacity = Clamp(PanelOpacity, 0, 1, 1),
        GradientAngle = Clamp(GradientAngle, 0, 360, 135),
        CornerRadius = Clamp(CornerRadius, 0, MaxCornerRadius, 12),
        BorderWidth = Clamp(BorderWidth, 0, MaxBorderWidth, 0),
        PanelGap = Clamp(PanelGap, 0, MaxPanelGap, 8),
        PlayerWidth = ClampOrNull(PlayerWidth, MinPlayerWidth, MaxPlayerWidth),
        PlayerHeight = ClampOrNull(PlayerHeight, MinPlayerHeight, MaxPlayerHeight),
        PlayerOffsetX = ClampOrNull(PlayerOffsetX, -MaxPlayerOffset, MaxPlayerOffset),
        PlayerOffsetY = ClampOrNull(PlayerOffsetY, -MaxPlayerOffset, MaxPlayerOffset),
        DisplayFont = string.IsNullOrWhiteSpace(DisplayFont) ? DefaultDisplayFont : Clean(DisplayFont, MaxFontLength),
        TextFont = string.IsNullOrWhiteSpace(TextFont) ? DefaultTextFont : Clean(TextFont, MaxFontLength),
        Backdrop = Enum.IsDefined(Backdrop) ? Backdrop : WindowBackdrop.Solid,
        Buttons = Enum.IsDefined(Buttons) ? Buttons : ButtonShape.Round,
        Shadow = Enum.IsDefined(Shadow) ? Shadow : ShadowStyle.Soft,
        PlayerLayout = Enum.IsDefined(PlayerLayout) ? PlayerLayout : PlayerLayout.Docked,
        PlayerGlow = double.IsFinite(PlayerGlow) ? Math.Clamp(PlayerGlow, 0, 1) : 0,
        ProgressGlow = double.IsFinite(ProgressGlow) ? Math.Clamp(ProgressGlow, 0, 1) : 0,
        StageVisualizer = Enum.IsDefined(StageVisualizer) && StageVisualizer != VisualizerStyle.Off ? VisualizerShapes.Current(StageVisualizer) : VisualizerStyle.Bars,
        PlayerVisualizer = Enum.IsDefined(PlayerVisualizer) && VisualizerShapes.FitsPlayerBar(VisualizerShapes.Current(PlayerVisualizer)) ? VisualizerShapes.Current(PlayerVisualizer) : VisualizerStyle.Off,
        Scene = Enum.IsDefined(Scene) ? Scene : ThemeScene.None,
        Progress = ProgressPatterns.Current(Progress),
        PlayButton = Enum.IsDefined(PlayButton) ? PlayButton : PlayButtonStyle.Filled,
        Cover = Enum.IsDefined(Cover) ? Cover : CoverStyle.Rounded,
    };

    /// <summary>
    /// Replaces the neutral colours (background, panels, text) with a light,
    /// dark or true-black set, keeping the accents and everything else.
    /// </summary>
    public ThemeDefinition WithBase(ThemeBase themeBase) => themeBase switch
    {
        ThemeBase.Light => this with
        {
            Background = ThemeColor.FromRgb(0xEEF0F4),
            Background2 = ThemeColor.FromRgb(0xE4E1F5),
            Sidebar = ThemeColor.FromRgb(0xF8F9FB),
            Surface = ThemeColor.FromRgb(0xFFFFFF),
            Player = ThemeColor.FromRgb(0xFFFFFF),
            Text = ThemeColor.FromRgb(0x15161A),
            Border = null,
        },
        ThemeBase.Black => this with
        {
            Background = ThemeColor.Black,
            Background2 = ThemeColor.FromRgb(0x101014),
            Sidebar = ThemeColor.Black,
            Surface = ThemeColor.Black,
            Player = ThemeColor.Black,
            Text = ThemeColor.White,
            Border = null,
        },
        _ => this with
        {
            Background = ThemeColor.FromRgb(0x0F1014),
            Background2 = ThemeColor.FromRgb(0x1C1730),
            Sidebar = ThemeColor.FromRgb(0x14151A),
            Surface = ThemeColor.FromRgb(0x1A1C22),
            Player = ThemeColor.FromRgb(0x0F1014),
            Text = ThemeColor.FromRgb(0xF2F3F5),
            Border = null,
        },
    };

    /// <summary>Whether two looks differ in more than colour (shapes, fonts, layout or materials).</summary>
    public bool HasSameStructure(ThemeDefinition other) =>
        Backdrop == other.Backdrop
        && Math.Abs(CornerRadius - other.CornerRadius) < 0.01
        && Buttons == other.Buttons
        && Math.Abs(BorderWidth - other.BorderWidth) < 0.01
        && Math.Abs(PanelGap - other.PanelGap) < 0.01
        && Shadow == other.Shadow
        && DisplayFont == other.DisplayFont
        && TextFont == other.TextFont
        && PlayerLayout == other.PlayerLayout
        && PlayerWidth == other.PlayerWidth
        && PlayerHeight == other.PlayerHeight
        && PlayerOffsetX == other.PlayerOffsetX
        && PlayerOffsetY == other.PlayerOffsetY
        && Progress == other.Progress
        && PlayButton == other.PlayButton
        && Cover == other.Cover;

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static double? ClampOrNull(double? value, double min, double max) =>
        value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : null;

    private static string Clean(string text, int maxLength)
    {
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }
}

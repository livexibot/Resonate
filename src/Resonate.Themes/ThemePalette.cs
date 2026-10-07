namespace Resonate.Themes;

/// <summary>A shadow, ready for the compositor: blur, offset, colour and strength.</summary>
public readonly record struct ShadowSpec(double BlurRadius, double OffsetX, double OffsetY, ThemeColor Color)
{
    public static readonly ShadowSpec None = new(0, 0, 0, ThemeColor.Transparent);

    public bool IsVisible => Color.A > 0;
}

/// <summary>
/// Every colour and size the interface reads, worked out from a
/// <see cref="ThemeDefinition"/>. The user picks eight colours; the rest
/// (hover and pressed fills, secondary text, the colour drawn on the accent,
/// the progress track) follows from them so any combination stays readable.
/// </summary>
public sealed record ThemePalette
{
    /// <summary>Light controls (dark text) or dark controls (light text).</summary>
    public required bool IsLight { get; init; }

    public required ThemeColor Background { get; init; }

    public required ThemeColor Background2 { get; init; }

    /// <summary>The background colour as drawn over cover art or a Windows material.</summary>
    public required ThemeColor BackdropTint { get; init; }

    public required ThemeColor Sidebar { get; init; }

    public required ThemeColor Surface { get; init; }

    public required ThemeColor Player { get; init; }

    /// <summary>Drawn over anything to show the pointer is on it.</summary>
    public required ThemeColor Hover { get; init; }

    public required ThemeColor Pressed { get; init; }

    /// <summary>The quiet fill of secondary buttons and fields.</summary>
    public required ThemeColor Control { get; init; }

    public required ThemeColor Border { get; init; }

    public required ThemeColor TextPrimary { get; init; }

    public required ThemeColor TextSecondary { get; init; }

    public required ThemeColor TextTertiary { get; init; }

    public required ThemeColor Accent { get; init; }

    public required ThemeColor AccentHover { get; init; }

    public required ThemeColor AccentPressed { get; init; }

    /// <summary>Text and icons drawn on the accent colour.</summary>
    public required ThemeColor OnAccent { get; init; }

    public required ThemeColor Accent2 { get; init; }

    /// <summary>The unplayed part of the progress and volume bars.</summary>
    public required ThemeColor Track { get; init; }

    public required ThemeColor PlayButtonBackground { get; init; }

    public required ThemeColor PlayButtonHover { get; init; }

    public required ThemeColor PlayButtonForeground { get; init; }

    public required ThemeColor PlayButtonBorder { get; init; }

    // Sizes

    public required double CornerSmall { get; init; }

    public required double CornerMedium { get; init; }

    public required double CornerLarge { get; init; }

    /// <summary>Corner radius of buttons (999 draws a pill).</summary>
    public required double CornerButton { get; init; }

    public required double BorderWidth { get; init; }

    public required double PlayButtonBorderWidth { get; init; }

    public required double PanelGap { get; init; }

    public required ShadowSpec PanelShadow { get; init; }

    /// <summary>The shadow under small things: covers and the play button.</summary>
    public required ShadowSpec ItemShadow { get; init; }

    public static ThemePalette From(ThemeDefinition theme, ThemeColor? accentOverride = null)
    {
        theme = theme.Normalize();

        // What the panels actually look like once the background shows through.
        var background = theme.Background.Opaque;
        var panelOpacity = theme.PanelOpacity;
        var surface = theme.Surface.Opaque.WithAlpha(panelOpacity);
        var effectiveSurface = surface.Over(background);
        var isLight = effectiveSurface.IsLight;

        var text = EnsureContrast(theme.Text.Opaque, effectiveSurface, 4.5);
        var accent = EnsureContrast((accentOverride ?? theme.Accent).Opaque, effectiveSurface, 2.4);
        var accent2 = theme.Accent2.Opaque;
        var onAccent = ThemeColor.ContrastRatio(accent, ThemeColor.White) >= ThemeColor.ContrastRatio(accent, Ink)
            ? ThemeColor.White
            : Ink;

        var hover = text.WithAlpha(isLight ? 0.06 : 0.08);
        var pressed = text.WithAlpha(isLight ? 0.10 : 0.12);
        var border = theme.Border ?? text.WithAlpha(isLight ? 0.13 : 0.10);

        var radius = theme.CornerRadius;
        var cornerButton = theme.Buttons switch
        {
            ButtonShape.Round => 999,
            ButtonShape.Rounded => Math.Clamp(radius * 0.5, 4, 12),
            _ => Math.Min(radius * 0.25, 3),
        };

        var (playBackground, playHover, playForeground, playBorder, playBorderWidth) = theme.PlayButton switch
        {
            PlayButtonStyle.Outline => (ThemeColor.Transparent, accent.WithAlpha(0.16), accent, accent, 1.5),
            PlayButtonStyle.Plain => (ThemeColor.Transparent, hover, text, ThemeColor.Transparent, 0.0),
            _ => (accent, AccentHoverFor(accent, isLight), onAccent, ThemeColor.Transparent, 0.0),
        };

        return new ThemePalette
        {
            IsLight = isLight,
            Background = background,
            Background2 = theme.Background2.Opaque,
            BackdropTint = background.WithAlpha(theme.BackdropTint),
            Sidebar = theme.Sidebar.Opaque.WithAlpha(panelOpacity),
            Surface = surface,
            Player = theme.Player.Opaque.WithAlpha(panelOpacity),
            Hover = hover,
            Pressed = pressed,
            Control = text.WithAlpha(isLight ? 0.04 : 0.06),
            Border = border,
            TextPrimary = text,
            TextSecondary = text.Mix(effectiveSurface, 0.33),
            TextTertiary = text.Mix(effectiveSurface, 0.55),
            Accent = accent,
            AccentHover = AccentHoverFor(accent, isLight),
            AccentPressed = isLight ? accent.Mix(ThemeColor.Black, 0.18) : accent.Mix(ThemeColor.Black, 0.12),
            OnAccent = onAccent,
            Accent2 = accent2,
            Track = text.WithAlpha(isLight ? 0.16 : 0.18),
            PlayButtonBackground = playBackground,
            PlayButtonHover = playHover,
            PlayButtonForeground = playForeground,
            PlayButtonBorder = playBorder,
            CornerSmall = Math.Round(radius * 0.4, 1),
            CornerMedium = Math.Round(radius * 0.66, 1),
            CornerLarge = radius,
            CornerButton = cornerButton,
            BorderWidth = theme.BorderWidth,
            PlayButtonBorderWidth = playBorderWidth,
            PanelGap = theme.PanelGap,
            PanelShadow = Shadow(theme.Shadow, isLight, accent, text, large: true),
            ItemShadow = Shadow(theme.Shadow, isLight, accent, text, large: false),
        };
    }

    /// <summary>A near-black for text on light accents (pure black looks harsh).</summary>
    private static ThemeColor Ink { get; } = ThemeColor.FromRgb(0x0B0B10);

    private static ThemeColor AccentHoverFor(ThemeColor accent, bool isLight) =>
        isLight ? accent.Mix(ThemeColor.Black, 0.08) : accent.Mix(ThemeColor.White, 0.14);

    private static ShadowSpec Shadow(ShadowStyle style, bool isLight, ThemeColor accent, ThemeColor text, bool large)
    {
        var scale = large ? 1.0 : 0.6;
        return style switch
        {
            ShadowStyle.Soft => new ShadowSpec(24 * scale, 0, 6 * scale, ThemeColor.Black.WithAlpha(isLight ? 0.14 : 0.40)),
            ShadowStyle.Strong => new ShadowSpec(44 * scale, 0, 14 * scale, ThemeColor.Black.WithAlpha(isLight ? 0.26 : 0.62)),
            ShadowStyle.Glow => new ShadowSpec(30 * scale, 0, 0, accent.WithAlpha(large ? 0.30 : 0.60)),
            ShadowStyle.Hard => new ShadowSpec(0, 5 * scale, 5 * scale, text),
            _ => ShadowSpec.None,
        };
    }

    /// <summary>
    /// Nudges a colour lighter or darker until it reaches the contrast ratio
    /// against <paramref name="background"/>, keeping its hue.
    /// </summary>
    internal static ThemeColor EnsureContrast(ThemeColor color, ThemeColor background, double ratio)
    {
        if (ThemeColor.ContrastRatio(color, background) >= ratio)
        {
            return color;
        }

        var target = background.IsLight ? ThemeColor.Black : ThemeColor.White;
        for (var step = 1; step <= 20; step++)
        {
            var candidate = color.Mix(target, step / 20.0);
            if (ThemeColor.ContrastRatio(candidate, background) >= ratio)
            {
                return candidate;
            }
        }

        return target;
    }
}

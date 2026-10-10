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

    /// <summary>
    /// The page as drawn over the backdrop text reads worst over: for
    /// see-through panels the gradient's other end, the cover at the most
    /// <see cref="ArtworkColors.DimForWhiteText"/> leaves it or the lightest
    /// scenery (see <see cref="HardestBackdrop"/>); for opaque ones the page.
    /// </summary>
    public required ThemeColor LightestPage { get; init; }

    /// <summary>
    /// The page behind a header's glow at its lightest: as <see cref="LightestPage"/>
    /// without the scenery, which lies lower in the window or is small where
    /// headers sit (the top of the page, over the sky).
    /// </summary>
    private ThemeColor HeaderPage { get; init; }

    public required ThemeColor Player { get; init; }

    /// <summary>Drawn over anything to show the pointer is on it.</summary>
    public required ThemeColor Hover { get; init; }

    public required ThemeColor Pressed { get; init; }

    /// <summary>The quiet fill of secondary buttons and fields.</summary>
    public required ThemeColor Control { get; init; }

    public required ThemeColor Border { get; init; }

    /// <summary>
    /// The outline of text fields (search, filter, names): 3:1 against the
    /// page, so a field still shows once it holds text and lost focus.
    /// Panels and buttons keep the quiet <see cref="Border"/>.
    /// </summary>
    public required ThemeColor FieldBorder { get; init; }

    public required ThemeColor TextPrimary { get; init; }

    public required ThemeColor TextSecondary { get; init; }

    public required ThemeColor TextTertiary { get; init; }

    public required ThemeColor Accent { get; init; }

    /// <summary>
    /// The accent for small text (a playing song's title, the sung lyric
    /// line): the accent itself where it already reads at 4.5:1, otherwise a
    /// shade of it that does, on the page, a row under the pointer, the
    /// selected row and the player. Buttons and bars keep <see cref="Accent"/>.
    /// </summary>
    public required ThemeColor AccentText { get; init; }

    /// <summary>The bars of Home's charts other than the one for now: the accent, just strong enough for 3:1 on the card.</summary>
    public required ThemeColor ChartQuiet { get; init; }

    public required ThemeColor AccentHover { get; init; }

    public required ThemeColor AccentPressed { get; init; }

    /// <summary>Text and icons drawn on the accent colour.</summary>
    public required ThemeColor OnAccent { get; init; }

    public required ThemeColor Accent2 { get; init; }

    /// <summary>
    /// A soft wash of the accent behind the selected row of a list (and the
    /// sidebar's selected link). It reads at least as well as the grey
    /// highlight it replaces (see <see cref="SoftAccent"/>).
    /// </summary>
    public required ThemeColor AccentSoft { get; init; }

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

    /// <summary>
    /// The light behind the player's play button: a glow in the accent colour
    /// for soft and deep shadows, neon for glowing looks, a hard offset copy
    /// for printed looks, and nothing for looks without shadows or a plain
    /// play button.
    /// </summary>
    public required ShadowSpec PlayButtonShadow { get; init; }

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

        // Controls drawn in the accent (the progress bar, switches, outline play buttons) keep 3:1.
        var accent = EnsureContrast((accentOverride ?? theme.Accent).Opaque, effectiveSurface, 3.0);
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

        var itemShadow = Shadow(theme.Shadow, isLight, accent, text, large: false);

        var (playBackground, playHover, playForeground, playBorder, playBorderWidth) = theme.PlayButton switch
        {
            PlayButtonStyle.Outline => (ThemeColor.Transparent, accent.WithAlpha(0.16), accent, accent, 1.5),
            PlayButtonStyle.Plain => (ThemeColor.Transparent, hover, text, ThemeColor.Transparent, 0.0),
            _ => (accent, AccentHoverFor(accent, isLight), onAccent, ThemeColor.Transparent, 0.0),
        };

        // The panels as drawn, and as drawn over the backdrop text reads worst
        // over (the same colours for opaque panels): the grey text keeps 4.5:1
        // and the icons 3:1 on all of them, and on a row under the pointer.
        var sidebarPanel = theme.Sidebar.Opaque.WithAlpha(panelOpacity);
        var shownSidebar = sidebarPanel.Over(background);
        var hardest = HardestBackdrop(theme, text, scenery: true);
        var lightestPage = surface.Over(hardest);
        ThemeColor[] panels = [effectiveSurface, shownSidebar, lightestPage, sidebarPanel.Over(hardest)];
        var secondary = Readable(text.Mix(effectiveSurface, 0.33), panels, 4.5);
        var tertiary = Readable(text.Mix(effectiveSurface, 0.55), [.. panels, hover.Over(effectiveSurface)], 3.0);
        var accentSoft = SoftAccent(accent, isLight, pressed, [text, secondary, tertiary], panels);
        var player = PlayerFill(theme, effectiveSurface);

        return new ThemePalette
        {
            IsLight = isLight,
            Background = background,
            Background2 = theme.Background2.Opaque,
            BackdropTint = background.WithAlpha(theme.BackdropTint),
            Sidebar = theme.Sidebar.Opaque.WithAlpha(panelOpacity),
            Surface = surface,
            LightestPage = lightestPage,
            HeaderPage = surface.Over(HardestBackdrop(theme, text, scenery: false)),
            Player = player,
            Hover = hover,
            Pressed = pressed,
            Control = text.WithAlpha(isLight ? 0.04 : 0.06),
            Border = border,
            FieldBorder = FieldBorderFor(theme.Border, text, effectiveSurface, lightestPage),
            TextPrimary = text,
            TextSecondary = secondary,
            TextTertiary = tertiary,
            Accent = accent,
            AccentText = Readable(accent, [effectiveSurface, lightestPage, hover.Over(effectiveSurface), accentSoft.Over(effectiveSurface), player.Over(background)], 4.5),
            ChartQuiet = ChartQuietFor(accent, hover.Over(effectiveSurface)),
            AccentHover = AccentHoverFor(accent, isLight),
            AccentPressed = isLight ? accent.Mix(ThemeColor.Black, 0.18) : accent.Mix(ThemeColor.Black, 0.12),
            OnAccent = onAccent,
            Accent2 = accent2,
            AccentSoft = accentSoft,
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
            ItemShadow = itemShadow,
            PlayButtonShadow = PlayButtonGlow(theme, isLight, accent, itemShadow),
        };
    }

    /// <summary>The lowest opacity of a hovering player, so rows scrolling underneath never show through behind its text.</summary>
    internal const double HoveringPlayerOpacity = 0.97;

    /// <summary>
    /// The player's fill. Docked and floating players are panels like the
    /// others. A hovering player sits over the page while it scrolls, so it
    /// is the panel as it looks over the background, made almost opaque:
    /// glass looks get smoky glass instead of a see-through bar over text.
    /// </summary>
    private static ThemeColor PlayerFill(ThemeDefinition theme, ThemeColor effectiveSurface)
    {
        var panel = theme.Player.Opaque.WithAlpha(theme.PanelOpacity);
        return PlayerPlacement.HoversOverPage(theme.PlayerLayout)
            ? panel.Over(effectiveSurface).WithAlpha(Math.Max(theme.PanelOpacity, HoveringPlayerOpacity))
            : panel;
    }

    /// <summary>
    /// The glow behind the play button. An outline button glows as a ring
    /// (the window draws it so), so its glow sits straight behind it.
    /// </summary>
    private static ShadowSpec PlayButtonGlow(ThemeDefinition theme, bool isLight, ThemeColor accent, ShadowSpec itemShadow)
    {
        if (theme.PlayButton == PlayButtonStyle.Plain)
        {
            return ShadowSpec.None;
        }

        var drop = theme.PlayButton == PlayButtonStyle.Filled ? 4 : 0;
        return theme.Shadow switch
        {
            ShadowStyle.Soft or ShadowStyle.Strong => new ShadowSpec(16, 0, drop, accent.WithAlpha(isLight ? 0.35 : 0.45)),
            ShadowStyle.Glow or ShadowStyle.Hard => itemShadow,
            _ => ShadowSpec.None,
        };
    }

    /// <summary>A near-black for text on light accents (pure black looks harsh).</summary>
    private static ThemeColor Ink { get; } = ThemeColor.FromRgb(0x0B0B10);

    private static ThemeColor AccentHoverFor(ThemeColor accent, bool isLight) =>
        isLight ? accent.Mix(ThemeColor.Black, 0.08) : accent.Mix(ThemeColor.White, 0.14);

    /// <summary>
    /// The glow of a cover's colour behind a page's header (it fades to
    /// nothing towards the list below). It starts strong on dark pages,
    /// softer on light, glass and true-black ones, and is lowered until every
    /// text on the page still reads over it as well as the presets promise:
    /// main text 7:1, secondary text (captions too) 4.5:1, icons 2.5:1 and
    /// the accent 2.4:1, over the page as drawn and, for see-through panels,
    /// over the lightest backdrop behind a header (<see cref="HeaderPage"/>).
    /// Transparent when even a faint glow would make text harder to read.
    /// </summary>
    public ThemeColor HeroTint(ThemeColor source)
    {
        var page = Surface.Over(Background);
        var strength = page == ThemeColor.Black ? 0.30
            : Surface.A < 0xFF ? 0.25
            : IsLight ? 0.24
            : 0.42;

        bool ReadsOver(ThemeColor shown) =>
            ThemeColor.ContrastRatio(TextPrimary, shown) >= 7
            && ThemeColor.ContrastRatio(TextSecondary, shown) >= 4.5
            && ThemeColor.ContrastRatio(TextTertiary, shown) >= 2.5
            && ThemeColor.ContrastRatio(Accent, shown) >= 2.4;

        // Whole steps, so the same cover always gets the same glow.
        for (var step = (int)Math.Round(strength * 100); step > 0; step -= 5)
        {
            var tint = source.Opaque.WithAlpha(step / 100.0);
            if (ReadsOver(tint.Over(page)) && ReadsOver(tint.Over(HeaderPage)))
            {
                return tint;
            }
        }

        return source.Opaque.WithAlpha(0);
    }

    /// <summary>
    /// What shows through see-through panels that text reads worst over:
    /// the background, or for a gradient its second colour, for the song
    /// cover the brightest cover <see cref="ArtworkColors.DimForWhiteText"/>
    /// leaves, and for a special look the lightest part of its scenery that
    /// lies behind text (<see cref="SceneryLight"/>; only with
    /// <paramref name="scenery"/>). Opaque panels hide it all, so for them it
    /// changes nothing.
    /// </summary>
    internal static ThemeColor HardestBackdrop(ThemeDefinition theme, ThemeColor text, bool scenery)
    {
        var background = theme.Background.Opaque;
        var panel = theme.Surface.Opaque.WithAlpha(theme.PanelOpacity);
        var hardest = background;
        void Consider(ThemeColor candidate)
        {
            if (ThemeColor.ContrastRatio(text, panel.Over(candidate)) < ThemeColor.ContrastRatio(text, panel.Over(hardest)))
            {
                hardest = candidate;
            }
        }

        switch (theme.Backdrop)
        {
            case WindowBackdrop.Gradient:
                Consider(theme.Background2.Opaque);
                break;
            case WindowBackdrop.Artwork:
                Consider(ArtworkColors.BrightestCover(theme.PanelOpacity));
                break;
        }

        if (scenery && SceneryLight(theme.Scene) is { } light)
        {
            Consider(light);
        }

        return hardest;
    }

    /// <summary>
    /// The lightest scenery behind the panels' text in each special look:
    /// the moon over Japan, Snow's snowfields and far ranges, Synthwave's
    /// sun, Liquid Chrome's blobs and waves, Cyberpunk's lit towers and
    /// Afterhours' city glow. Measured under the sidebar's and the song
    /// list's text in CI's screenshots of v0.17.0 (the 99.9th percentile
    /// of the panel colour behind text, worked back through the panel), a
    /// little lighter for safety. A few small bright specks (stars, lit
    /// windows, the cabin, tail lights) are lighter still.
    /// </summary>
    internal static ThemeColor? SceneryLight(ThemeScene scene) => scene switch
    {
        ThemeScene.Japan => ThemeColor.FromRgb(0x857866),
        ThemeScene.Snow => ThemeColor.FromRgb(0x749CC4),
        ThemeScene.Synthwave => ThemeColor.FromRgb(0xFFB27E),
        ThemeScene.LiquidChrome => ThemeColor.FromRgb(0x5E6066),
        ThemeScene.Cyberpunk => ThemeColor.FromRgb(0x2C6C8C),
        ThemeScene.Afterhours => ThemeColor.FromRgb(0x5A2A28),
        _ => null,
    };

    /// <summary>
    /// Nudges <paramref name="color"/> until it reaches <paramref name="ratio"/>
    /// against every one of <paramref name="backgrounds"/>. Every nudge goes
    /// the way the first one (the page) needs, so a backdrop of the other
    /// lightness (a custom look's bright gradient behind dark glass) can never
    /// push the text into the page; it is then as readable there as it can be.
    /// </summary>
    private static ThemeColor Readable(ThemeColor color, ThemeColor[] backgrounds, double ratio)
    {
        var target = backgrounds[0].Opaque.IsLight ? ThemeColor.Black : ThemeColor.White;
        foreach (var background in backgrounds)
        {
            var shown = background.Opaque;
            if (ThemeColor.ContrastRatio(color, shown) >= ratio)
            {
                continue;
            }

            var start = color;
            for (var step = 1; step <= 20; step++)
            {
                color = start.Mix(target, step / 20.0);
                if (ThemeColor.ContrastRatio(color, shown) >= ratio)
                {
                    break;
                }
            }
        }

        return color;
    }

    /// <summary>
    /// A text field's outline: the look's own outline when it already reaches
    /// 3:1 on the page, otherwise the text colour at the faintest strength
    /// that reaches 3.5:1 (room for a header's glow behind the field).
    /// </summary>
    private static ThemeColor FieldBorderFor(ThemeColor? own, ThemeColor text, ThemeColor page, ThemeColor lightestPage)
    {
        if (own is { } outline
            && ThemeColor.ContrastRatio(outline.Over(page), page) >= 3
            && ThemeColor.ContrastRatio(outline.Over(lightestPage), lightestPage) >= 3)
        {
            return outline;
        }

        for (var step = 30; step < 100; step += 5)
        {
            var candidate = text.WithAlpha(step / 100.0);
            if (ThemeColor.ContrastRatio(candidate.Over(page), page) >= 3.5
                && ThemeColor.ContrastRatio(candidate.Over(lightestPage), lightestPage) >= 3.5)
            {
                return candidate;
            }
        }

        return text;
    }

    /// <summary>The accent at the faintest whole step from 38 % up to 85 % that reads at 3:1 on the card (the strongest when none does).</summary>
    private static ThemeColor ChartQuietFor(ThemeColor accent, ThemeColor card)
    {
        for (var step = 38; step <= 85; step++)
        {
            var candidate = accent.WithAlpha(step / 100.0);
            if (ThemeColor.ContrastRatio(candidate.Over(card), card) >= 3)
            {
                return candidate;
            }
        }

        return accent.WithAlpha(0.85);
    }

    /// <summary>
    /// The accent as a soft wash for selected rows, on the page and in the
    /// sidebar. Each text over it keeps 7:1 (main), 4.5:1 (secondary) and
    /// 2.5:1 (captions), or, where the grey highlight it replaces already
    /// fell short of that, at least as much as over that grey; it is made
    /// fainter until it does (transparent if nothing faint enough helps).
    /// </summary>
    private static ThemeColor SoftAccent(ThemeColor accent, bool isLight, ThemeColor grey, ThemeColor[] texts, ThemeColor[] panels)
    {
        // Main text, secondary text, captions: the bars of Every_preset_is_readable.
        double[] bars = [7, 4.5, 2.5];

        bool ReadsOver(ThemeColor tint)
        {
            foreach (var panel in panels)
            {
                var shown = tint.Over(panel);
                var greyShown = grey.Over(panel);
                for (var i = 0; i < texts.Length; i++)
                {
                    var bar = Math.Min(bars[i], ThemeColor.ContrastRatio(texts[i], greyShown));
                    if (ThemeColor.ContrastRatio(texts[i], shown) < bar)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        for (var step = isLight ? 12 : 16; step > 0; step -= 2)
        {
            var tint = accent.WithAlpha(step / 100.0);
            if (ReadsOver(tint))
            {
                return tint;
            }
        }

        return accent.WithAlpha(0);
    }

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

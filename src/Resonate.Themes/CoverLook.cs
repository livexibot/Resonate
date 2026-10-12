namespace Resonate.Themes;

/// <summary>
/// A look that follows the cover ("Colours follow the cover"): every colour
/// the look sets takes the cover's hue while keeping its own lightness and
/// saturation, so a dark purple background becomes a dark blue one for a
/// blue cover, light text stays light, and greys stay grey. The accent and
/// second accent become the cover's two colours, the gradient runs between
/// their hues, and a coloured outline follows the accent. A cover with no
/// colour of its own (black, white or grey) turns the look grey instead
/// (<see cref="Monochrome"/>). Readability is still the palette's job
/// (<see cref="ThemePalette.From"/>).
/// </summary>
public static class CoverLook
{
    /// <summary>Below this saturation a colour counts as grey and keeps its own hue.</summary>
    public const double GreyBelow = 0.04;

    /// <summary><paramref name="look"/> in the cover's colours, <paramref name="accent"/> and <paramref name="second"/>.</summary>
    public static ThemeDefinition Follow(ThemeDefinition look, ThemeColor accent, ThemeColor second)
    {
        var (hue, saturation, _) = accent.ToHsl();
        if (saturation < GreyBelow)
        {
            return Monochrome(look);
        }

        var secondHue = second.ToHsl().Hue;
        return look with
        {
            Accent = accent,
            Accent2 = second,
            Background = Turn(look.Background, hue),
            Background2 = Turn(look.Background2, secondHue),
            Sidebar = Turn(look.Sidebar, hue),
            Surface = Turn(look.Surface, hue),
            Player = Turn(look.Player, hue),
            Text = Turn(look.Text, hue),
            Border = look.Border is { } border ? Turn(border, hue) : null,
        };
    }

    /// <summary>
    /// <paramref name="look"/> for a cover with no colour of its own (black,
    /// white or grey): every colour turns grey with its own lightness, and the
    /// accents are white on a dark look and black on a light one, whatever
    /// the cover's brightness, so they always read (the owner's request,
    /// 11 October 2026: the look kept its own colours for such covers).
    /// </summary>
    public static ThemeDefinition Monochrome(ThemeDefinition look)
    {
        var light = look.Surface.Over(look.Background.Opaque).IsLight;
        return look with
        {
            Accent = light ? MonochromeDark : ThemeColor.White,
            Accent2 = light ? ThemeColor.FromRgb(0x5A5A5A) : ThemeColor.FromRgb(0xB4B4B4),
            Background = Grey(look.Background),
            Background2 = Grey(look.Background2),
            Sidebar = Grey(look.Sidebar),
            Surface = Grey(look.Surface),
            Player = Grey(look.Player),
            Text = Grey(look.Text),
            Border = look.Border is { } border ? Grey(border) : null,
        };
    }

    /// <summary>The accent on a light look for a black-and-white cover: near black.</summary>
    public static readonly ThemeColor MonochromeDark = ThemeColor.FromRgb(0x141414);

    /// <summary><paramref name="colour"/> without its colour: the same lightness and alpha, no saturation.</summary>
    public static ThemeColor Grey(ThemeColor colour)
    {
        var (_, _, lightness) = colour.ToHsl();
        return ThemeColor.FromHsl(0, 0, lightness, colour.A);
    }

    /// <summary><paramref name="colour"/> with the hue <paramref name="hue"/>, its saturation, lightness and alpha kept; a grey is left alone.</summary>
    public static ThemeColor Turn(ThemeColor colour, double hue)
    {
        var (_, saturation, lightness) = colour.ToHsl();
        return saturation < GreyBelow ? colour : ThemeColor.FromHsl(hue, saturation, lightness, colour.A);
    }
}

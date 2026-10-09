namespace Resonate.Themes;

/// <summary>
/// A look that follows the cover ("Colours follow the cover"): every colour
/// the look sets takes the cover's hue while keeping its own lightness and
/// saturation, so a dark purple background becomes a dark blue one for a
/// blue cover, light text stays light, and greys stay grey. The accent and
/// second accent become the cover's two colours, the gradient runs between
/// their hues, and a coloured outline follows the accent. Readability is
/// still the palette's job (<see cref="ThemePalette.From"/>).
/// </summary>
public static class CoverLook
{
    /// <summary>Below this saturation a colour counts as grey and keeps its own hue.</summary>
    public const double GreyBelow = 0.04;

    /// <summary><paramref name="look"/> in the cover's colours, <paramref name="accent"/> and <paramref name="second"/>.</summary>
    public static ThemeDefinition Follow(ThemeDefinition look, ThemeColor accent, ThemeColor second)
    {
        var hue = accent.ToHsl().Hue;
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

    /// <summary><paramref name="colour"/> with the hue <paramref name="hue"/>, its saturation, lightness and alpha kept; a grey is left alone.</summary>
    public static ThemeColor Turn(ThemeColor colour, double hue)
    {
        var (_, saturation, lightness) = colour.ToHsl();
        return saturation < GreyBelow ? colour : ThemeColor.FromHsl(hue, saturation, lightness, colour.A);
    }
}

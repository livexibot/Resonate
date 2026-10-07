using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>
/// A colour with transparency, written as <c>#RRGGBB</c> or <c>#AARRGGBB</c>
/// (the same order XAML uses). Includes the colour maths the theme needs:
/// mixing, lightness and contrast.
/// </summary>
[JsonConverter(typeof(ThemeColorJsonConverter))]
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static readonly ThemeColor White = FromRgb(0xFFFFFF);
    public static readonly ThemeColor Black = FromRgb(0x000000);
    public static readonly ThemeColor Transparent = new(0, 0, 0, 0);

    /// <summary>An opaque colour from <c>0xRRGGBB</c>.</summary>
    public static ThemeColor FromRgb(uint rgb) => new(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>The same colour with a different opacity (0 to 1).</summary>
    public ThemeColor WithAlpha(double opacity) => this with { A = ToByte(opacity) };

    public ThemeColor Opaque => this with { A = 0xFF };

    public double Opacity => A / 255.0;

    /// <summary>
    /// Mixes towards <paramref name="other"/>: 0 keeps this colour, 1 gives the
    /// other. Opacity is mixed too.
    /// </summary>
    public ThemeColor Mix(ThemeColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new ThemeColor(
            Lerp(A, other.A, amount),
            Lerp(R, other.R, amount),
            Lerp(G, other.G, amount),
            Lerp(B, other.B, amount));
    }

    /// <summary>What this colour looks like painted over an opaque <paramref name="below"/>.</summary>
    public ThemeColor Over(ThemeColor below) => below.Opaque.Mix(Opaque, Opacity);

    /// <summary>Relative luminance (WCAG 2), from 0 (black) to 1 (white), ignoring opacity.</summary>
    public double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    /// <summary>Whether dark text reads better on this colour than light text.</summary>
    public bool IsLight => ContrastRatio(this, Black) > ContrastRatio(this, White);

    /// <summary>WCAG contrast ratio between two opaque colours, from 1 to 21.</summary>
    public static double ContrastRatio(ThemeColor a, ThemeColor b)
    {
        var (light, dark) = a.Luminance >= b.Luminance ? (a.Luminance, b.Luminance) : (b.Luminance, a.Luminance);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Hue (0 to 360), saturation and lightness (0 to 1).</summary>
    public (double Hue, double Saturation, double Lightness) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta < 1e-9)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
        {
            hue = ((g - b) / delta) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2;
        }
        else
        {
            hue = ((r - g) / delta) + 4;
        }

        return (hue * 60, saturation, lightness);
    }

    public static ThemeColor FromHsl(double hue, double saturation, double lightness, byte alpha = 0xFF)
    {
        hue = ((hue % 360) + 360) % 360 / 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        if (saturation < 1e-9)
        {
            var grey = ToByte(lightness);
            return new ThemeColor(alpha, grey, grey, grey);
        }

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - (lightness * saturation);
        var p = (2 * lightness) - q;
        return new ThemeColor(
            alpha,
            ToByte(HueToChannel(p, q, hue + (1 / 3.0))),
            ToByte(HueToChannel(p, q, hue)),
            ToByte(HueToChannel(p, q, hue - (1 / 3.0))));
    }

    /// <summary><c>#RRGGBB</c> when opaque, otherwise <c>#AARRGGBB</c>.</summary>
    public override string ToString() =>
        A == 0xFF
            ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");

    /// <summary>Reads <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> (the # is optional).</summary>
    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length is not (6 or 8)
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = hex.Length == 6
            ? FromRgb(value)
            : new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private static byte Lerp(byte from, byte to, double amount) => ToByte((from + ((to - from) * amount)) / 255.0);

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        return t switch
        {
            < 1 / 6.0 => p + ((q - p) * 6 * t),
            < 1 / 2.0 => q,
            < 2 / 3.0 => p + ((q - p) * ((2 / 3.0) - t) * 6),
            _ => p,
        };
    }
}

/// <summary>Writes colours as hex text, so a shared look stays readable.</summary>
public sealed class ThemeColorJsonConverter : JsonConverter<ThemeColor>
{
    public override ThemeColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && ThemeColor.TryParse(reader.GetString(), out var color)
            ? color
            : throw new JsonException("Expected a colour such as \"#8B7CFF\".");

    public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

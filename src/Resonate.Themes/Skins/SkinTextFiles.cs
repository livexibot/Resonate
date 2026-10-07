using System.Globalization;
using System.Text;

namespace Resonate.Themes.Skins;

/// <summary>
/// Reads a classic skin's two text files: viscolor.txt (the visualiser's 24
/// colours) and pledit.txt (the playlist colours). Skins were written by hand
/// in many editors, so both are read forgivingly: a line or a value that
/// cannot be read keeps its default.
/// </summary>
public static class SkinTextFiles
{
    /// <summary>Real files are a few hundred bytes; anything past this is ignored.</summary>
    public const int MaxTextBytes = 64 * 1024;

    /// <summary>The longest font name kept from pledit.txt.</summary>
    public const int MaxFontNameLength = 64;

    /// <summary>
    /// The visualiser colours a skin without viscolor.txt gets: black behind
    /// grey grid dots, bars from red at the top through yellow to green at the
    /// bottom, a white-to-grey oscilloscope and grey peak dots. These are the
    /// values classic skins were drawn to expect.
    /// </summary>
    public static IReadOnlyList<uint> DefaultVisColors { get; } =
    [
        Rgb(0, 0, 0), Rgb(24, 33, 41), Rgb(239, 49, 16), Rgb(206, 41, 16), Rgb(214, 90, 0),
        Rgb(214, 102, 0), Rgb(214, 115, 0), Rgb(198, 123, 8), Rgb(222, 165, 24), Rgb(214, 181, 33),
        Rgb(189, 222, 41), Rgb(148, 222, 33), Rgb(41, 206, 16), Rgb(50, 190, 16), Rgb(57, 181, 16),
        Rgb(49, 156, 8), Rgb(41, 148, 0), Rgb(24, 132, 8), Rgb(255, 255, 255), Rgb(214, 214, 222),
        Rgb(181, 189, 189), Rgb(160, 170, 175), Rgb(148, 156, 165), Rgb(150, 150, 150),
    ];

    /// <summary>The playlist colours a skin without pledit.txt gets: green text on black, white for the current song, blue for the selection.</summary>
    public static PlaylistColors DefaultPlaylistColors { get; } = new(Rgb(0, 255, 0), Rgb(255, 255, 255), Rgb(0, 0, 0), Rgb(0, 0, 255), null);

    /// <summary>Reads viscolor.txt, filling what it lacks from <see cref="DefaultVisColors"/>.</summary>
    public static uint[] ParseVisColors(ReadOnlySpan<byte> text) => ParseVisColors(text, DefaultVisColors);

    /// <summary>
    /// Reads viscolor.txt: each line that starts with three numbers (split by
    /// commas or spaces, anything after them ignored) is the next colour, so
    /// comment lines are skipped. Numbers over 255 count as 255. Colours the
    /// file lacks come from <paramref name="defaults"/>.
    /// </summary>
    public static uint[] ParseVisColors(ReadOnlySpan<byte> text, IReadOnlyList<uint> defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (defaults.Count != Skin.VisColorCount)
        {
            throw new ArgumentException($"A skin has {Skin.VisColorCount} visualiser colours.", nameof(defaults));
        }

        var colours = defaults.ToArray();
        var rest = Normalise(text).AsSpan();
        var index = 0;
        while (index < colours.Length && !rest.IsEmpty)
        {
            var end = rest.IndexOfAny('\r', '\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? [] : rest[(end + 1)..];
            if (TryParseRgb(line, out var colour))
            {
                colours[index++] = colour;
            }
        }

        return colours;
    }

    /// <summary>Reads pledit.txt, filling what it lacks from <see cref="DefaultPlaylistColors"/>.</summary>
    public static PlaylistColors ParsePlaylistColors(ReadOnlySpan<byte> text) => ParsePlaylistColors(text, DefaultPlaylistColors);

    /// <summary>
    /// Reads pledit.txt, an INI file whose <c>[Text]</c> section names the
    /// playlist colours (<c>Normal=#00FF00</c>; the # is optional and only the
    /// first six hex digits count) and font. Section and key names ignore
    /// case; a missing or unreadable value keeps its default.
    /// </summary>
    public static PlaylistColors ParsePlaylistColors(ReadOnlySpan<byte> text, PlaylistColors defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var normal = defaults.Normal;
        var current = defaults.Current;
        var normalBackground = defaults.NormalBackground;
        var selectedBackground = defaults.SelectedBackground;
        var font = defaults.Font;
        var inText = false;
        foreach (var rawLine in Normalise(text).AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                var section = close < 0 ? line[1..] : line[1..close];
                inText = section.Trim().Equals("Text", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inText || equals < 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..];

            // Anything after a second '=' is ignored, and quotes around the value are dropped.
            var second = value.IndexOf('=');
            value = (second < 0 ? value : value[..second]).Trim().Trim("\"'").Trim();
            if (key.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            {
                normal = ParseHex(value, normal);
            }
            else if (key.Equals("Current", StringComparison.OrdinalIgnoreCase))
            {
                current = ParseHex(value, current);
            }
            else if (key.Equals("NormalBG", StringComparison.OrdinalIgnoreCase))
            {
                normalBackground = ParseHex(value, normalBackground);
            }
            else if (key.Equals("SelectedBG", StringComparison.OrdinalIgnoreCase))
            {
                selectedBackground = ParseHex(value, selectedBackground);
            }
            else if (key.Equals("Font", StringComparison.OrdinalIgnoreCase) && !value.IsEmpty)
            {
                font = value[..Math.Min(value.Length, MaxFontNameLength)].Trim().ToString();
            }
        }

        return new PlaylistColors(normal, current, normalBackground, selectedBackground, font);
    }

    /// <summary>
    /// The file as text: UTF-8 (a byte-order mark dropped, bad bytes replaced),
    /// or UTF-16 when it starts with that byte-order mark, as Notepad's
    /// "Unicode" saves it.
    /// </summary>
    private static string Normalise(ReadOnlySpan<byte> text)
    {
        text = text[..Math.Min(text.Length, MaxTextBytes)];
        if (text.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(text[2..]);
        }

        if (text.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(text[2..]);
        }

        if (text.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            text = text[3..];
        }

        return Encoding.UTF8.GetString(text);
    }

    /// <summary>Three numbers at the start of the line, each followed by spaces, an optional comma and spaces.</summary>
    private static bool TryParseRgb(ReadOnlySpan<char> line, out uint colour)
    {
        colour = 0;
        Span<int> parts = stackalloc int[3];
        var at = SkipSpaces(line, 0);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                at = SkipSpaces(line, at);
                if (at < line.Length && line[at] == ',')
                {
                    at++;
                }

                at = SkipSpaces(line, at);
            }

            var start = at;
            var value = 0;
            while (at < line.Length && char.IsAsciiDigit(line[at]))
            {
                // Stop counting once past 255; the value is clamped anyway.
                value = Math.Min((value * 10) + (line[at] - '0'), 256);
                at++;
            }

            if (at == start)
            {
                return false;
            }

            parts[i] = Math.Min(value, 255);
        }

        colour = Rgb(parts[0], parts[1], parts[2]);
        return true;
    }

    private static int SkipSpaces(ReadOnlySpan<char> line, int at)
    {
        while (at < line.Length && line[at] is ' ' or '\t' or '\v' or '\f')
        {
            at++;
        }

        return at;
    }

    /// <summary>Six hex digits after any number of '#'; anything after them is ignored. Otherwise <paramref name="fallback"/>.</summary>
    private static uint ParseHex(ReadOnlySpan<char> value, uint fallback)
    {
        value = value.TrimStart('#');
        if (value.Length < 6)
        {
            return fallback;
        }

        var digits = value[..6];
        foreach (var c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return fallback;
            }
        }

        return 0xFF000000 | uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static uint Rgb(int red, int green, int blue) => 0xFF000000 | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
}

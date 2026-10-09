namespace Resonate.Themes;

/// <summary>What a font's letters look like, so the look editor can group them.</summary>
public enum FontKind
{
    /// <summary>Plain letters without serifs, the usual choice for an interface.</summary>
    Sans,

    /// <summary>Letters with small strokes at their ends, like a book or a newspaper.</summary>
    Serif,

    /// <summary>Every letter the same width, like code.</summary>
    Mono,
}

/// <summary>A font that comes with Resonate: its family name and its file in Assets/Fonts.</summary>
public sealed record BundledFont(string Name, string File, FontKind Kind);

/// <summary>
/// The fonts Resonate ships in Assets/Fonts, so a look can use them on any PC
/// whether or not they are installed. Each file holds the Regular, SemiBold
/// and Bold weights of one family, and its licence sits next to it
/// (<c>&lt;file&gt;-OFL.txt</c>); tools/fonts/build_fonts.py makes them. A look
/// stores plain family names ("Inter, Segoe UI"); <see cref="Resolve"/> turns
/// the bundled ones into the address XAML needs to load them from the app.
/// </summary>
public static class BundledFonts
{
    public const string UriPrefix = "ms-appx:///Assets/Fonts/";

    /// <summary>Every bundled font, in alphabetical order.</summary>
    public static IReadOnlyList<BundledFont> All { get; } =
    [
        new("Bodoni Moda", "BodoniModa.ttc", FontKind.Serif),
        new("DM Sans", "DMSans.ttc", FontKind.Sans),
        new("Figtree", "Figtree.ttc", FontKind.Sans),
        new("Geist", "Geist.ttc", FontKind.Sans),
        new("Geist Mono", "GeistMono.ttc", FontKind.Mono),
        new("Inter", "Inter.ttc", FontKind.Sans),
        new("JetBrains Mono", "JetBrainsMono.ttc", FontKind.Mono),
        new("Literata", "Literata.ttc", FontKind.Serif),
        new("Manrope", "Manrope.ttc", FontKind.Sans),
        new("Montserrat", "Montserrat.ttc", FontKind.Sans),
        new("Newsreader", "Newsreader.ttc", FontKind.Serif),
        new("Nunito", "Nunito.ttc", FontKind.Sans),
        new("Outfit", "Outfit.ttc", FontKind.Sans),
        new("Oxanium", "Oxanium.ttc", FontKind.Sans),
        new("Plus Jakarta Sans", "PlusJakartaSans.ttc", FontKind.Sans),
        new("Poppins", "Poppins.ttc", FontKind.Sans),
        new("Rubik", "Rubik.ttc", FontKind.Sans),
        new("Sora", "Sora.ttc", FontKind.Sans),
        new("Space Grotesk", "SpaceGrotesk.ttc", FontKind.Sans),
        new("Tektur", "Tektur.ttc", FontKind.Sans),
        new("Unbounded", "Unbounded.ttc", FontKind.Sans),
        new("Work Sans", "WorkSans.ttc", FontKind.Sans),
    ];

    /// <summary>The bundled font with this family name, ignoring case and spaces around it.</summary>
    public static BundledFont? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return All.FirstOrDefault(f => string.Equals(f.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Turns a look's font list ("Inter, Segoe UI") into one XAML can use:
    /// each bundled font becomes its file's address followed by # and its
    /// name ("ms-appx:///Assets/Fonts/Inter.ttc#Inter"). Installed fonts, and
    /// anything that already names a file (has a # or a /), stay as they are.
    /// </summary>
    public static string Resolve(string? fontList)
    {
        if (string.IsNullOrWhiteSpace(fontList))
        {
            return string.Empty;
        }

        var entries = fontList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].IndexOfAny(['#', '/']) < 0 && Find(entries[i]) is { } font)
            {
                entries[i] = UriPrefix + font.File + "#" + font.Name;
            }
        }

        return string.Join(", ", entries);
    }
}

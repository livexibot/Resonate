using System.Collections.Frozen;

namespace Resonate.Themes.Skins;

/// <summary>
/// The skin's own 5 x 6 pixel font (text.bmp: 31 columns by 3 rows of
/// glyphs). It has capitals only, so lower case shares them; accented Latin
/// letters lose their accents; anything else it cannot show draws as a space.
/// </summary>
public static class PixelFont
{
    public const int CellWidth = 5;
    public const int CellHeight = 6;

    private static readonly (int Row, int Column) Space = (0, 30);

    /// <summary>
    /// U+0100 to U+017F (Latin Extended-A: Czech, Polish, Turkish, Baltic and
    /// more) without their accents, one letter each, as webamp's deburr does.
    /// </summary>
    private const string LatinExtendedA =
        "aaaaaaccccccccddddeeeeeeeeeegggg" +
        "gggghhhhiiiiiiiiiiiijjkkklllllll" +
        "lllnnnnnnnnnoooooooorrrrrrssssss" +
        "ssttttttuuuuuuuuuuuuwwyyyzzzzzzs";

    // webamp's FONT_LOOKUP (skinSprites.ts), key for key: lower-case keys, values are (row, column).
    private static readonly FrozenDictionary<char, (int Row, int Column)> Lookup = new Dictionary<char, (int Row, int Column)>
    {
        ['a'] = (0, 0),
        ['b'] = (0, 1),
        ['c'] = (0, 2),
        ['d'] = (0, 3),
        ['e'] = (0, 4),
        ['f'] = (0, 5),
        ['g'] = (0, 6),
        ['h'] = (0, 7),
        ['i'] = (0, 8),
        ['j'] = (0, 9),
        ['k'] = (0, 10),
        ['l'] = (0, 11),
        ['m'] = (0, 12),
        ['n'] = (0, 13),
        ['o'] = (0, 14),
        ['p'] = (0, 15),
        ['q'] = (0, 16),
        ['r'] = (0, 17),
        ['s'] = (0, 18),
        ['t'] = (0, 19),
        ['u'] = (0, 20),
        ['v'] = (0, 21),
        ['w'] = (0, 22),
        ['x'] = (0, 23),
        ['y'] = (0, 24),
        ['z'] = (0, 25),
        ['"'] = (0, 26),
        ['@'] = (0, 27),
        [' '] = (0, 30),
        ['0'] = (1, 0),
        ['1'] = (1, 1),
        ['2'] = (1, 2),
        ['3'] = (1, 3),
        ['4'] = (1, 4),
        ['5'] = (1, 5),
        ['6'] = (1, 6),
        ['7'] = (1, 7),
        ['8'] = (1, 8),
        ['9'] = (1, 9),
        ['\u2026'] = (1, 10), // the ellipsis character
        ['.'] = (1, 11),
        [':'] = (1, 12),
        ['('] = (1, 13),
        [')'] = (1, 14),
        ['-'] = (1, 15),
        ['\''] = (1, 16),
        ['!'] = (1, 17),
        ['_'] = (1, 18),
        ['+'] = (1, 19),
        ['\\'] = (1, 20),
        ['/'] = (1, 21),
        ['['] = (1, 22),
        [']'] = (1, 23),
        ['^'] = (1, 24),
        ['&'] = (1, 25),
        ['%'] = (1, 26),
        [','] = (1, 27),
        ['='] = (1, 28),
        ['$'] = (1, 29),
        ['#'] = (1, 30),
        ['Å'] = (2, 0),
        ['Ö'] = (2, 1),
        ['Ä'] = (2, 2),
        ['?'] = (2, 3),
        ['*'] = (2, 4),
        ['<'] = (1, 22),
        ['>'] = (1, 23),
        ['{'] = (1, 22),
        ['}'] = (1, 23),
    }.ToFrozenDictionary();

    /// <summary>The (row, column) of the text.bmp cell that draws a character; one the font lacks draws as a space.</summary>
    public static (int Row, int Column) Cell(char character) =>
        Lookup.TryGetValue(Fold(character), out var cell) ? cell : Space;

    /// <summary>True when the font has a shape for the character (spaces included), false when it would come out blank.</summary>
    public static bool Covers(char character) => Lookup.ContainsKey(Fold(character));

    /// <summary>True when the font can write every character of <paramref name="text"/>.</summary>
    public static bool Covers(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (!Covers(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes <paramref name="text"/> with the skin's font, 5 pixels a
    /// character from (<paramref name="x"/>, <paramref name="y"/>), cut off
    /// <paramref name="width"/> pixels to the right. Glyph cells are opaque:
    /// each carries the skin's text background.
    /// </summary>
    public static void Draw(Skin skin, SkinImage target, ReadOnlySpan<char> text, int x, int y, int width)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(target);
        var sheet = skin.Sheet(SkinSheet.Text);
        var right = x + width;
        for (var i = 0; i < text.Length; i++)
        {
            var left = x + (i * CellWidth);
            if (left >= right)
            {
                break;
            }

            DrawGlyph(sheet, target, text[i], left, y, Math.Min(CellWidth, right - left));
        }
    }

    /// <summary>One glyph, <paramref name="width"/> (at most 5) columns of it, from an already looked-up text.bmp.</summary>
    internal static void DrawGlyph(SkinImage sheet, SkinImage target, char character, int x, int y, int width)
    {
        var (row, column) = Cell(character);
        target.Draw(sheet, column * CellWidth, row * CellHeight, width, CellHeight, x, y);
    }

    /// <summary>
    /// Brings a character to one the table has (spotifast's fold): full-width
    /// forms to ASCII, capitals to the lower-case keys, accents dropped, and
    /// typographic quotes and dashes to the plain ones.
    /// </summary>
    private static char Fold(char character)
    {
        // Full-width forms are ASCII again, 0xFEE0 higher; East Asian releases write titles with them.
        if (character is >= '\uFF01' and <= '\uFF5E')
        {
            character = (char)(character - 0xFEE0);
        }

        return character switch
        {
            >= 'A' and <= 'Z' => (char)(character + ('a' - 'A')),
            '\t' or '\u00A0' or '\u3000' => ' ',
            'Å' or 'å' => 'Å',
            'Ö' or 'ö' => 'Ö',
            'Ä' or 'ä' => 'Ä',
            (>= 'À' and <= 'Ã') or (>= 'à' and <= 'ã') or 'Æ' or 'æ' => 'a',
            'Ç' or 'ç' => 'c',
            (>= 'È' and <= 'Ë') or (>= 'è' and <= 'ë') => 'e',
            (>= 'Ì' and <= 'Ï') or (>= 'ì' and <= 'ï') => 'i',
            'Ð' or 'ð' => 'd',
            'Ñ' or 'ñ' => 'n',
            (>= 'Ò' and <= 'Õ') or (>= 'ò' and <= 'õ') or 'Ø' or 'ø' => 'o',
            (>= 'Ù' and <= 'Ü') or (>= 'ù' and <= 'ü') => 'u',
            'Ý' or 'ý' or 'ÿ' => 'y',
            'ß' => 's',
            >= '\u0100' and <= '\u017F' => LatinExtendedA[character - 0x100],
            '\u2018' or '\u2019' or '`' => '\'',
            '\u201C' or '\u201D' => '"',
            '\u2013' or '\u2014' or '\u2212' => '-',
            '|' => '/',
            '~' => '-',
            ';' => ',',
            _ => character,
        };
    }
}

namespace Resonate.Themes.Skins;

/// <summary>
/// Resonate's own classic skin, drawn in code: original artwork in the
/// layout every classic skin uses, so the classic player works before any
/// skin is added and fills in the pictures a skin leaves out.
/// </summary>
/// <remarks>
/// The look comes from the Midnight preset: a graphite-indigo body with soft
/// one-pixel bevels lit from the top left, deep blue-black display wells,
/// light violet lettering and a dot-matrix clock, violet and cyan accents,
/// and a logo of concentric rings. Nothing here is copied from another skin.
/// </remarks>
internal static partial class BuiltInSkin
{
    public const string Name = "Resonate Classic";

    // Midnight's accents.
    private const uint Violet = 0xFF8B7CFF;
    private const uint Cyan = 0xFF5CC8FF;

    // The body: graphite with a touch of indigo, a shade darker towards the bottom.
    private const uint Outline = 0xFF06070A;
    private const uint BodyTop = 0xFF212333;
    private const uint BodyBottom = 0xFF191A25;
    private const uint BodyLight = 0xFF34374C;
    private const uint BodyShadow = 0xFF101118;

    // Display wells.
    private const uint WellFill = 0xFF090A14;
    private const uint WellShade = 0xFF030407;
    private const uint WellRim = 0xFF2E3145;
    private const uint WellGrid = 0xFF1A1B31;

    // Lettering inside the wells.
    private const uint Lcd = 0xFFC9C1FF;
    private const uint LcdGlow = 0xFF4E4697;
    private const uint LcdDim = 0xFF1F1C40;
    private const uint LcdLabel = 0xFF615D96;

    // Raised keys.
    private const uint KeyLight = 0xFF464A63;
    private const uint KeyTop = 0xFF2E3044;
    private const uint KeyBottom = 0xFF232436;
    private const uint KeyShadow = 0xFF15161F;
    private const uint KeySunk = 0xFF14151D;
    private const uint KeySunkShadow = 0xFF0A0B10;
    private const uint KeySunkLight = 0xFF2B2D3F;
    private const uint Ink = 0xFFE7E5F3;
    private const uint InkShadow = 0xFF0C0D13;
    private const uint InkDim = 0xFF7E809D;
    private const uint InkLit = 0xFFAFA4FF;

    // The title and shade bars.
    private const uint BarTop = 0xFF2A2C3F;
    private const uint BarBottom = 0xFF1E1F2D;
    private const uint BarLight = 0xFF3A3D54;
    private const uint BarIdleLine = 0xFF2C2E3E;
    private const uint TitleInk = 0xFFE9E7F7;
    private const uint TitleInkIdle = 0xFF6C6E88;

    /// <summary>The visualiser's colours: the well, its grid, the bars from the top (magenta) to the bottom (deep cyan), the scope, the peaks.</summary>
    private static readonly uint[] VisPalette =
    [
        WellFill,
        WellGrid,
        0xFFFF6FD8, 0xFFF270E0, 0xFFE272E8, 0xFFD074F0,
        0xFFBD77F7, 0xFFA97AFC, 0xFF977CFF, 0xFF8780FF,
        0xFF7887FF, 0xFF6A90FB, 0xFF5D99F4, 0xFF51A1EA,
        0xFF46A6DE, 0xFF3D9FCF, 0xFF3592BD, 0xFF2E83A8,
        0xFFEAE5FF, 0xFFC4BAFF, 0xFFA196FF, 0xFF7F72E6, 0xFF6155BD,
        0xFFE2DCFF,
    ];

    /// <summary>The playlist colours: soft grey text, the current song in violet, Midnight's background.</summary>
    private static readonly PlaylistColors PlaylistPalette = new(
        Normal: 0xFFB9BCCB,
        Current: 0xFFA99EFF,
        NormalBackground: 0xFF0F1014,
        SelectedBackground: 0xFF2A2650,
        Font: "Segoe UI");

    /// <summary>Draws every picture of the skin; nothing is read from disk.</summary>
    public static Skin Create()
    {
        var titleBar = DrawTitleBar();
        var buttons = DrawButtons();
        var lamps = DrawPlayPause();
        var channels = DrawMonoStereo();
        var numbers = DrawNumbers(withMinus: false);
        var numbersEx = DrawNumbers(withMinus: true);
        var text = DrawText();
        var position = DrawPosBar();
        var volume = DrawVolume();
        var balance = DrawBalance();
        var toggles = DrawToggles();
        var main = DrawMain(titleBar, buttons, lamps, channels, position, volume, balance, toggles);
        var equalizer = DrawEqMain();
        var equalizerShade = DrawEqEx();
        var playlist = DrawPlEdit();

        var sheets = new Dictionary<SkinSheet, SkinImage>(SkinSheets.All.Count)
        {
            [SkinSheet.Main] = main,
            [SkinSheet.TitleBar] = titleBar,
            [SkinSheet.CButtons] = buttons,
            [SkinSheet.PlayPaus] = lamps,
            [SkinSheet.MonoSter] = channels,
            [SkinSheet.Numbers] = numbers,
            [SkinSheet.NumsEx] = numbersEx,
            [SkinSheet.Text] = text,
            [SkinSheet.PosBar] = position,
            [SkinSheet.Volume] = volume,
            [SkinSheet.Balance] = balance,
            [SkinSheet.ShufRep] = toggles,
            [SkinSheet.EqMain] = equalizer,
            [SkinSheet.EqEx] = equalizerShade,
            [SkinSheet.PlEdit] = playlist,
        };
        return new Skin(Name, sheets, VisPalette, PlaylistPalette, isBuiltIn: true);
    }

    /// <summary>A new sheet at the size classic skins use.</summary>
    private static SkinImage NewSheet(SkinSheet sheet, uint fill)
    {
        var (width, height) = SkinSheets.ExpectedSize(sheet);
        var image = new SkinImage(width, height);
        image.Fill(fill);
        return image;
    }

    /// <summary>The body's colour on a row of the main window, so sprites that sit on it blend in.</summary>
    private static uint BodyAt(int windowY) => Mix(BodyTop, BodyBottom, (windowY - 14) / 101.0);

    /// <summary>Fills a sprite's box with the body as it is where the sprite is drawn.</summary>
    private static void BodyBehind(SkinImage image, int x, int y, int width, int height, int windowY)
    {
        for (var row = 0; row < height; row++)
        {
            HLine(image, x, y + row, width, BodyAt(windowY + row));
        }
    }

    /// <summary>The play, pause and stop lamp and the work indicator beside it (playpaus.bmp).</summary>
    private static SkinImage DrawPlayPause()
    {
        var image = NewSheet(SkinSheet.PlayPaus, WellFill);

        // Playing: a bright violet triangle.
        Art(image, 3, 1, "#.../##../###./####/###./##../#...", Mix(Violet, Lcd, 0.4));

        // Paused: two calm cyan bars.
        Art(image, 9 + 2, 1, "##.##/##.##/##.##/##.##/##.##/##.##/##.##", Mix(Cyan, Lcd, 0.35));

        // Stopped: a dim square.
        Art(image, 18 + 2, 2, "#####/#####/#####/#####/#####", LcdLabel);

        // (27, 0) stays blank: some players draw its first columns beside a stopped lamp.
        // The work indicator: a quiet violet tick while in step, a bright cyan bar while busy.
        Art(image, 36, 3, "##/##/##", LcdGlow);
        Art(image, 39, 1, "##/##/##/##/##/##/##", Cyan);
        return image;
    }

    /// <summary>The MONO and STEREO lamps (monoster.bmp): lit, a bright label over a glowing line; unlit, dim.</summary>
    private static SkinImage DrawMonoStereo()
    {
        var image = NewSheet(SkinSheet.MonoSter, WellFill);
        Lamp(image, 0, 0, 29, "STEREO", lit: true);
        Lamp(image, 0, 12, 29, "STEREO", lit: false);
        Lamp(image, 29, 0, 27, "MONO", lit: true);
        Lamp(image, 29, 12, 27, "MONO", lit: false);
        return image;
    }

    private static void Lamp(SkinImage image, int x, int y, int width, string label, bool lit)
    {
        var labelWidth = LabelWidth(label);
        var left = x + ((width - labelWidth) / 2);
        Label(image, left, y + 2, label, lit ? Lcd : LcdDim);
        if (lit)
        {
            HLine(image, left - 1, y + 9, labelWidth + 2, LcdGlow);
            HLine(image, left, y + 9, labelWidth, Violet);
            HLine(image, left, y + 10, labelWidth, LcdGlow);
        }
        else
        {
            HLine(image, left, y + 9, labelWidth, LcdDim);
        }
    }

    /// <summary>
    /// The clock's digits (numbers.bmp, or nums_ex.bmp with the minus at the
    /// end): lit dots over the dim dots of the whole matrix.
    /// </summary>
    private static SkinImage DrawNumbers(bool withMinus)
    {
        var image = NewSheet(withMinus ? SkinSheet.NumsEx : SkinSheet.Numbers, WellFill);
        var cells = withMinus ? 12 : 11;
        for (var cell = 0; cell < cells; cell++)
        {
            DotMatrix(image, cell * 9, 0, Digit(cell));
        }

        return image;
    }

    /// <summary>A 9x13 dot-matrix cell: every dot dim, the pattern's dots lit, and a faint glow between lit neighbours.</summary>
    private static void DotMatrix(SkinImage image, int x, int y, string pattern)
    {
        for (var row = 0; row < 7; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                Plot(image, x + (column * 2), y + (row * 2), LcdDim);
            }
        }

        for (var row = 0; row < 7; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                if (!Lit(pattern, column, row))
                {
                    continue;
                }

                Plot(image, x + (column * 2), y + (row * 2), Lcd);
                if (Lit(pattern, column + 1, row))
                {
                    Plot(image, x + (column * 2) + 1, y + (row * 2), LcdGlow);
                }

                if (Lit(pattern, column, row + 1))
                {
                    Plot(image, x + (column * 2), y + (row * 2) + 1, LcdGlow);
                }
            }
        }
    }

    /// <summary>Whether a dot of a 5x7 pattern ("#...#/..." rows) is lit.</summary>
    private static bool Lit(string pattern, int column, int row)
    {
        if (column is < 0 or > 4 || row is < 0 or > 6)
        {
            return false;
        }

        var index = (row * 6) + column;
        return index < pattern.Length && pattern[index] == '#';
    }

    /// <summary>The 5x6 font (text.bmp): light violet on the wells' colour, every cell opaque.</summary>
    private static SkinImage DrawText()
    {
        var image = NewSheet(SkinSheet.Text, WellFill);
        for (var row = 0; row < FontRows.Length; row++)
        {
            var characters = FontRows[row];
            for (var column = 0; column < characters.Length; column++)
            {
                Art(image, column * GlyphAdvance, row * 6, Glyph(characters[column]), Lcd);
            }
        }

        return image;
    }
}

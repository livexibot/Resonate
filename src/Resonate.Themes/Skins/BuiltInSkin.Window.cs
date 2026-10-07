namespace Resonate.Themes.Skins;

/// <summary>The built-in skin's window: its background (main.bmp) and its bars (titlebar.bmp).</summary>
internal static partial class BuiltInSkin
{
    private const string TitleText = "RESONATE";

    /// <summary>The glow behind a lit clutter-bar letter.</summary>
    private const uint ClutterGlow = 0xFF2E2766;

    // Title button glyphs, 5x5.
    private const string OptionsGlyph = ".###./#...#/#.#.#/#...#/.###.";
    private const string MinimizeGlyph = "...../...../...../...../#####";
    private const string ShadeGlyph = "..#../.#.#./#...#";
    private const string UnshadeGlyph = "#...#/.#.#./..#..";
    private const string CloseGlyph = "#...#/.#.#./..#../.#.#./#...#";

    /// <summary>The first row of each clutter-bar letter (O, A, I, D, V) inside the bar.</summary>
    private static ReadOnlySpan<int> ClutterRows => [5, 12, 19, 26, 34];

    /// <summary>The bars' face on a row (rows 2 to 11 of a 14-row bar), the same active or not so the buttons fit both.</summary>
    private static uint BarFace(int row) => Mix(BarTop, BarBottom, (row - 2) / 9.0);

    /// <summary>
    /// titlebar.bmp: the title and shade bars (active, inactive, and the
    /// playful variants), the title buttons, the clutter bar with each letter
    /// lit, and the shade mode's little seek bar.
    /// </summary>
    private static SkinImage DrawTitleBar()
    {
        var image = NewSheet(SkinSheet.TitleBar, BodyShadow);

        TitleBarStrip(image, 27, 0, active: true, playful: false);
        TitleBarStrip(image, 27, 15, active: false, playful: false);
        TitleBarStrip(image, 27, 57, active: true, playful: true);
        TitleBarStrip(image, 27, 72, active: false, playful: true);

        // The shade bars share a row (the active one's last is the inactive one's first); both draw it as the outline.
        ShadeBarStrip(image, 27, 29, active: true);
        ShadeBarStrip(image, 27, 42, active: false);

        TitleButton(image, 0, 0, OptionsGlyph, pressed: false, active: true);
        TitleButton(image, 0, 9, OptionsGlyph, pressed: true, active: true);
        TitleButton(image, 9, 0, MinimizeGlyph, pressed: false, active: true);
        TitleButton(image, 9, 9, MinimizeGlyph, pressed: true, active: true);
        TitleButton(image, 18, 0, CloseGlyph, pressed: false, active: true);
        TitleButton(image, 18, 9, CloseGlyph, pressed: true, active: true);
        TitleButton(image, 0, 18, ShadeGlyph, pressed: false, active: true);
        TitleButton(image, 9, 18, ShadeGlyph, pressed: true, active: true);
        TitleButton(image, 0, 27, UnshadeGlyph, pressed: false, active: true);
        TitleButton(image, 9, 27, UnshadeGlyph, pressed: true, active: true);

        MiniSeek(image);
        ClutterBar(image);
        return image;
    }

    /// <summary>The frame and face every bar shares: outline, a lit top row, the face, a line above the bottom.</summary>
    private static void BarBase(SkinImage image, int x, int y, uint line)
    {
        const int Width = ClassicRenderer.Width;
        image.FillRect(x, y, Width, 14, Outline);
        HLine(image, x + 1, y + 1, Width - 2, BarLight);
        for (var row = 2; row < 12; row++)
        {
            HLine(image, x + 1, y + row, Width - 2, BarFace(row));
        }

        VLine(image, x + 1, y + 1, 11, BarLight);
        VLine(image, x + Width - 2, y + 2, 10, BarBottom);
        HLine(image, x + 1, y + 12, Width - 2, line);
    }

    /// <summary>The accent line under an active bar: violet into cyan, fading into the bar at both ends.</summary>
    private static void AccentLine(SkinImage image, int x, int y)
    {
        const int Width = ClassicRenderer.Width;
        for (var i = 2; i < Width - 2; i++)
        {
            var colour = Mix(Violet, Cyan, (i - 2) / (double)(Width - 5));
            var fade = Math.Min(Math.Min(i - 1, Width - 2 - i) / 48.0, 1);
            Plot(image, x + i, y, Mix(BarIdleLine, colour, fade));
        }
    }

    private static void TitleBarStrip(SkinImage image, int x, int y, bool active, bool playful)
    {
        BarBase(image, x, y, BarIdleLine);
        if (active)
        {
            AccentLine(image, x, y + 12);
        }

        var left = x + ((ClassicRenderer.Width - WriteWidth(TitleText)) / 2);
        if (playful)
        {
            // The hidden variant: each letter a step from violet to cyan, with a spark either side.
            for (var i = 0; i < TitleText.Length; i++)
            {
                var colour = active ? Mix(Violet, Cyan, i / (double)(TitleText.Length - 1)) : TitleInkIdle;
                Art(image, left + (i * GlyphAdvance), y + 5, Glyph(TitleText[i]), colour);
            }

            Art(image, left - 9, y + 5, ".#./#+#/.#.", active ? Violet : TitleInkIdle, active ? Lcd : TitleInkIdle);
            Art(image, left + WriteWidth(TitleText) + 6, y + 5, ".#./#+#/.#.", active ? Cyan : TitleInkIdle, active ? Lcd : TitleInkIdle);
        }
        else
        {
            Write(image, left, y + 5, TitleText, active ? TitleInk : TitleInkIdle);
        }

        BarButtons(image, x, y, active, shaded: false);
    }

    /// <summary>The bar of shade mode: name, mini visualiser, mini clock, mini transport and seek, buttons.</summary>
    private static void ShadeBarStrip(SkinImage image, int x, int y, bool active)
    {
        BarBase(image, x, y, BarIdleLine);
        if (active)
        {
            AccentLine(image, x, y + 12);
        }

        var ink = active ? TitleInk : TitleInkIdle;
        Write(image, x + 20, y + 5, TitleText, active ? Mix(TitleInk, BarTop, 0.3) : TitleInkIdle);

        // The mini visualiser's well (it draws inside at 79, 5, 38 x 5) and the mini clock's (cells at 128 to 156, row 4).
        Well(image, x + 78, y + 4, 40, 7);
        Well(image, x + 126, y + 3, 33, 8);
        Art(image, x + 145, y + 5, "#/./#", active ? Lcd : LcdLabel);

        // Mini transport, drawn on the bar: previous, play, pause, stop, next, eject.
        Art(image, x + 170, y + 4, "#...#/#..##/#.###/#..##/#...#", ink);
        Art(image, x + 180, y + 4, "#../##./###/##./#..", ink);
        Art(image, x + 188, y + 4, "#.#/#.#/#.#/#.#/#.#", ink);
        Art(image, x + 197, y + 4, "#####/#####/#####/#####/#####", ink);
        Art(image, x + 207, y + 4, "#...#/##..#/###.#/##..#/#...#", ink);
        Art(image, x + 217, y + 4, "..#../.###./#####/...../#####", ink);

        // The mini seek bar's track, as its sprite draws it.
        MiniSeekTrack(image, x + 226, y + 4);
        BarButtons(image, x, y, active, shaded: true);
    }

    /// <summary>The title buttons as the bar shows them when nobody presses them (players draw only the pressed sprites).</summary>
    private static void BarButtons(SkinImage image, int x, int y, bool active, bool shaded)
    {
        TitleButton(image, x + 6, y + 3, OptionsGlyph, pressed: false, active);
        TitleButton(image, x + 244, y + 3, MinimizeGlyph, pressed: false, active);
        TitleButton(image, x + 254, y + 3, shaded ? UnshadeGlyph : ShadeGlyph, pressed: false, active);
        TitleButton(image, x + 264, y + 3, CloseGlyph, pressed: false, active);
    }

    /// <summary>A 9x9 title button: a small raised key with a light glyph, or sunk with a violet one while pressed.</summary>
    private static void TitleButton(SkinImage image, int x, int y, string glyph, bool pressed, bool active)
    {
        // Rows 3 to 11 of the bar lie behind it.
        for (var row = 0; row < 9; row++)
        {
            HLine(image, x, y + row, 9, BarFace(row + 3));
        }

        image.FillRect(x + 1, y + 1, 7, 7, pressed ? KeySunk : Mix(KeyTop, KeyBottom, 0.5));
        if (pressed)
        {
            Edge(image, x, y, 9, 9, KeySunkShadow, KeySunkLight);
        }
        else
        {
            Edge(image, x, y, 9, 9, KeyLight, KeyShadow);
        }

        Round(image, x, y, 9, 9, BarFace(3), BarFace(11));
        var (glyphX, glyphY) = Centre(glyph, x, y, 9, 9);
        if (pressed)
        {
            Art(image, glyphX, glyphY + 1, glyph, InkLit);
        }
        else
        {
            Art(image, glyphX, glyphY, glyph, active ? Ink : InkDim);
        }
    }

    /// <summary>The shade mode's 17x7 seek track: a short groove on the bar.</summary>
    private static void MiniSeekTrack(SkinImage image, int x, int y)
    {
        for (var row = 0; row < 7; row++)
        {
            HLine(image, x, y + row, 17, BarFace(row + 4));
        }

        HLine(image, x + 1, y + 2, 15, KeySunkShadow);
        HLine(image, x + 1, y + 3, 15, WellFill);
        HLine(image, x + 1, y + 4, 15, KeySunkLight);
        Plot(image, x, y + 3, KeySunkShadow);
        Plot(image, x + 16, y + 3, KeySunkLight);
    }

    /// <summary>The mini seek track and its three thumbs: cyan near the start, violet near the end.</summary>
    private static void MiniSeek(SkinImage image)
    {
        MiniSeekTrack(image, 0, 36);
        ReadOnlySpan<uint> looks = [Cyan, Mix(Cyan, Violet, 0.5), Violet];
        for (var i = 0; i < looks.Length; i++)
        {
            var x = 17 + (i * 3);
            for (var row = 0; row < 7; row++)
            {
                HLine(image, x, 36 + row, 3, BarFace(row + 4));
            }

            Art(image, x, 37, ".#./###/###/###/.#.", looks[i]);
            Plot(image, x + 1, 38, Lcd);
        }
    }

    /// <summary>
    /// The clutter bar (O, A, I, D, V) in a narrow well, the same with every
    /// letter faded (switched off), and each letter lit in its own box.
    /// </summary>
    private static void ClutterBar(SkinImage image)
    {
        ClutterColumn(image, 304, 0, LcdLabel);
        ClutterColumn(image, 312, 0, LcdDim);

        // Lit letter i is cut at (304 + 8i, 44 + top) and drawn at (10, 22 + top).
        ReadOnlySpan<int> tops = [3, 11, 18, 25, 33];
        ReadOnlySpan<int> heights = [8, 7, 7, 8, 7];
        for (var i = 0; i < tops.Length; i++)
        {
            var x = 304 + (8 * i);
            var y = 44 + tops[i];
            image.Draw(image, 304, tops[i], 8, heights[i], x, y);
            var glowTop = 44 + ClutterRows[i] - 1;
            image.FillRect(x + 2, glowTop, 4, 7, ClutterGlow);
            VLine(image, x + 1, glowTop + 1, 5, ClutterGlow);
            VLine(image, x + 6, glowTop + 1, 5, ClutterGlow);
            ClutterLetter(image, x, 44, i, Lcd);
        }
    }

    private static void ClutterColumn(SkinImage image, int x, int y, uint ink)
    {
        // The well covers rows 0 to 40 so its foot lines up with the display beside it; the last two rows are body.
        BodyBehind(image, x, y, 8, 43, 22);
        Well(image, x, y, 8, 41);
        for (var i = 0; i < 5; i++)
        {
            ClutterLetter(image, x, y, i, ink);
        }
    }

    private static void ClutterLetter(SkinImage image, int x, int y, int index, uint ink)
    {
        var glyph = Glyph("OAIDV"[index]);
        Art(image, x + 2, y + ClutterRows[index], glyph, ink);
    }

    /// <summary>
    /// main.bmp: the body with its bevelled frame, the two display wells (clock
    /// and visualiser on the left; song, rates and channel lamps on the right),
    /// the labels, the logo, and the resting look of every control drawn over it.
    /// </summary>
    private static SkinImage DrawMain(SkinImage titleBar, SkinImage buttons, SkinImage position, SkinImage volume, SkinImage balance, SkinImage toggles)
    {
        const int Width = ClassicRenderer.Width;
        const int Height = ClassicRenderer.Height;
        var image = new SkinImage(Width, Height);
        BodyBehind(image, 0, 0, Width, Height, 0);

        // The frame: outline, then a lit edge at the top left and a shaded one at the bottom right.
        VLine(image, 0, 14, Height - 14, Outline);
        VLine(image, Width - 1, 14, Height - 14, Outline);
        HLine(image, 0, Height - 1, Width, Outline);
        HLine(image, 1, 14, Width - 2, BodyLight);
        VLine(image, 1, 14, Height - 15, BodyLight);
        HLine(image, 2, Height - 2, Width - 3, BodyShadow);
        VLine(image, Width - 2, 15, Height - 16, BodyShadow);

        image.Draw(titleBar, 27, 0, Width, 14, 0, 0);
        image.Draw(titleBar, 304, 0, 8, 43, 10, 22);

        // Left well: lamp, clock and visualiser.
        Well(image, 20, 22, 84, 41);
        foreach (var cellX in (ReadOnlySpan<int>)[36, 48, 60, 78, 90])
        {
            DotMatrix(image, cellX, 26, Digit(10));
        }

        Plot(image, 73, 30, Lcd);
        Plot(image, 73, 34, Lcd);
        VisualiserRest(image, 24, 43);

        // Right well: the song line, a dotted rule, the rates and the channel lamps.
        Well(image, 108, 22, 161, 33);
        for (var x = 110; x < 267; x += 2)
        {
            Plot(image, x, 38, WellGrid);
        }

        Write(image, 129, 43, "KBPS", LcdLabel);
        Write(image, 169, 43, "KHZ", LcdLabel);

        Logo(image, 258, 97);

        // The controls at rest, as the player draws them over this picture anyway.
        image.Draw(volume, 0, 0, 68, 13, 107, 57);
        image.Draw(volume, 15, 422, 14, 11, 107, 58);
        image.Draw(balance, 9, 0, 38, 13, 177, 57);
        image.Draw(balance, 15, 422, 14, 11, 189, 58);
        image.Draw(toggles, 0, 61, 23, 12, 219, 58);
        image.Draw(toggles, 23, 61, 23, 12, 242, 58);
        image.Draw(position, 0, 0, 248, 10, 16, 72);
        image.Draw(buttons, 0, 0, 114, 18, 16, 88);
        image.Draw(buttons, 114, 0, 22, 16, 136, 89);
        image.Draw(toggles, 28, 0, 47, 15, 164, 89);
        image.Draw(toggles, 0, 0, 28, 15, 210, 89);
        return image;
    }

    /// <summary>The visualiser's empty look: the well with a dot on every other pixel of every other row, as players draw it.</summary>
    private static void VisualiserRest(SkinImage image, int x, int y)
    {
        image.FillRect(x, y, 76, 16, WellFill);
        for (var row = 1; row < 16; row += 2)
        {
            for (var column = 0; column < 75; column += 2)
            {
                Plot(image, x + column, y + row, WellGrid);
            }
        }
    }

    /// <summary>Resonate's mark: a bright core inside two rings, violet within and cyan without, like a note ringing out.</summary>
    private static void Logo(SkinImage image, int centreX, int centreY)
    {
        Ring(image, centreX, centreY, 6, Mix(Cyan, BodyAt(centreY), 0.3));
        Ring(image, centreX, centreY, 3, Violet);
        Art(image, centreX - 1, centreY - 1, ".#./###/.#.", Lcd);
    }
}

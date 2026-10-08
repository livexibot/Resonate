namespace Resonate.Themes.Skins;

/// <summary>The built-in skin's equalizer (eqmain.bmp, eq_ex.bmp) and playlist (pledit.bmp), in the main window's style.</summary>
internal static partial class BuiltInSkin
{
    private const string EqualizerTitle = "EQUALIZER";
    private const string PlaylistTitle = "PLAYLIST";

    /// <summary>The thumb's middle on a slider track for frame 0 (bottom) to 27 (top), in rows from the track's top.</summary>
    private static int SliderLevelRow(int frame) =>
        ClassicLayout.RoundHalfUp((1 - (frame / 27.0)) * EqualizerLayout.SliderTravel) + 5;

    /// <summary>
    /// eqmain.bmp: the window with its wells, labels and resting controls; the
    /// close button, the ON and AUTO switches in their four looks, the bars,
    /// the slider thumbs and 28 track frames, PRESETS, and the graph's parts.
    /// </summary>
    private static SkinImage DrawEqMain()
    {
        var image = NewSheet(SkinSheet.EqMain, BodyShadow);

        // The bars (rows 134 and 149), then the window, which shows the active one.
        EqualizerBar(image, 0, 134, active: true);
        EqualizerBar(image, 0, 149, active: false);
        TitleButton(image, 0, 116, CloseGlyph, pressed: false, active: true);
        TitleButton(image, 0, 125, CloseGlyph, pressed: true, active: true);

        EqKey(image, 10, 119, 26, "ON", on: false, pressed: false);
        EqKey(image, 128, 119, 26, "ON", on: false, pressed: true);
        EqKey(image, 69, 119, 26, "ON", on: true, pressed: false);
        EqKey(image, 187, 119, 26, "ON", on: true, pressed: true);
        EqKey(image, 36, 119, 32, "AUTO", on: false, pressed: false);
        EqKey(image, 154, 119, 32, "AUTO", on: false, pressed: true);
        EqKey(image, 95, 119, 32, "AUTO", on: true, pressed: false);
        EqKey(image, 213, 119, 32, "AUTO", on: true, pressed: true);

        EqThumb(image, 0, 164, pressed: false);
        EqThumb(image, 0, 176, pressed: true);
        for (var frame = 0; frame < StackSprites.EqSliderFrames; frame++)
        {
            var sprite = StackSprites.EqSliderFrame(frame);
            SliderTrack(image, sprite.X, sprite.Y, frame);
        }

        PresetsKey(image, 224, 164, pressed: false);
        PresetsKey(image, 224, 176, pressed: true);
        EqGraph(image, 0, 294);
        EqGraphColours(image, 115, 294);
        for (var x = 0; x < 113; x++)
        {
            Plot(image, x, 314, x % 3 == 2 ? WellFill : LcdLabel);
        }

        EqualizerWindow(image);
        return image;
    }

    /// <summary>The equalizer's 275 x 116 window at the top of eqmain.bmp, drawn from the parts below it.</summary>
    private static void EqualizerWindow(SkinImage image)
    {
        const int Width = EqualizerLayout.Width;
        const int Height = EqualizerLayout.Height;
        BodyBehind(image, 0, 0, Width, Height, 0);
        VLine(image, 0, 14, Height - 14, Outline);
        VLine(image, Width - 1, 14, Height - 14, Outline);
        HLine(image, 0, Height - 1, Width, Outline);
        HLine(image, 1, 14, Width - 2, BodyLight);
        VLine(image, 1, 14, Height - 15, BodyLight);
        HLine(image, 2, Height - 2, Width - 3, BodyShadow);
        VLine(image, Width - 2, 15, Height - 16, BodyShadow);

        image.Draw(image, 0, 134, Width, 14, 0, 0);
        image.Draw(image, 10, 119, 26, 12, 14, 18);
        image.Draw(image, 36, 119, 32, 12, 40, 18);
        image.Draw(image, 224, 164, 44, 12, 217, 18);
        Well(image, 85, 16, 115, 21);
        image.Draw(image, 0, 294, 113, 19, 86, 17);

        // The scale beside the sliders, and their names under them.
        var neutral = StackSprites.EqSliderFrame(14);
        image.Draw(image, neutral.X, neutral.Y, 14, 63, 21, 38);
        for (var band = 0; band < EqualizerLayout.BandCount; band++)
        {
            image.Draw(image, neutral.X, neutral.Y, 14, 63, 78 + (18 * band), 38);
            var label = EqualizerLayout.BandLabels[band];
            Write(image, 78 + (18 * band) + 7 - ((WriteWidth(label) + 1) / 2), 104, label, LcdLabel);
        }

        foreach (var (text, row) in new[] { ("+12 DB", 41), ("+0 DB", 67), ("-12 DB", 93) })
        {
            Write(image, 74 - WriteWidth(text), row, text, LcdLabel);
        }

        Write(image, 28 - ((WriteWidth("PREAMP") + 1) / 2), 104, "PREAMP", LcdLabel);

        // The thumbs at rest, as the player draws them anyway.
        image.Draw(image, 0, 164, 11, 11, 22, EqualizerLayout.ThumbTop(0));
        for (var band = 0; band < EqualizerLayout.BandCount; band++)
        {
            image.Draw(image, 0, 164, 11, 11, 79 + (18 * band), EqualizerLayout.ThumbTop(0));
        }
    }

    /// <summary>The equalizer's title bar: its name in the middle, the shade and close buttons on the right.</summary>
    private static void EqualizerBar(SkinImage image, int x, int y, bool active)
    {
        BarBase(image, x, y, BarIdleLine);
        if (active)
        {
            AccentLine(image, x, y + 12);
        }

        Write(image, x + ((EqualizerLayout.Width - WriteWidth(EqualizerTitle)) / 2), y + 5, EqualizerTitle, active ? TitleInk : TitleInkIdle);
        TitleButton(image, x + 254, y + 3, ShadeGlyph, pressed: false, active);
        TitleButton(image, x + 264, y + 3, CloseGlyph, pressed: false, active);
    }

    /// <summary>An ON or AUTO key: a lamp and a label, lit violet while on, sunk while pressed.</summary>
    private static void EqKey(SkinImage image, int x, int y, int width, string label, bool on, bool pressed)
    {
        KeyFace(image, x, y, width, 12, 18, pressed);
        var shift = pressed ? 1 : 0;
        var ink = on ? InkLit : pressed ? InkDim : Ink;
        var textX = x + 8 + ((width - 9 - WriteWidth(label)) / 2);
        if (!pressed)
        {
            Write(image, textX, y + 4, label, InkShadow);
        }

        Write(image, textX, y + 3 + shift, label, ink);
        ToggleLamp(image, x + 4, y + 4 + shift, 3, on);
    }

    private static void PresetsKey(SkinImage image, int x, int y, bool pressed)
    {
        KeyFace(image, x, y, 44, 12, 18, pressed);
        const string Label = "PRESETS";
        var textX = x + ((44 - WriteWidth(Label)) / 2);
        if (pressed)
        {
            Write(image, textX, y + 4, Label, InkLit);
        }
        else
        {
            Write(image, textX, y + 4, Label, InkShadow);
            Write(image, textX, y + 3, Label, Ink);
        }
    }

    /// <summary>An 11 x 11 slider thumb: a small raised key with a violet line across (cyan and sunk while held).</summary>
    private static void EqThumb(SkinImage image, int x, int y, bool pressed)
    {
        KeyFace(image, x, y, 11, 11, 60, pressed);
        var row = y + (pressed ? 5 : 4);
        HLine(image, x + 2, row, 7, pressed ? Cyan : Violet);
        HLine(image, x + 2, row + 1, 7, pressed ? Mix(Cyan, Outline, 0.4) : Mix(Violet, Outline, 0.45));
    }

    /// <summary>
    /// A 14 x 63 slider track for frame 0 (-12 dB) to 27 (+12 dB): a groove
    /// lit from the 0 dB mark to the thumb, cyan below it and violet above,
    /// brighter the further it reaches.
    /// </summary>
    private static void SliderTrack(SkinImage image, int x, int y, int frame)
    {
        BodyBehind(image, x, y, 14, 63, 38);
        image.FillRect(x + 5, y + 1, 4, 61, WellFill);
        VLine(image, x + 5, y + 1, 61, WellShade);
        VLine(image, x + 8, y + 1, 61, WellRim);
        HLine(image, x + 5, y + 1, 4, WellShade);
        HLine(image, x + 5, y + 61, 4, WellRim);

        const int Centre = 31;
        HLine(image, x + 1, y + Centre, 3, LcdLabel);
        HLine(image, x + 10, y + Centre, 3, LcdLabel);
        var level = SliderLevelRow(frame);
        var (top, bottom) = level < Centre ? (level, Centre) : (Centre, level);
        var reach = Math.Abs(frame - 13.5) / 13.5;
        var colour = Mix(LcdDim, level < Centre ? Violet : Cyan, 0.35 + (0.65 * reach));
        image.FillRect(x + 6, y + top, 2, bottom - top + 1, colour);
        Plot(image, x + 6, y + level, Lcd);
    }

    /// <summary>The graph's 113 x 19 well: a dotted 0 dB line and a dot for each band.</summary>
    private static void EqGraph(SkinImage image, int x, int y)
    {
        image.FillRect(x, y, 113, 19, WellFill);
        for (var column = 0; column < 113; column += 2)
        {
            Plot(image, x + column, y + 9, WellGrid);
        }

        for (var band = 0; band < EqualizerLayout.BandCount; band++)
        {
            var column = x + EqualizerLayout.GraphInset + (band * EqualizerLayout.GraphStep);
            Plot(image, column, y, WellGrid);
            Plot(image, column, y + 18, WellGrid);
        }
    }

    /// <summary>The graph's colour for each of its 19 rows: magenta at the top (boosts), violet through the middle, cyan at the bottom (cuts).</summary>
    private static void EqGraphColours(SkinImage image, int x, int y)
    {
        for (var row = 0; row < 19; row++)
        {
            var colour = row < 9
                ? Mix(0xFFFF6FD8, Violet, row / 9.0)
                : Mix(Violet, Cyan, (row - 9) / 9.0);
            Plot(image, x, y + row, Mix(colour, Lcd, 0.15));
        }
    }

    /// <summary>
    /// eq_ex.bmp: the rolled-up bars (name, little volume and balance
    /// grooves, buttons), the little thumbs in three looks each, and the
    /// bar's pressed buttons.
    /// </summary>
    private static SkinImage DrawEqEx()
    {
        var image = NewSheet(SkinSheet.EqEx, BodyShadow);
        EqualizerShadeBar(image, 0, 0, active: true);
        EqualizerShadeBar(image, 0, 15, active: false);

        ReadOnlySpan<uint> volume = [Cyan, Mix(Cyan, Violet, 0.5), Violet];
        ReadOnlySpan<uint> balance = [Cyan, Lcd, Violet];
        for (var i = 0; i < 3; i++)
        {
            MiniThumb(image, 1 + (i * 3), 30, volume[i]);
            MiniThumb(image, 11 + (i * 3), 30, balance[i]);
        }

        TitleButton(image, 1, 38, ShadeGlyph, pressed: true, active: true);
        TitleButton(image, 1, 47, UnshadeGlyph, pressed: true, active: true);
        TitleButton(image, 11, 38, CloseGlyph, pressed: false, active: true);
        TitleButton(image, 11, 47, CloseGlyph, pressed: true, active: true);
        return image;
    }

    private static void EqualizerShadeBar(SkinImage image, int x, int y, bool active)
    {
        BarBase(image, x, y, BarIdleLine);
        if (active)
        {
            AccentLine(image, x, y + 12);
        }

        Write(image, x + 8, y + 5, EqualizerTitle, active ? Mix(TitleInk, BarTop, 0.3) : TitleInkIdle);
        Groove(image, x + 61, y + 4, 97);
        Groove(image, x + 164, y + 4, 43);
        Plot(image, x + 164 + 21, y + 4, active ? Violet : LcdLabel);
        TitleButton(image, x + 254, y + 3, UnshadeGlyph, pressed: false, active);
        TitleButton(image, x + 264, y + 3, CloseGlyph, pressed: false, active);
    }

    /// <summary>A 7-pixel-tall groove on a bar, for a little slider.</summary>
    private static void Groove(SkinImage image, int x, int y, int width)
    {
        for (var row = 0; row < 7; row++)
        {
            HLine(image, x, y + row, width, BarFace(row + 4));
        }

        HLine(image, x + 1, y + 2, width - 2, KeySunkShadow);
        HLine(image, x + 1, y + 3, width - 2, WellFill);
        HLine(image, x + 1, y + 4, width - 2, KeySunkLight);
        Plot(image, x, y + 3, KeySunkShadow);
        Plot(image, x + width - 1, y + 3, KeySunkLight);
    }

    private static void MiniThumb(SkinImage image, int x, int y, uint colour)
    {
        for (var row = 0; row < 7; row++)
        {
            HLine(image, x, y + row, 3, BarFace(row + 4));
        }

        Art(image, x, y + 1, ".#./###/###/###/.#.", colour);
        Plot(image, x + 1, y + 2, Lcd);
    }

    /// <summary>
    /// pledit.bmp: the frame's corners, title and tiles (lit and not), the
    /// sides, the bottom corners with the menus, times and little transport,
    /// the scroll handle, the pressed title buttons and the rolled-up bar.
    /// </summary>
    private static SkinImage DrawPlEdit()
    {
        var image = NewSheet(SkinSheet.PlEdit, BodyShadow);
        foreach (var (y, active) in new[] { (0, true), (21, false) })
        {
            PlaylistBar(image, 0, y, 25, active);
            PlaylistBar(image, 26, y, 100, active);
            Write(image, 26 + ((100 - WriteWidth(PlaylistTitle)) / 2), y + 7, PlaylistTitle, active ? TitleInk : TitleInkIdle);
            PlaylistBar(image, 127, y, 25, active);
            PlaylistBar(image, 153, y, 25, active);
            TitleButton(image, 153 + 4, y + 3, ShadeGlyph, pressed: false, active);
            TitleButton(image, 153 + 14, y + 3, CloseGlyph, pressed: false, active);
        }

        PlaylistSide(image, 0, 42, 12, groove: false);
        PlaylistSide(image, 31, 42, 20, groove: true);
        TitleButton(image, 52, 42, CloseGlyph, pressed: true, active: true);
        TitleButton(image, 62, 42, ShadeGlyph, pressed: true, active: true);
        TitleButton(image, 150, 42, UnshadeGlyph, pressed: true, active: true);
        ScrollHandle(image, 52, 53, pressed: false);
        ScrollHandle(image, 61, 53, pressed: true);

        PlaylistShadeBar(image, 72, 42, 25, left: true);
        PlaylistShadeBar(image, 72, 57, 25, left: false);
        PlaylistShadeBar(image, 99, 42, 50, left: false);
        PlaylistShadeBar(image, 99, 57, 50, left: false);
        foreach (var (y, active) in new[] { (42, true), (57, false) })
        {
            TitleButton(image, 99 + 29, y + 3, UnshadeGlyph, pressed: false, active);
            TitleButton(image, 99 + 39, y + 3, CloseGlyph, pressed: false, active);
        }

        PlaylistBottom(image, 179, 0, 25);
        PlaylistBottom(image, 205, 0, 75);
        PlaylistBottomLeft(image, 0, 72);
        PlaylistBottomRight(image, 126, 72);
        return image;
    }

    /// <summary>A piece of the playlist's 20-pixel title bar: the main bars' face, an accent line while lit, a dark rim at the foot.</summary>
    private static void PlaylistBar(SkinImage image, int x, int y, int width, bool active)
    {
        HLine(image, x, y, width, Outline);
        HLine(image, x, y + 1, width, BarLight);
        for (var row = 2; row < 18; row++)
        {
            HLine(image, x, y + row, width, BarFace(Math.Min(row, 11)));
        }

        HLine(image, x, y + 18, width, active ? Mix(Violet, BarIdleLine, 0.25) : BarIdleLine);
        HLine(image, x, y + 19, width, Outline);
    }

    /// <summary>A side tile, 29 rows that repeat: body with an outline at the edge, and, on the right, the scroll groove.</summary>
    private static void PlaylistSide(SkinImage image, int x, int y, int width, bool groove)
    {
        for (var row = 0; row < 29; row++)
        {
            HLine(image, x, y + row, width, BodyAt(60));
        }

        if (groove)
        {
            VLine(image, x + width - 1, y, 29, Outline);
            VLine(image, x + width - 2, y, 29, BodyShadow);
            VLine(image, x, y, 29, Outline);
            image.FillRect(x + 5, y, 8, 29, WellFill);
            VLine(image, x + 4, y, 29, WellShade);
            VLine(image, x + 13, y, 29, WellRim);
        }
        else
        {
            VLine(image, x, y, 29, Outline);
            VLine(image, x + 1, y, 29, BodyLight);
            VLine(image, x + width - 1, y, 29, Outline);
        }
    }

    /// <summary>The 8 x 18 scroll handle: a small raised violet pill (brighter while held).</summary>
    private static void ScrollHandle(SkinImage image, int x, int y, bool pressed)
    {
        var (top, bottom) = pressed ? (0xFFCBC4FFu, 0xFF8F81FFu) : (Mix(0xFFB0A6FF, Violet, 0.15), 0xFF6A5CDBu);
        image.FillRect(x, y, 8, 18, WellFill);
        for (var row = 1; row < 17; row++)
        {
            HLine(image, x + 1, y + row, 6, Mix(top, bottom, (row - 1) / 15.0));
        }

        Round(image, x + 1, y + 1, 6, 16, WellFill, WellFill);
        for (var i = 0; i < 3; i++)
        {
            HLine(image, x + 2, y + 7 + (i * 2), 4, Mix(bottom, Outline, 0.3));
        }
    }

    /// <summary>A piece of the rolled-up playlist bar: the bar's face with a well for the song's name (the left piece starts it); only the buttons show focus.</summary>
    private static void PlaylistShadeBar(SkinImage image, int x, int y, int width, bool left)
    {
        HLine(image, x, y, width, Outline);
        HLine(image, x, y + 1, width, BarLight);
        for (var row = 2; row < 12; row++)
        {
            HLine(image, x, y + row, width, BarFace(row));
        }

        HLine(image, x, y + 12, width, BarIdleLine);
        HLine(image, x, y + 13, width, Outline);

        // The song's well runs from the left piece through the tiles into the right piece, up to the time's end.
        var start = left ? x + 3 : x;
        var end = width == 50 ? x + 22 : x + width;
        image.FillRect(start, y + 3, end - start, 8, WellFill);
        HLine(image, start, y + 3, end - start, WellShade);
        HLine(image, start, y + 10, end - start, WellRim);
        if (left)
        {
            VLine(image, start, y + 3, 8, WellShade);
            VLine(image, x, y, 14, Outline);
        }

        if (width == 50)
        {
            VLine(image, end - 1, y + 3, 8, WellRim);
            VLine(image, x + width - 1, y, 14, Outline);
        }
    }

    /// <summary>A plain piece of the bottom bar (the tile between the corners of a wider window).</summary>
    private static void PlaylistBottom(SkinImage image, int x, int y, int width)
    {
        for (var row = 0; row < 38; row++)
        {
            HLine(image, x, y + row, width, BodyAt(78 + row));
        }

        HLine(image, x, y, width, WellRim);
        HLine(image, x, y + 37, width, Outline);
        HLine(image, x, y + 36, width, BodyShadow);
    }

    /// <summary>The bottom left corner: ADD, REM, SEL and MISC.</summary>
    private static void PlaylistBottomLeft(SkinImage image, int x, int y)
    {
        PlaylistBottom(image, x, y, 125);
        VLine(image, x, y, 38, Outline);
        VLine(image, x + 1, y + 1, 35, BodyLight);
        foreach (var (left, label) in new[] { (14, "ADD"), (43, "REM"), (72, "SEL"), (101, "MISC") })
        {
            MenuKey(image, x + left, y + 8, label, null);
        }
    }

    /// <summary>The bottom right corner: the times' wells, the little transport, LIST OPTS and the grip.</summary>
    private static void PlaylistBottomRight(SkinImage image, int x, int y)
    {
        PlaylistBottom(image, x, y, 150);
        VLine(image, x + 149, y, 38, Outline);
        VLine(image, x + 148, y + 1, 35, BodyShadow);

        // The selection and list times (text at 7, 10), and the song's time (text at 66, 23).
        Well(image, x + 5, y + 8, 64, 10);
        image.FillRect(x + 7, y + 10, 60, 6, WellFill);
        Well(image, x + 64, y + 21, 29, 10);

        // The little transport: previous, play, pause, stop, next and eject, ten pixels each from (3, 22).
        ReadOnlySpan<string> glyphs =
        [
            "#..#/#.##/####/#.##/#..#",
            "#../##./###/##./#..",
            "#.#/#.#/#.#/#.#/#.#",
            "###/###/###",
            "#..#/##.#/####/##.#/#..#",
            "..#../.###./#####/...../#####",
        ];
        for (var i = 0; i < glyphs.Length; i++)
        {
            var (glyphX, glyphY) = Centre(glyphs[i], x + 3 + (i * 10), y + 22, 10, 10);
            Embossed(image, glyphX, glyphY, glyphs[i], Ink, InkShadow);
        }

        MenuKey(image, x + 106, y + 8, "LIST", "OPTS");

        // The grip: three ridges across the corner, lit above and shaded below.
        for (var i = 0; i < 3; i++)
        {
            var length = 6 + (i * 5);
            for (var k = 0; k < length; k++)
            {
                var px = x + 147 - k;
                var py = y + 35 - (length - 1) + k;
                Plot(image, px, py, BodyLight);
                Plot(image, px + 1, py, BodyShadow);
            }
        }
    }

    /// <summary>A 22 x 18 key of the playlist's bottom bar with one or two lines of label.</summary>
    private static void MenuKey(SkinImage image, int x, int y, string label, string? second)
    {
        KeyFace(image, x, y, 22, 18, 86, pressed: false);
        if (second is null)
        {
            Write(image, x + 1 + ((20 - WriteWidth(label)) / 2), y + 7, label, InkShadow);
            Write(image, x + 1 + ((20 - WriteWidth(label)) / 2), y + 6, label, Ink);
        }
        else
        {
            Write(image, x + 1 + ((20 - WriteWidth(label)) / 2), y + 3, label, Ink);
            Write(image, x + 1 + ((20 - WriteWidth(second)) / 2), y + 10, second, Ink);
        }
    }
}

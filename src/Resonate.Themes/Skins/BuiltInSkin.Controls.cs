namespace Resonate.Themes.Skins;

/// <summary>The built-in skin's keys, toggles and sliders.</summary>
internal static partial class BuiltInSkin
{
    // Transport glyphs: seven rows tall, the play triangle nine so it looks as big as the square.
    private const string PreviousGlyph = "##....#/##...##/##..###/##.####/##..###/##...##/##....#";
    private const string PlayGlyph = "#..../##.../###../####./#####/####./###../##.../#....";
    private const string PauseGlyph = "##..##/##..##/##..##/##..##/##..##/##..##/##..##";
    private const string StopGlyph = "#######/#######/#######/#######/#######/#######/#######";
    private const string NextGlyph = "#....##/##...##/###..##/####.##/###..##/##...##/#....##";
    private const string EjectGlyph = "...#.../..###../.#####./#######/......./#######";

    // Toggle glyphs.
    private const string ShuffleGlyph = "........#../###...#####/...#.#..#../....#....../...#.#..#../###...#####/........#..";
    private const string RepeatGlyph = "..#....../.#######./#.#.....#/#.......#/#.....#.#/.#######./......#..";

    /// <summary>cbuttons.bmp: previous, play, pause, stop, next and eject, raised and pressed.</summary>
    private static SkinImage DrawButtons()
    {
        var image = NewSheet(SkinSheet.CButtons, BodyAt(88));
        ReadOnlySpan<int> lefts = [0, 23, 46, 69, 92];
        ReadOnlySpan<int> widths = [23, 23, 23, 23, 22];
        ReadOnlySpan<string> glyphs = [PreviousGlyph, PlayGlyph, PauseGlyph, StopGlyph, NextGlyph];
        for (var i = 0; i < lefts.Length; i++)
        {
            Key(image, lefts[i], 0, widths[i], 18, 88, glyphs[i], pressed: false);
            Key(image, lefts[i], 18, widths[i], 18, 88, glyphs[i], pressed: true);
        }

        Key(image, 114, 0, 22, 16, 89, EjectGlyph, pressed: false);
        Key(image, 114, 16, 22, 16, 89, EjectGlyph, pressed: true);
        BodyBehind(image, 114, 32, 22, 4, 105);
        return image;
    }

    /// <summary>
    /// A key: an outlined, rounded block lit from the top left with a light
    /// glyph, or, pressed, sunk in with the glyph turned violet and moved down
    /// a pixel. <paramref name="windowY"/> is the row it is drawn at, for the
    /// body showing at its corners.
    /// </summary>
    private static void Key(SkinImage image, int x, int y, int width, int height, int windowY, string glyph, bool pressed)
    {
        KeyFace(image, x, y, width, height, windowY, pressed);
        var (glyphX, glyphY) = Centre(glyph, x, y, width, height);
        if (pressed)
        {
            Art(image, glyphX, glyphY + 1, glyph, InkLit);
        }
        else
        {
            Embossed(image, glyphX, glyphY, glyph, Ink, InkShadow);
        }
    }

    /// <summary>A key without its glyph: raised with a lit top-left edge, or sunk with a shaded one.</summary>
    private static void KeyFace(SkinImage image, int x, int y, int width, int height, int windowY, bool pressed)
    {
        image.FillRect(x, y, width, height, Outline);
        if (pressed)
        {
            image.FillRect(x + 1, y + 1, width - 2, height - 2, KeySunk);
            Edge(image, x + 1, y + 1, width - 2, height - 2, KeySunkShadow, KeySunkLight);
        }
        else
        {
            for (var row = 2; row < height - 2; row++)
            {
                HLine(image, x + 2, y + row, width - 4, Mix(KeyTop, KeyBottom, (row - 2) / (double)(height - 5)));
            }

            Edge(image, x + 1, y + 1, width - 2, height - 2, KeyLight, KeyShadow);
        }

        Round(image, x, y, width, height, BodyAt(windowY), BodyAt(windowY + height - 1));
    }

    /// <summary>posbar.bmp: a dark groove the length of the song, and the violet pill that rides it.</summary>
    private static SkinImage DrawPosBar()
    {
        var image = NewSheet(SkinSheet.PosBar, BodyAt(76));
        BodyBehind(image, 0, 0, 307, 10, 72);

        // The groove, rows 3 to 6, with rounded ends.
        HLine(image, 2, 3, 244, KeySunkShadow);
        HLine(image, 1, 4, 246, WellFill);
        HLine(image, 1, 5, 246, WellFill);
        HLine(image, 2, 6, 244, KeySunkLight);
        Plot(image, 1, 4, KeySunkShadow);
        Plot(image, 246, 5, KeySunkLight);

        // Quiet ticks at every tenth of the way, under the groove.
        for (var i = 0; i <= 10; i++)
        {
            Plot(image, 14 + (i * 22), 8, BodyLight);
        }

        PositionThumb(image, 248, Mix(0xFFB0A6FF, Violet, 0.15), 0xFF6A5CDB, 0xFFD4CEFF);
        PositionThumb(image, 278, 0xFFCBC4FF, 0xFF8F81FF, 0xFFF2F0FF);
        return image;
    }

    private static void PositionThumb(SkinImage image, int x, uint top, uint bottom, uint shine)
    {
        BodyBehind(image, x, 0, 29, 10, 72);
        for (var row = 1; row < 9; row++)
        {
            var inset = row is 1 or 8 ? 2 : row is 2 or 7 ? 1 : 0;
            HLine(image, x + inset, row, 29 - (2 * inset), Mix(top, bottom, (row - 1) / 7.0));
        }

        HLine(image, x + 3, 1, 23, shine);
        HLine(image, x + 3, 8, 23, Mix(bottom, Outline, 0.35));

        // Three grip lines in the middle.
        for (var i = 0; i < 3; i++)
        {
            VLine(image, x + 12 + (i * 2), 3, 4, Mix(bottom, Outline, 0.3));
        }
    }

    /// <summary>
    /// volume.bmp: 28 frames of a rising row of bars, lit from the left in
    /// cyan turning violet as the volume goes up, and the fader cap (pressed
    /// on the left, at rest on the right).
    /// </summary>
    private static SkinImage DrawVolume()
    {
        var image = NewSheet(SkinSheet.Volume, BodyAt(63));
        // 22 bars three pixels apart, so the cap covers the first and the last at either end of its travel.
        const int Bars = 22;
        for (var frame = 0; frame < 28; frame++)
        {
            var y = frame * 15;
            BodyBehind(image, 0, y, 68, 15, 57);
            var lit = frame * Bars / 27.0;
            for (var bar = 0; bar < Bars; bar++)
            {
                var height = 2 + (int)Math.Round(bar * 7 / (double)(Bars - 1));
                var colour = LevelBar(lit - bar, bar / (double)(Bars - 1));
                image.FillRect(bar * 3, y + 10 - height, 2, height, colour);
            }
        }

        BodyBehind(image, 0, 420, 68, 13, 56);
        FaderCap(image, 0, 422, 58, pressed: true);
        FaderCap(image, 15, 422, 58, pressed: false);
        return image;
    }

    /// <summary>balance.bmp: 28 frames lighting bars outward from a centre mark (the same either way), and the fader cap.</summary>
    private static SkinImage DrawBalance()
    {
        var image = NewSheet(SkinSheet.Balance, BodyAt(63));
        // Frames start at x = 9 (the columns before them are unused); the centre mark is at 18 and 19 of the 38.
        const int Bars = 6;
        for (var frame = 0; frame < 28; frame++)
        {
            var y = frame * 15;
            BodyBehind(image, 0, y, 47, 15, 57);
            var lit = frame * Bars / 27.0;
            image.FillRect(9 + 18, y + 3, 2, 7, frame == 0 ? Violet : Mix(Violet, KeySunkLight, 0.4));
            for (var bar = 0; bar < Bars; bar++)
            {
                var height = 3 + bar;
                var colour = LevelBar(lit - bar, bar / (double)(Bars - 1));
                image.FillRect(9 + 21 + (bar * 3), y + 10 - height, 2, height, colour);
                image.FillRect(9 + 15 - (bar * 3), y + 10 - height, 2, height, colour);
            }
        }

        BodyBehind(image, 0, 420, 47, 13, 56);
        FaderCap(image, 0, 422, 58, pressed: true);
        FaderCap(image, 15, 422, 58, pressed: false);
        return image;
    }

    /// <summary>
    /// A level bar's colour: unlit, lit in a colour <paramref name="along"/>
    /// the way from cyan to violet, or part lit when the level ends inside it
    /// (<paramref name="fill"/> between 0 and 1), so every frame differs.
    /// </summary>
    private static uint LevelBar(double fill, double along) =>
        Mix(KeySunkLight, Mix(Cyan, Violet, along), Math.Clamp(fill, 0, 1));

    /// <summary>A 14x11 fader cap: a small raised key with a violet line down its middle (cyan and sunk while held).</summary>
    private static void FaderCap(SkinImage image, int x, int y, int windowY, bool pressed)
    {
        KeyFace(image, x, y, 14, 11, windowY, pressed);
        VLine(image, x + 6, y + (pressed ? 3 : 2), 6, pressed ? Cyan : Violet);
        VLine(image, x + 7, y + (pressed ? 3 : 2), 6, pressed ? Mix(Cyan, Outline, 0.4) : Mix(Violet, Outline, 0.45));
    }

    /// <summary>
    /// shufrep.bmp: shuffle and repeat keys with an icon and a lamp, and the
    /// small EQ and PL keys; each off, off pressed, on (violet) and on pressed.
    /// </summary>
    private static SkinImage DrawToggles()
    {
        var image = NewSheet(SkinSheet.ShufRep, BodyAt(96));
        for (var state = 0; state < 4; state++)
        {
            var on = state >= 2;
            var pressed = state % 2 == 1;
            Toggle(image, 0, state * 15, 28, 15, 89, RepeatGlyph, on, pressed);
            Toggle(image, 28, state * 15, 47, 15, 89, ShuffleGlyph, on, pressed);
        }

        BodyBehind(image, 0, 60, 92, 1, 69);
        SmallToggle(image, 0, 61, "EQ", on: false, pressed: false);
        SmallToggle(image, 46, 61, "EQ", on: false, pressed: true);
        SmallToggle(image, 0, 73, "EQ", on: true, pressed: false);
        SmallToggle(image, 46, 73, "EQ", on: true, pressed: true);
        SmallToggle(image, 23, 61, "PL", on: false, pressed: false);
        SmallToggle(image, 69, 61, "PL", on: false, pressed: true);
        SmallToggle(image, 23, 73, "PL", on: true, pressed: false);
        SmallToggle(image, 69, 73, "PL", on: true, pressed: true);
        return image;
    }

    private static void Toggle(SkinImage image, int x, int y, int width, int height, int windowY, string glyph, bool on, bool pressed)
    {
        KeyFace(image, x, y, width, height, windowY, pressed);
        var shift = pressed ? 1 : 0;
        // Centre the icon in the room right of the lamp (the shuffle key's last column hides under repeat's).
        var room = (width > 30 ? width - 1 : width) - 9;
        var glyphX = x + 7 + ((room - ArtWidth(glyph)) / 2);
        var glyphY = y + ((height - ArtHeight(glyph)) / 2) + shift;
        var ink = on ? InkLit : pressed ? InkDim : Ink;
        if (pressed)
        {
            Art(image, glyphX, glyphY, glyph, ink);
        }
        else
        {
            Embossed(image, glyphX, glyphY, glyph, ink, InkShadow);
        }

        ToggleLamp(image, x + 4, y + 5 + shift, 4, on);
    }

    private static void SmallToggle(SkinImage image, int x, int y, string label, bool on, bool pressed)
    {
        KeyFace(image, x, y, 23, 12, 58, pressed);
        var shift = pressed ? 1 : 0;
        var textX = x + 11;
        var textY = y + 3 + shift;
        var ink = on ? InkLit : pressed ? InkDim : Ink;
        if (pressed)
        {
            Write(image, textX, textY, label, ink);
        }
        else
        {
            Write(image, textX, textY + 1, label, InkShadow);
            Write(image, textX, textY, label, ink);
        }

        ToggleLamp(image, x + 5, y + 4 + shift, 3, on);
    }

    /// <summary>A toggle's lamp: a short dark slot, or a violet bar glowing into the key while on.</summary>
    private static void ToggleLamp(SkinImage image, int x, int y, int height, bool on)
    {
        if (on)
        {
            image.FillRect(x - 1, y - 1, 4, height + 2, LcdGlow);
            image.FillRect(x, y, 2, height, Violet);
            Plot(image, x, y, Lcd);
        }
        else
        {
            image.FillRect(x, y, 2, height, KeySunkShadow);
        }
    }
}

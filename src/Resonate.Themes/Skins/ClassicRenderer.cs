using static Resonate.Themes.Skins.ClassicLayout;

namespace Resonate.Themes.Skins;

/// <summary>
/// Draws the classic player's main window from a skin, pixel for pixel as
/// Winamp 2 did: 275 x 116 skin pixels, or the 275 x 14 bar in shade mode.
/// </summary>
public static class ClassicRenderer
{
    public const int Width = 275;
    public const int Height = 116;
    public const int ShadeHeight = 14;

    private const uint Black = 0xFF000000;

    // The visualiser: 16 rows (5 in shade mode); the bars use 75 of its 76 columns (37 of 38).
    private const int Rows = 16;
    private const int MaxBarHeight = 15;
    private const int BarColumns = 75;
    private const int ClassicBars = 19;
    private const int ShadeRows = 5;
    private const int ShadeColumns = 37;
    private const int ShadeScopeColumns = 38;
    private const int ShadeClassicBars = 10;

    // viscolor.txt: 0 background, 1 grid dots, 2-17 bar rows from the top, 18-22 the scope, 23 peaks.
    private const int BackgroundColour = 0;
    private const int GridColour = 1;
    private const int FirstBarColour = 2;
    private const int FirstScopeColour = 18;
    private const int PeakColour = 23;

    /// <summary>
    /// Draws the whole window into <paramref name="target"/>, which is
    /// <see cref="Width"/> wide and <see cref="Height"/> (or, shaded,
    /// <see cref="ShadeHeight"/>) tall. The visualiser shows
    /// <paramref name="frame"/>; without one it is drawn at rest.
    /// </summary>
    public static void Render(Skin skin, ClassicView view, SkinImage target, VisualiserFrame? frame = null)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(target);
        var height = view.Shaded ? ShadeHeight : Height;
        if (target.Width < Width || target.Height < height)
        {
            throw new ArgumentException($"The classic window needs a picture of {Width} x {height} pixels.", nameof(target));
        }

        // A sheet smaller than it should be leaves gaps; they show black, not the previous picture.
        target.FillRect(0, 0, Width, height, Black);
        if (view.Shaded)
        {
            RenderShaded(skin, view, target, frame);
        }
        else
        {
            RenderNormal(skin, view, target, frame);
        }
    }

    /// <summary>
    /// Draws only the visualiser, into a picture the size of
    /// <see cref="ClassicLayout.VisualiserArea"/>; the control lays it over
    /// the window and redraws it every frame while a local file plays.
    /// </summary>
    /// <remarks>With <see cref="VisualiserMode.Off"/> it draws what the window has there instead (main.bmp, or the active shade bar).</remarks>
    public static void RenderVisualiser(Skin skin, VisualiserMode mode, VisualiserFrame? frame, bool shaded, SkinImage target)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(target);
        if (mode == VisualiserMode.Off)
        {
            var area = shaded ? VisualiserShaded : VisualiserNormal;
            var under = shaded ? SkinSprites.ShadeBarActive : SkinSprites.Background;
            target.FillRect(0, 0, area.Width, area.Height, Black);
            target.Draw(skin.Sheet(under.Sheet), under.X + area.X, under.Y + area.Y, area.Width, area.Height, 0, 0);
            return;
        }

        DrawVisualiser(skin, mode, frame, shaded, target, 0, 0);
    }

    private static void RenderNormal(Skin skin, ClassicView view, SkinImage target, VisualiserFrame? frame)
    {
        // Draw order of Winamp's main window (skin-format.md 1.12): later parts cover earlier ones.
        target.Draw(skin, SkinSprites.Background, 0, 0);
        target.Draw(skin, view.WindowActive ? SkinSprites.TitleBarActive : SkinSprites.TitleBarInactive, TitleBar.X, TitleBar.Y);
        DrawTitleButtons(skin, view, target);
        DrawClutterBar(skin, view, target);
        DrawStatus(skin, view, target);
        DrawTime(skin, view, target);
        if (ShowsVisualiser(view))
        {
            DrawVisualiser(skin, view.Visualiser, frame, false, target, VisualiserNormal.X, VisualiserNormal.Y);
        }

        DrawMarquee(skin, view, target);
        if (view.State != ClassicPlayState.Stopped)
        {
            PixelFont.Draw(skin, target, view.Kbps, Kbps.X, Kbps.Y, Kbps.Width);
            PixelFont.Draw(skin, target, view.Khz, Khz.X, Khz.Y, Khz.Width);
        }

        // Both lamps always show; one lights while something is loaded.
        var live = view.State != ClassicPlayState.Stopped;
        target.Draw(skin, live && view.Mono && !view.Stereo ? SkinSprites.MonoLit : SkinSprites.MonoUnlit, MonoLamp.X, MonoLamp.Y);
        target.Draw(skin, live && view.Stereo ? SkinSprites.StereoLit : SkinSprites.StereoUnlit, StereoLamp.X, StereoLamp.Y);

        DrawSliders(skin, view, target);
        target.Draw(skin, SkinSprites.EqualizerToggle.For(view.EqualizerOn, view.Pressed == ClassicControl.Equalizer), Equalizer.X, Equalizer.Y);
        target.Draw(skin, SkinSprites.PlaylistToggle.For(view.PlaylistOn, view.Pressed == ClassicControl.Playlist), Playlist.X, Playlist.Y);

        target.Draw(skin, SkinSprites.SeekTrack, Seek.X, Seek.Y);
        if (view.Seek is { } seek)
        {
            var thumb = view.Pressed == ClassicControl.Seek ? SkinSprites.SeekThumbPressed : SkinSprites.SeekThumb;
            target.Draw(skin, thumb, Seek.X + SeekOffset(seek), Seek.Y);
        }

        DrawButton(skin, view, target, ClassicControl.Previous, SkinSprites.Previous, SkinSprites.PreviousPressed, Previous);
        DrawButton(skin, view, target, ClassicControl.Play, SkinSprites.Play, SkinSprites.PlayPressed, Play);
        DrawButton(skin, view, target, ClassicControl.Pause, SkinSprites.Pause, SkinSprites.PausePressed, Pause);
        DrawButton(skin, view, target, ClassicControl.Stop, SkinSprites.Stop, SkinSprites.StopPressed, Stop);
        DrawButton(skin, view, target, ClassicControl.Next, SkinSprites.Next, SkinSprites.NextPressed, Next);
        DrawButton(skin, view, target, ClassicControl.Eject, SkinSprites.Eject, SkinSprites.EjectPressed, Eject);

        // Shuffle first: repeat's first column covers shuffle's last.
        target.Draw(skin, SkinSprites.ShuffleToggle.For(view.Shuffle, view.Pressed == ClassicControl.Shuffle), Shuffle.X, Shuffle.Y);
        target.Draw(skin, SkinSprites.RepeatToggle.For(view.Repeat, view.Pressed == ClassicControl.Repeat), Repeat.X, Repeat.Y);
    }

    private static void RenderShaded(Skin skin, ClassicView view, SkinImage target, VisualiserFrame? frame)
    {
        target.Draw(skin, view.WindowActive ? SkinSprites.ShadeBarActive : SkinSprites.ShadeBarInactive, TitleBar.X, TitleBar.Y);
        DrawTitleButtons(skin, view, target);
        if (ShowsVisualiser(view))
        {
            DrawVisualiser(skin, view.Visualiser, frame, true, target, VisualiserShaded.X, VisualiserShaded.Y);
        }

        // The time in text glyphs; all five cells are spaces while stopped or blinked off.
        var text = skin.Sheet(SkinSheet.Text);
        Span<int> digits = stackalloc int[4];
        var shown = ShowsTime(view);
        var minus = shown && TimeDigits(view, digits);
        var cells = ShadeTimeX;
        PixelFont.DrawGlyph(text, target, minus ? '-' : ' ', cells[0], ShadeTimeY, PixelFont.CellWidth);
        for (var i = 0; i < digits.Length; i++)
        {
            PixelFont.DrawGlyph(text, target, shown ? (char)('0' + digits[i]) : ' ', cells[i + 1], ShadeTimeY, PixelFont.CellWidth);
        }

        target.Draw(skin, SkinSprites.ShadeSeekTrack, ShadeSeek.X, ShadeSeek.Y);
        if (view.Seek is { } seek)
        {
            // One thumb with three looks, by how far along it is.
            var offset = ShadeSeekOffset(seek);
            var thumb = offset < 6 ? SkinSprites.ShadeSeekThumbLeft : offset < 9 ? SkinSprites.ShadeSeekThumbMiddle : SkinSprites.ShadeSeekThumbRight;
            target.Draw(skin, thumb, ShadeSeek.X + offset, ShadeSeek.Y);
        }
    }

    /// <summary>
    /// The title bar's own art already shows the buttons, so only a pressed
    /// one is drawn (and, shaded, the unshade button while the window is
    /// active); an inactive bar stays exactly as the skin drew it.
    /// </summary>
    private static void DrawTitleButtons(Skin skin, ClassicView view, SkinImage target)
    {
        switch (view.Pressed)
        {
            case ClassicControl.Options:
                target.Draw(skin, SkinSprites.OptionsPressed, Options.X, Options.Y);
                break;
            case ClassicControl.Minimize:
                target.Draw(skin, SkinSprites.MinimizePressed, Minimize.X, Minimize.Y);
                break;
            case ClassicControl.Close:
                target.Draw(skin, SkinSprites.ClosePressed, Close.X, Close.Y);
                break;
            case ClassicControl.Shade:
                target.Draw(skin, view.Shaded ? SkinSprites.UnshadePressed : SkinSprites.ShadePressed, Shade.X, Shade.Y);
                return;
            default:
                break;
        }

        if (view.Shaded && view.WindowActive)
        {
            target.Draw(skin, SkinSprites.UnshadeButton, Shade.X, Shade.Y);
        }
    }

    private static void DrawClutterBar(Skin skin, ClassicView view, SkinImage target)
    {
        target.Draw(skin, SkinSprites.ClutterBar, ClutterBar.X, ClutterBar.Y);
        switch (view.Pressed)
        {
            case ClassicControl.ClutterOptions:
                target.Draw(skin, SkinSprites.ClutterOptionsLit, ClutterOptions.X, ClutterOptions.Y);
                break;
            case ClassicControl.ClutterAlwaysOnTop:
                target.Draw(skin, SkinSprites.ClutterAlwaysOnTopLit, ClutterAlwaysOnTop.X, ClutterAlwaysOnTop.Y);
                break;
            case ClassicControl.ClutterInfo:
                target.Draw(skin, SkinSprites.ClutterInfoLit, ClutterInfo.X, ClutterInfo.Y);
                break;
            case ClassicControl.ClutterVisualiser:
                target.Draw(skin, SkinSprites.ClutterVisualiserLit, ClutterVisualiser.X, ClutterVisualiser.Y);
                break;
            default:
                break;
        }

        // D stays lit while the window is doubled, as well as while it is held.
        if (view.DoubleSize || view.Pressed == ClassicControl.ClutterDoubleSize)
        {
            target.Draw(skin, SkinSprites.ClutterDoubleSizeLit, ClutterDoubleSize.X, ClutterDoubleSize.Y);
        }
    }

    /// <summary>
    /// The lamp (hidden while working, as webamp does), then the work
    /// indicator while playing or working: its third column covers the
    /// lamp's first, as in webamp and Audacious.
    /// </summary>
    private static void DrawStatus(Skin skin, ClassicView view, SkinImage target)
    {
        if (view.Working)
        {
            target.Draw(skin, SkinSprites.Working, WorkIndicator.X, WorkIndicator.Y);
            return;
        }

        var lamp = view.State switch
        {
            ClassicPlayState.Playing => SkinSprites.PlayingLamp,
            ClassicPlayState.Paused => SkinSprites.PausedLamp,
            _ => SkinSprites.StoppedLamp,
        };
        target.Draw(skin, lamp, Lamp.X, Lamp.Y);
        if (view.State == ClassicPlayState.Playing)
        {
            target.Draw(skin, SkinSprites.NotWorking, WorkIndicator.X, WorkIndicator.Y);
        }
    }

    private static void DrawTime(Skin skin, ClassicView view, SkinImage target)
    {
        if (!ShowsTime(view))
        {
            return;
        }

        Span<int> digits = stackalloc int[4];
        var minus = TimeDigits(view, digits);

        // nums_ex.bmp has a minus cell; a skin with only numbers.bmp gets the
        // blank cell with the middle bar of its 2 laid on it, as Winamp did.
        var sheet = skin.OwnSheet(SkinSheet.NumsEx) ?? skin.OwnSheet(SkinSheet.Numbers) ?? skin.Sheet(SkinSheet.NumsEx);
        if (sheet.Width >= SkinSprites.DigitWidth * (SkinSprites.MinusCell + 1))
        {
            DrawDigit(sheet, target, minus ? SkinSprites.MinusCell : SkinSprites.BlankCell, TimeSign.X, TimeSign.Y);
        }
        else
        {
            DrawDigit(sheet, target, SkinSprites.BlankCell, TimeSign.X, TimeSign.Y);
            if (minus)
            {
                var bar = SkinSprites.NumbersMinus;
                target.Draw(sheet, bar.X, bar.Y, bar.Width, bar.Height, NumbersMinusX, NumbersMinusY);
            }
        }

        var places = TimeDigitsX;
        for (var i = 0; i < digits.Length; i++)
        {
            DrawDigit(sheet, target, digits[i], places[i], TimeDigitsY);
        }
    }

    private static void DrawDigit(SkinImage sheet, SkinImage target, int cell, int x, int y) =>
        target.Draw(sheet, SkinSprites.DigitWidth * cell, 0, SkinSprites.DigitWidth, SkinSprites.DigitHeight, x, y);

    /// <summary>The time is hidden while stopped and during the dark half of the paused blink.</summary>
    private static bool ShowsTime(ClassicView view) =>
        view.State == ClassicPlayState.Playing || (view.State == ClassicPlayState.Paused && view.BlinkOn);

    private static bool ShowsVisualiser(ClassicView view) =>
        view.State != ClassicPlayState.Stopped && view.Visualiser != VisualiserMode.Off;

    /// <summary>
    /// The four digits the time display shows (MM:SS, or HH:MM from 100
    /// minutes on, as Audacious does), and whether it counts down with a
    /// minus. Counting down needs a known length.
    /// </summary>
    private static bool TimeDigits(ClassicView view, Span<int> digits)
    {
        // In seconds rather than TimeSpans, which would throw on overflow.
        var remaining = view.ShowRemaining && view.Duration > TimeSpan.Zero;
        var time = remaining ? view.Duration.TotalSeconds - view.Elapsed.TotalSeconds : view.Elapsed.TotalSeconds;
        var seconds = (long)Math.Clamp(Math.Floor(time), 0, 99 * 3600);
        var (high, low) = seconds >= 6000
            ? (Math.Min(seconds / 3600, 99), seconds / 60 % 60)
            : (seconds / 60, seconds % 60);
        digits[0] = (int)(high / 10);
        digits[1] = (int)(high % 10);
        digits[2] = (int)(low / 10);
        digits[3] = (int)(low % 10);
        return remaining;
    }

    /// <summary>The song line in the skin's font, padded with space glyphs to fill the box, the 31st cell cut to 4 pixels.</summary>
    private static void DrawMarquee(Skin skin, ClassicView view, SkinImage target)
    {
        var sheet = skin.Sheet(SkinSheet.Text);
        var line = view.Marquee ?? string.Empty;
        for (var i = 0; i < Marquee.Cells; i++)
        {
            var left = MarqueeText.X + (i * PixelFont.CellWidth);
            var width = Math.Min(PixelFont.CellWidth, MarqueeText.Right - left);
            PixelFont.DrawGlyph(sheet, target, Marquee.CharAt(line, view.MarqueeOffset, i), left, MarqueeText.Y, width);
        }
    }

    private static void DrawSliders(Skin skin, ClassicView view, SkinImage target)
    {
        var volume = VolumeOffset(view.Volume);
        target.Draw(skin, SkinSprites.VolumeFrame(VolumeFrame(volume)), Volume.X, Volume.Y);
        target.Draw(skin, view.Pressed == ClassicControl.Volume ? SkinSprites.VolumeThumbPressed : SkinSprites.VolumeThumb, Volume.X + volume, SliderThumbY);

        var balance = BalanceOffset(view.Balance);
        target.Draw(skin, SkinSprites.BalanceFrame(BalanceFrame(balance)), Balance.X, Balance.Y);
        target.Draw(skin, view.Pressed == ClassicControl.Balance ? SkinSprites.BalanceThumbPressed : SkinSprites.BalanceThumb, Balance.X + balance, SliderThumbY);
    }

    private static void DrawButton(Skin skin, ClassicView view, SkinImage target, ClassicControl control, Sprite normal, Sprite pressed, PixelRect place) =>
        target.Draw(skin, view.Pressed == control ? pressed : normal, place.X, place.Y);

    /// <summary>
    /// The visualiser at (<paramref name="left"/>, <paramref name="top"/>):
    /// Winamp's background and grid, then the bars or the trace. No frame, or
    /// one at rest (a Spotify song, or silence), leaves just the background.
    /// </summary>
    private static void DrawVisualiser(Skin skin, VisualiserMode mode, VisualiserFrame? frame, bool shaded, SkinImage target, int left, int top)
    {
        var colours = skin.VisColors;
        var area = shaded ? VisualiserShaded : VisualiserNormal;
        target.FillRect(left, top, area.Width, area.Height, Colour(colours, BackgroundColour));
        if (!shaded)
        {
            // Grid dots on every other column of the odd rows; shade mode has none.
            var grid = Colour(colours, GridColour);
            for (var y = 1; y < Rows; y += 2)
            {
                for (var x = 0; x < BarColumns; x += 2)
                {
                    Plot(target, left + x, top + y, grid);
                }
            }
        }

        if (frame is null || frame.IsAtRest)
        {
            return;
        }

        switch (mode, shaded)
        {
            case (VisualiserMode.Spectrum, false):
                DrawSpectrum(colours, frame, target, left, top);
                break;
            case (VisualiserMode.Spectrum, true):
                DrawShadeSpectrum(colours, frame, target, left, top);
                break;
            case (VisualiserMode.Oscilloscope, false):
                DrawScope(colours, frame, target, left, top);
                break;
            case (VisualiserMode.Oscilloscope, true):
                DrawShadeScope(colours, frame, target, left, top);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Winamp's bars: 19 of 3 columns with a gap, or 75 of one column, rows
    /// coloured 2 (top) to 17 (bottom) by where they are, each with a peak
    /// cap in colour 23 one row above a bar of the cap's height.
    /// </summary>
    private static void DrawSpectrum(IReadOnlyList<uint> colours, VisualiserFrame frame, SkinImage target, int left, int top)
    {
        var count = Math.Clamp(frame.BarCount, 0, VisualiserFrame.MaxBars);
        var thin = count >= BarColumns;
        var bars = thin ? BarColumns : Math.Min(count, ClassicBars);
        var pitch = thin ? 1 : 4;
        var width = thin ? 1 : 3;
        var peak = Colour(colours, PeakColour);
        for (var b = 0; b < bars; b++)
        {
            var x = left + (b * pitch);
            var height = Math.Clamp(RoundHalfUp(frame.Bars[b]), 0, MaxBarHeight);
            for (var row = Rows - height; row < Rows; row++)
            {
                target.FillRect(x, top + row, width, 1, Colour(colours, FirstBarColour + row));
            }

            var cap = Math.Clamp(RoundHalfUp(frame.Peaks[b]), 0, MaxBarHeight);
            if (cap >= 1)
            {
                target.FillRect(x, top + MaxBarHeight - cap, width, 1, peak);
            }
        }
    }

    /// <summary>
    /// The shade bar's bars (webamp's look): 37 columns, 10 bars of 3 (or 37
    /// of one), 5 rows coloured 4, 8, 11, 14 and 17 from the top, no caps.
    /// Each shows the mean of the frame's bars it stands for, scaled from 15 rows to 5.
    /// </summary>
    private static void DrawShadeSpectrum(IReadOnlyList<uint> colours, VisualiserFrame frame, SkinImage target, int left, int top)
    {
        ReadOnlySpan<int> rowColours = [4, 8, 11, 14, 17];
        var count = Math.Clamp(frame.BarCount, 0, VisualiserFrame.MaxBars);
        if (count == 0)
        {
            return;
        }

        var thin = count >= BarColumns;
        var bars = thin ? ShadeColumns : ShadeClassicBars;
        var pitch = thin ? 1 : 4;
        for (var b = 0; b < bars; b++)
        {
            var first = b * count / bars;
            var end = Math.Max(first + 1, (b + 1) * count / bars);
            var sum = 0f;
            for (var i = first; i < end; i++)
            {
                sum += frame.Bars[i];
            }

            var x = b * pitch;
            var width = Math.Min(thin ? 1 : 3, ShadeColumns - x);
            var height = Math.Clamp(RoundHalfUp(sum / (end - first) * ShadeRows / MaxBarHeight), 0, ShadeRows);
            for (var row = ShadeRows - height; row < ShadeRows; row++)
            {
                target.FillRect(left + x, top + row, width, 1, Colour(colours, rowColours[row]));
            }
        }
    }

    /// <summary>
    /// Winamp's oscilloscope, "lines" style: each of 75 columns joins its
    /// sample's row to the previous column's, in the colour (18 to 22) of
    /// how far its sample is from the middle.
    /// </summary>
    private static void DrawScope(IReadOnlyList<uint> colours, VisualiserFrame frame, SkinImage target, int left, int top)
    {
        var last = 0;
        for (var x = 0; x < BarColumns; x++)
        {
            var y = ScopeRow(frame.Scope[x]);
            if (x == 0)
            {
                last = y;
            }

            var (from, to) = (y, last);
            last = y;
            if (to < from)
            {
                // Going down, the run starts below the previous sample (webamp's "top++", as Winamp drew it).
                (from, to) = (to + 1, from);
            }

            target.FillRect(left + x, top + from, 1, to - from + 1, Colour(colours, FirstScopeColour + ScopeShade(y)));
        }
    }

    /// <summary>The shade bar's trace (webamp's look): 38 columns, 5 rows, all in colour 18.</summary>
    private static void DrawShadeScope(IReadOnlyList<uint> colours, VisualiserFrame frame, SkinImage target, int left, int top)
    {
        var colour = Colour(colours, FirstScopeColour);
        var last = 0;
        for (var x = 0; x < ShadeScopeColumns; x++)
        {
            // Winamp's row for 16 rows, moved up 5, then squeezed into 5.
            var y = Math.Clamp(RoundHalfUp((ScopeRowUnclamped(frame.Scope[x]) - 5 + 11) / 16.0 * ShadeRows) - 2, 0, ShadeRows - 1);
            if (x == 0)
            {
                last = y;
            }

            var (from, to) = (Math.Min(y, last), Math.Max(y, last));
            last = y;
            target.FillRect(left + x, top + from, 1, to - from + 1, colour);
        }
    }

    /// <summary>Winamp's scope row (0 at the top, 7 for silence) for a trace value of -1 to 1.</summary>
    private static int ScopeRow(float value) => Math.Clamp(ScopeRowUnclamped(value), 0, MaxBarHeight);

    private static int ScopeRowUnclamped(float value)
    {
        // The trace is the sample doubled, so 64 per unit here is Winamp's 128 per unit of sample.
        var sample = float.IsFinite(value) ? value : 0;
        var level = Math.Clamp(RoundHalfUp((64.0 * sample) + 128), 0, 255);
        return RoundHalfUp(level / 8.0) - 9;
    }

    /// <summary>webamp's colour step for a scope row: 0 (colour 18) in the middle, up to 4 (colour 22) at the bottom.</summary>
    private static int ScopeShade(int row) => row switch
    {
        >= 14 => 4,
        >= 12 => 3,
        >= 10 => 2,
        >= 8 => 1,
        >= 6 => 0,
        >= 4 => 1,
        >= 2 => 2,
        _ => 3,
    };

    private static uint Colour(IReadOnlyList<uint> colours, int index) => colours[index] | Black;

    private static void Plot(SkinImage target, int x, int y, uint colour)
    {
        if (target.Contains(x, y))
        {
            target[x, y] = colour;
        }
    }
}

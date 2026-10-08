namespace Resonate.Themes.Skins;

/// <summary>A part of the classic playlist window that reacts to the pointer.</summary>
public enum PlaylistControl
{
    None,
    TitleBar,
    Shade,
    Close,

    /// <summary>The songs (drawn by the app over the frame).</summary>
    List,

    /// <summary>The scroll bar's column.</summary>
    Scroll,

    /// <summary>The bottom right corner that makes the window taller or shorter.</summary>
    Grip,

    // The menus along the bottom
    Add,
    Remove,
    Select,
    Misc,
    ListOptions,

    // The little transport in the bottom right
    Previous,
    Play,
    Pause,
    Stop,
    Next,
    Eject,
}

/// <summary>Everything the classic playlist window's frame shows at one moment (the songs themselves are the app's).</summary>
public sealed record PlaylistView
{
    /// <summary>The window's height in skin pixels (see <see cref="PlaylistLayout.SnapHeight"/>), when not rolled up.</summary>
    public int Height { get; init; } = PlaylistLayout.MinHeight;

    /// <summary>Rolled up into the 275 x 14 bar.</summary>
    public bool Shaded { get; init; }

    /// <summary>The window has focus: its frame is drawn active.</summary>
    public bool WindowActive { get; init; } = true;

    /// <summary>The control under a pressed pointer, drawn pressed.</summary>
    public PlaylistControl Pressed { get; init; }

    /// <summary>How far down the list is scrolled, 0 to 1 (the scroll handle's place).</summary>
    public double Scroll { get; init; }

    /// <summary>The selected songs' length and the whole list's, as Winamp wrote them ("0:00/1:03:06").</summary>
    public string RunningTime { get; init; } = string.Empty;

    /// <summary>The playing song's time ("1:23"), or empty.</summary>
    public string TrackTime { get; init; } = string.Empty;

    /// <summary>The rolled-up bar's song ("1. Artist - Title").</summary>
    public string ShadeTitle { get; init; } = string.Empty;

    /// <summary>The rolled-up bar's time, or empty.</summary>
    public string ShadeTime { get; init; } = string.Empty;
}

/// <summary>Where the playlist window's parts are, in skin pixels from its top left (Winamp's places, at its narrowest).</summary>
public static class PlaylistLayout
{
    public const int Width = 275;
    public const int ShadeHeight = 14;

    /// <summary>The window is at least this tall, and grows in steps of <see cref="HeightStep"/>, as Winamp's did.</summary>
    public const int MinHeight = 116;
    public const int HeightStep = 29;
    public const int MaxHeight = MinHeight + (HeightStep * 20);

    /// <summary>A song's row is 13 pixels tall.</summary>
    public const int RowHeight = 13;

    internal const int TitleHeight = 20;
    internal const int BottomHeight = 38;
    internal const int LeftWidth = 12;
    internal const int RightWidth = 20;
    internal const int TileWidth = 25;
    internal const int TileHeight = 29;
    internal const int ScrollX = 260;
    internal const int ScrollHandleWidth = 8;
    internal const int ScrollHandleHeight = 18;
    internal const int GripSize = 20;
    internal const int ShadeTimeRight = 30;

    // From the bottom bar's top: the menus' row, the running time, the song's time and the little transport.
    internal const int MenuTop = 8;
    internal const int MenuWidth = 22;
    internal const int MenuHeight = 18;
    internal const int RunningTimeX = 132;
    internal const int RunningTimeTop = 10;
    internal const int TrackTimeX = 191;
    internal const int TrackTimeTop = 23;
    internal const int TransportX = 128;
    internal const int TransportTop = 22;
    internal const int TransportCell = 10;

    internal static readonly PixelRect Close = new(264, 3, 9, 9);
    internal static readonly PixelRect Shade = new(254, 3, 9, 9);

    private static readonly (int X, PlaylistControl Control)[] Menus =
    [
        (14, PlaylistControl.Add),
        (43, PlaylistControl.Remove),
        (72, PlaylistControl.Select),
        (101, PlaylistControl.Misc),
        (231, PlaylistControl.ListOptions),
    ];

    private static readonly PlaylistControl[] Transport =
    [
        PlaylistControl.Previous,
        PlaylistControl.Play,
        PlaylistControl.Pause,
        PlaylistControl.Stop,
        PlaylistControl.Next,
        PlaylistControl.Eject,
    ];

    /// <summary>A height the window can have: within its limits, in whole steps from the smallest.</summary>
    public static int SnapHeight(int height)
    {
        var steps = (int)Math.Round((Math.Clamp(height, MinHeight, MaxHeight) - MinHeight) / (double)HeightStep, MidpointRounding.AwayFromZero);
        return MinHeight + (steps * HeightStep);
    }

    /// <summary>Where the songs are drawn: (x, y, width, height) in skin pixels, for a window <paramref name="height"/> tall.</summary>
    public static (int X, int Y, int Width, int Height) ListArea(int height)
    {
        height = SnapHeight(height);
        return (LeftWidth, TitleHeight, Width - LeftWidth - RightWidth, height - TitleHeight - BottomHeight);
    }

    /// <summary>How many whole rows fit in a window <paramref name="height"/> tall.</summary>
    public static int VisibleRows(int height) => ListArea(height).Height / RowHeight;

    /// <summary>The row (0 for the first one shown) at <paramref name="y"/> skin pixels from the window's top, or -1 outside the list.</summary>
    public static int RowAt(int y, int height)
    {
        var (_, top, _, listHeight) = ListArea(height);
        return y < top || y >= top + listHeight ? -1 : (y - top) / RowHeight;
    }

    /// <summary>The first of <paramref name="count"/> rows shown at a scroll of 0 to 1, with <paramref name="visible"/> rows on show.</summary>
    public static int FirstRow(double scroll, int count, int visible)
    {
        var hidden = Math.Max(count - Math.Max(visible, 0), 0);
        return ClassicLayout.RoundHalfUp(Math.Clamp(double.IsFinite(scroll) ? scroll : 0, 0, 1) * hidden);
    }

    /// <summary>The scroll (0 to 1) that shows row <paramref name="firstRow"/> first; 0 when every row fits.</summary>
    public static double ScrollOf(int firstRow, int count, int visible)
    {
        var hidden = count - visible;
        return hidden <= 0 ? 0 : Math.Clamp(firstRow / (double)hidden, 0, 1);
    }

    /// <summary>The scroll handle's top for a scroll of 0 to 1.</summary>
    internal static int ScrollHandleTop(int height, double scroll)
    {
        var travel = Math.Max(ListArea(height).Height - ScrollHandleHeight, 0);
        return TitleHeight + ClassicLayout.RoundHalfUp(Math.Clamp(double.IsFinite(scroll) ? scroll : 0, 0, 1) * travel);
    }

    /// <summary>
    /// The scroll (0 to 1) when the handle's top is at <paramref name="handleTop"/>
    /// (skin pixels from the window's top); pass the pointer's y less where it holds the handle.
    /// </summary>
    public static double ScrollAt(int height, double handleTop)
    {
        var travel = ListArea(height).Height - ScrollHandleHeight;
        return travel <= 0 ? 0 : Math.Clamp((handleTop - TitleHeight) / travel, 0, 1);
    }

    /// <summary>The control at a point of a window <paramref name="height"/> tall (or rolled up).</summary>
    public static PlaylistControl HitTest(int x, int y, int height, bool shaded)
    {
        height = shaded ? ShadeHeight : SnapHeight(height);
        if (x < 0 || y < 0 || x >= Width || y >= height)
        {
            return PlaylistControl.None;
        }

        if (Close.Contains(x, y))
        {
            return PlaylistControl.Close;
        }

        if (Shade.Contains(x, y))
        {
            return PlaylistControl.Shade;
        }

        if (shaded || y < TitleHeight)
        {
            return PlaylistControl.TitleBar;
        }

        var bottom = height - BottomHeight;
        if (y < bottom)
        {
            var (listX, _, listWidth, _) = ListArea(height);
            return x >= listX && x < listX + listWidth ? PlaylistControl.List
                : x >= ScrollX && x < ScrollX + ScrollHandleWidth ? PlaylistControl.Scroll
                : PlaylistControl.None;
        }

        if (x >= Width - GripSize && y >= height - GripSize)
        {
            return PlaylistControl.Grip;
        }

        foreach (var (menuX, control) in Menus)
        {
            if (new PixelRect(menuX, bottom + MenuTop, MenuWidth, MenuHeight).Contains(x, y))
            {
                return control;
            }
        }

        for (var i = 0; i < Transport.Length; i++)
        {
            if (new PixelRect(TransportX + (i * TransportCell), bottom + TransportTop, TransportCell, TransportCell).Contains(x, y))
            {
                return Transport[i];
            }
        }

        return PlaylistControl.None;
    }

    /// <summary>A length as Winamp's playlist wrote it: "3:07", or "1:03:06" from an hour on.</summary>
    public static string FormatTime(TimeSpan time)
    {
        var seconds = (long)Math.Clamp(Math.Floor(time.TotalSeconds), 0, (99 * 3600) + 3599);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}

/// <summary>Draws the classic playlist window's frame from a skin, as Winamp 2 did; the songs go on top.</summary>
public static class PlaylistRenderer
{
    private const uint Black = 0xFF000000;

    /// <summary>Draws the window into <paramref name="target"/>, at least 275 wide and as tall as the view says.</summary>
    public static void Render(Skin skin, PlaylistView view, SkinImage target)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(target);
        var height = view.Shaded ? PlaylistLayout.ShadeHeight : PlaylistLayout.SnapHeight(view.Height);
        if (target.Width < PlaylistLayout.Width || target.Height < height)
        {
            throw new ArgumentException($"The playlist needs a picture of {PlaylistLayout.Width} x {height} pixels.", nameof(target));
        }

        target.FillRect(0, 0, PlaylistLayout.Width, height, Black);
        if (view.Shaded)
        {
            RenderShaded(skin, view, target);
        }
        else
        {
            RenderNormal(skin, view, target, height);
        }
    }

    private static void RenderNormal(Skin skin, PlaylistView view, SkinImage target, int height)
    {
        const int Width = PlaylistLayout.Width;
        const int Tile = PlaylistLayout.TileWidth;
        var active = view.WindowActive;

        // The title bar: corners, the title in the middle, tiles either side of it.
        var inner = Width - (2 * Tile) - StackSprites.PlTitle.Width;
        var left = inner / 2;
        target.Draw(skin, active ? StackSprites.PlTopLeftActive : StackSprites.PlTopLeft, 0, 0);
        TileAcross(skin, target, active ? StackSprites.PlTopTileActive : StackSprites.PlTopTile, Tile, Tile + left, 0);
        target.Draw(skin, active ? StackSprites.PlTitleActive : StackSprites.PlTitle, Tile + left, 0);
        TileAcross(skin, target, active ? StackSprites.PlTopTileActive : StackSprites.PlTopTile, Tile + left + StackSprites.PlTitle.Width, Width - Tile, 0);
        target.Draw(skin, active ? StackSprites.PlTopRightActive : StackSprites.PlTopRight, Width - Tile, 0);

        // The sides, tiled down to the bottom bar.
        var bottom = height - PlaylistLayout.BottomHeight;
        for (var y = PlaylistLayout.TitleHeight; y < bottom; y += PlaylistLayout.TileHeight)
        {
            var rows = Math.Min(PlaylistLayout.TileHeight, bottom - y);
            Draw(skin, target, StackSprites.PlLeftTile, 0, y, StackSprites.PlLeftTile.Width, rows);
            Draw(skin, target, StackSprites.PlRightTile, Width - PlaylistLayout.RightWidth, y, StackSprites.PlRightTile.Width, rows);
        }

        target.Draw(skin, StackSprites.PlBottomLeft, 0, bottom);
        target.Draw(skin, StackSprites.PlBottomRight, Width - StackSprites.PlBottomRight.Width, bottom);

        var (listX, listY, listWidth, listHeight) = PlaylistLayout.ListArea(height);
        target.FillRect(listX, listY, listWidth, listHeight, skin.Playlist.NormalBackground | Black);

        var handle = view.Pressed == PlaylistControl.Scroll ? StackSprites.PlScrollHandlePressed : StackSprites.PlScrollHandle;
        target.Draw(skin, handle, PlaylistLayout.ScrollX, PlaylistLayout.ScrollHandleTop(height, view.Scroll));

        if (view.Pressed == PlaylistControl.Close)
        {
            target.Draw(skin, StackSprites.PlClosePressed, PlaylistLayout.Close.X, PlaylistLayout.Close.Y);
        }
        else if (view.Pressed == PlaylistControl.Shade)
        {
            target.Draw(skin, StackSprites.PlShadePressed, PlaylistLayout.Shade.X, PlaylistLayout.Shade.Y);
        }

        Text(skin, target, view.RunningTime, PlaylistLayout.RunningTimeX, bottom + PlaylistLayout.RunningTimeTop, 60);
        Text(skin, target, view.TrackTime, PlaylistLayout.TrackTimeX, bottom + PlaylistLayout.TrackTimeTop, 25);
    }

    private static void RenderShaded(Skin skin, PlaylistView view, SkinImage target)
    {
        const int Width = PlaylistLayout.Width;
        var right = view.WindowActive ? StackSprites.PlShadeRightActive : StackSprites.PlShadeRight;
        target.Draw(skin, StackSprites.PlShadeLeft, 0, 0);
        TileAcross(skin, target, StackSprites.PlShadeTile, StackSprites.PlShadeLeft.Width, Width - right.Width, 0);
        target.Draw(skin, right, Width - right.Width, 0);

        var timeWidth = PixelFont.CellWidth * view.ShadeTime.Length;
        var timeX = Width - PlaylistLayout.ShadeTimeRight - timeWidth;
        Text(skin, target, view.ShadeTime, timeX, 4, timeWidth);
        Text(skin, target, view.ShadeTitle, 5, 4, Math.Max(timeX - 10, 0));

        if (view.Pressed == PlaylistControl.Close)
        {
            target.Draw(skin, StackSprites.PlClosePressed, PlaylistLayout.Close.X, PlaylistLayout.Close.Y);
        }
        else if (view.Pressed == PlaylistControl.Shade)
        {
            target.Draw(skin, StackSprites.PlUnshadePressed, PlaylistLayout.Shade.X, PlaylistLayout.Shade.Y);
        }
    }

    /// <summary>Text in the skin's font, cut at <paramref name="width"/> pixels; nothing for empty text.</summary>
    private static void Text(Skin skin, SkinImage target, string text, int x, int y, int width)
    {
        if (!string.IsNullOrEmpty(text) && width > 0)
        {
            PixelFont.Draw(skin, target, text, x, y, width);
        }
    }

    /// <summary>A sprite repeated from <paramref name="from"/> up to (not including) <paramref name="to"/>, the last copy cut short.</summary>
    private static void TileAcross(Skin skin, SkinImage target, Sprite tile, int from, int to, int y)
    {
        for (var x = from; x < to; x += tile.Width)
        {
            Draw(skin, target, tile, x, y, Math.Min(tile.Width, to - x), tile.Height);
        }
    }

    /// <summary>The top left <paramref name="width"/> x <paramref name="height"/> of a sprite.</summary>
    private static void Draw(Skin skin, SkinImage target, Sprite sprite, int x, int y, int width, int height) =>
        target.Draw(skin.Sheet(sprite.Sheet), sprite.X, sprite.Y, Math.Min(width, sprite.Width), Math.Min(height, sprite.Height), x, y);
}

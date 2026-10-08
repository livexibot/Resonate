namespace Resonate.Themes.Skins;

/// <summary>A picture cut out of a skin sheet: which sheet, and the block of it.</summary>
internal readonly record struct Sprite(SkinSheet Sheet, int X, int Y, int Width, int Height);

/// <summary>The four looks of a toggle button: off or on, each also pressed.</summary>
internal readonly record struct ToggleSprites(Sprite Off, Sprite OffPressed, Sprite On, Sprite OnPressed)
{
    public Sprite For(bool on, bool pressed) => on ? (pressed ? OnPressed : On) : (pressed ? OffPressed : Off);
}

/// <summary>
/// Where every picture of a classic skin's main window sits in its sheet, as
/// Winamp 2 cut them (webamp's measurements, checked against Audacious).
/// Where each is drawn lives in <see cref="ClassicLayout"/>.
/// </summary>
internal static class SkinSprites
{
    // main.bmp
    public static readonly Sprite Background = new(SkinSheet.Main, 0, 0, 275, 116);

    // titlebar.bmp: the bars
    public static readonly Sprite TitleBarActive = new(SkinSheet.TitleBar, 27, 0, 275, 14);
    public static readonly Sprite TitleBarInactive = new(SkinSheet.TitleBar, 27, 15, 275, 14);
    public static readonly Sprite EasterEggBarActive = new(SkinSheet.TitleBar, 27, 57, 275, 14);
    public static readonly Sprite EasterEggBarInactive = new(SkinSheet.TitleBar, 27, 72, 275, 14);
    public static readonly Sprite ShadeBarActive = new(SkinSheet.TitleBar, 27, 29, 275, 14);
    public static readonly Sprite ShadeBarInactive = new(SkinSheet.TitleBar, 27, 42, 275, 14);

    // titlebar.bmp: the title buttons
    public static readonly Sprite OptionsButton = new(SkinSheet.TitleBar, 0, 0, 9, 9);
    public static readonly Sprite OptionsPressed = new(SkinSheet.TitleBar, 0, 9, 9, 9);
    public static readonly Sprite MinimizeButton = new(SkinSheet.TitleBar, 9, 0, 9, 9);
    public static readonly Sprite MinimizePressed = new(SkinSheet.TitleBar, 9, 9, 9, 9);
    public static readonly Sprite ShadeButton = new(SkinSheet.TitleBar, 0, 18, 9, 9);
    public static readonly Sprite ShadePressed = new(SkinSheet.TitleBar, 9, 18, 9, 9);
    public static readonly Sprite CloseButton = new(SkinSheet.TitleBar, 18, 0, 9, 9);
    public static readonly Sprite ClosePressed = new(SkinSheet.TitleBar, 18, 9, 9, 9);
    public static readonly Sprite UnshadeButton = new(SkinSheet.TitleBar, 0, 27, 9, 9);
    public static readonly Sprite UnshadePressed = new(SkinSheet.TitleBar, 9, 27, 9, 9);

    // titlebar.bmp: the clutter bar and its lit letters (O A I D V)
    public static readonly Sprite ClutterBar = new(SkinSheet.TitleBar, 304, 0, 8, 43);
    public static readonly Sprite ClutterBarDisabled = new(SkinSheet.TitleBar, 312, 0, 8, 43);
    public static readonly Sprite ClutterOptionsLit = new(SkinSheet.TitleBar, 304, 47, 8, 8);
    public static readonly Sprite ClutterAlwaysOnTopLit = new(SkinSheet.TitleBar, 312, 55, 8, 7);
    public static readonly Sprite ClutterInfoLit = new(SkinSheet.TitleBar, 320, 62, 8, 7);
    public static readonly Sprite ClutterDoubleSizeLit = new(SkinSheet.TitleBar, 328, 69, 8, 8);
    public static readonly Sprite ClutterVisualiserLit = new(SkinSheet.TitleBar, 336, 77, 8, 7);

    // titlebar.bmp: the shade bar's seek bar; the thumb's look follows its place
    public static readonly Sprite ShadeSeekTrack = new(SkinSheet.TitleBar, 0, 36, 17, 7);
    public static readonly Sprite ShadeSeekThumbLeft = new(SkinSheet.TitleBar, 17, 36, 3, 7);
    public static readonly Sprite ShadeSeekThumbMiddle = new(SkinSheet.TitleBar, 20, 36, 3, 7);
    public static readonly Sprite ShadeSeekThumbRight = new(SkinSheet.TitleBar, 23, 36, 3, 7);

    // cbuttons.bmp
    public static readonly Sprite Previous = new(SkinSheet.CButtons, 0, 0, 23, 18);
    public static readonly Sprite PreviousPressed = new(SkinSheet.CButtons, 0, 18, 23, 18);
    public static readonly Sprite Play = new(SkinSheet.CButtons, 23, 0, 23, 18);
    public static readonly Sprite PlayPressed = new(SkinSheet.CButtons, 23, 18, 23, 18);
    public static readonly Sprite Pause = new(SkinSheet.CButtons, 46, 0, 23, 18);
    public static readonly Sprite PausePressed = new(SkinSheet.CButtons, 46, 18, 23, 18);
    public static readonly Sprite Stop = new(SkinSheet.CButtons, 69, 0, 23, 18);
    public static readonly Sprite StopPressed = new(SkinSheet.CButtons, 69, 18, 23, 18);
    public static readonly Sprite Next = new(SkinSheet.CButtons, 92, 0, 22, 18);
    public static readonly Sprite NextPressed = new(SkinSheet.CButtons, 92, 18, 22, 18);
    public static readonly Sprite Eject = new(SkinSheet.CButtons, 114, 0, 22, 16);
    public static readonly Sprite EjectPressed = new(SkinSheet.CButtons, 114, 16, 22, 16);

    // playpaus.bmp: the lamp, and the work indicator (only its first three columns show)
    public static readonly Sprite PlayingLamp = new(SkinSheet.PlayPaus, 0, 0, 9, 9);
    public static readonly Sprite PausedLamp = new(SkinSheet.PlayPaus, 9, 0, 9, 9);
    public static readonly Sprite StoppedLamp = new(SkinSheet.PlayPaus, 18, 0, 9, 9);
    public static readonly Sprite NotWorking = new(SkinSheet.PlayPaus, 36, 0, 3, 9);
    public static readonly Sprite Working = new(SkinSheet.PlayPaus, 39, 0, 3, 9);

    // monoster.bmp
    public static readonly Sprite StereoLit = new(SkinSheet.MonoSter, 0, 0, 29, 12);
    public static readonly Sprite StereoUnlit = new(SkinSheet.MonoSter, 0, 12, 29, 12);
    public static readonly Sprite MonoLit = new(SkinSheet.MonoSter, 29, 0, 27, 12);
    public static readonly Sprite MonoUnlit = new(SkinSheet.MonoSter, 29, 12, 27, 12);

    // numbers.bmp and nums_ex.bmp: digit cells 0 to 9, then the blank (10) and, in nums_ex, the minus (11)
    public const int DigitWidth = 9;
    public const int DigitHeight = 13;
    public const int BlankCell = 10;
    public const int MinusCell = 11;

    /// <summary>numbers.bmp has no minus cell: Winamp borrowed the middle bar of its 2.</summary>
    public static readonly Sprite NumbersMinus = new(SkinSheet.Numbers, 20, 6, 5, 1);

    /// <summary>The same row of numbers.bmp's 1, normally empty, for "no minus".</summary>
    public static readonly Sprite NumbersNoMinus = new(SkinSheet.Numbers, 9, 6, 5, 1);

    // posbar.bmp
    public static readonly Sprite SeekTrack = new(SkinSheet.PosBar, 0, 0, 248, 10);
    public static readonly Sprite SeekThumb = new(SkinSheet.PosBar, 248, 0, 29, 10);
    public static readonly Sprite SeekThumbPressed = new(SkinSheet.PosBar, 278, 0, 29, 10);

    // volume.bmp and balance.bmp: 28 frames, then the thumbs (the pressed one is on the left)
    public const int SliderFrames = 28;
    public static readonly Sprite VolumeThumb = new(SkinSheet.Volume, 15, 422, 14, 11);
    public static readonly Sprite VolumeThumbPressed = new(SkinSheet.Volume, 0, 422, 14, 11);
    public static readonly Sprite BalanceThumb = new(SkinSheet.Balance, 15, 422, 14, 11);
    public static readonly Sprite BalanceThumbPressed = new(SkinSheet.Balance, 0, 422, 14, 11);

    // shufrep.bmp
    public static readonly Sprite ShuffleOff = new(SkinSheet.ShufRep, 28, 0, 47, 15);
    public static readonly Sprite ShuffleOffPressed = new(SkinSheet.ShufRep, 28, 15, 47, 15);
    public static readonly Sprite ShuffleOn = new(SkinSheet.ShufRep, 28, 30, 47, 15);
    public static readonly Sprite ShuffleOnPressed = new(SkinSheet.ShufRep, 28, 45, 47, 15);
    public static readonly Sprite RepeatOff = new(SkinSheet.ShufRep, 0, 0, 28, 15);
    public static readonly Sprite RepeatOffPressed = new(SkinSheet.ShufRep, 0, 15, 28, 15);
    public static readonly Sprite RepeatOn = new(SkinSheet.ShufRep, 0, 30, 28, 15);
    public static readonly Sprite RepeatOnPressed = new(SkinSheet.ShufRep, 0, 45, 28, 15);
    public static readonly Sprite EqualizerOff = new(SkinSheet.ShufRep, 0, 61, 23, 12);
    public static readonly Sprite EqualizerOffPressed = new(SkinSheet.ShufRep, 46, 61, 23, 12);
    public static readonly Sprite EqualizerOn = new(SkinSheet.ShufRep, 0, 73, 23, 12);
    public static readonly Sprite EqualizerOnPressed = new(SkinSheet.ShufRep, 46, 73, 23, 12);
    public static readonly Sprite PlaylistOff = new(SkinSheet.ShufRep, 23, 61, 23, 12);
    public static readonly Sprite PlaylistOffPressed = new(SkinSheet.ShufRep, 69, 61, 23, 12);
    public static readonly Sprite PlaylistOn = new(SkinSheet.ShufRep, 23, 73, 23, 12);
    public static readonly Sprite PlaylistOnPressed = new(SkinSheet.ShufRep, 69, 73, 23, 12);

    public static readonly ToggleSprites ShuffleToggle = new(ShuffleOff, ShuffleOffPressed, ShuffleOn, ShuffleOnPressed);
    public static readonly ToggleSprites RepeatToggle = new(RepeatOff, RepeatOffPressed, RepeatOn, RepeatOnPressed);
    public static readonly ToggleSprites EqualizerToggle = new(EqualizerOff, EqualizerOffPressed, EqualizerOn, EqualizerOnPressed);
    public static readonly ToggleSprites PlaylistToggle = new(PlaylistOff, PlaylistOffPressed, PlaylistOn, PlaylistOnPressed);

    /// <summary>Every sprite above, every digit, frame and glyph cell included, for checks against the sheets.</summary>
    public static IReadOnlyList<Sprite> All { get; } =
    [
        Background,
        TitleBarActive, TitleBarInactive, EasterEggBarActive, EasterEggBarInactive, ShadeBarActive, ShadeBarInactive,
        OptionsButton, OptionsPressed, MinimizeButton, MinimizePressed, ShadeButton, ShadePressed,
        CloseButton, ClosePressed, UnshadeButton, UnshadePressed,
        ClutterBar, ClutterBarDisabled, ClutterOptionsLit, ClutterAlwaysOnTopLit, ClutterInfoLit, ClutterDoubleSizeLit, ClutterVisualiserLit,
        ShadeSeekTrack, ShadeSeekThumbLeft, ShadeSeekThumbMiddle, ShadeSeekThumbRight,
        Previous, PreviousPressed, Play, PlayPressed, Pause, PausePressed, Stop, StopPressed, Next, NextPressed, Eject, EjectPressed,
        PlayingLamp, PausedLamp, StoppedLamp, NotWorking, Working,
        StereoLit, StereoUnlit, MonoLit, MonoUnlit,
        NumbersMinus, NumbersNoMinus,
        .. Enumerable.Range(0, BlankCell + 1).Select(cell => DigitCell(SkinSheet.Numbers, cell)),
        .. Enumerable.Range(0, MinusCell + 1).Select(cell => DigitCell(SkinSheet.NumsEx, cell)),
        SeekTrack, SeekThumb, SeekThumbPressed,
        .. Enumerable.Range(0, SliderFrames).Select(VolumeFrame),
        .. Enumerable.Range(0, SliderFrames).Select(BalanceFrame),
        VolumeThumb, VolumeThumbPressed, BalanceThumb, BalanceThumbPressed,
        ShuffleOff, ShuffleOffPressed, ShuffleOn, ShuffleOnPressed, RepeatOff, RepeatOffPressed, RepeatOn, RepeatOnPressed,
        EqualizerOff, EqualizerOffPressed, EqualizerOn, EqualizerOnPressed, PlaylistOff, PlaylistOffPressed, PlaylistOn, PlaylistOnPressed,
        .. Enumerable.Range(0, 3 * 31).Select(cell => Glyph(cell / 31, cell % 31)),
    ];

    /// <summary>Digit or sign cell <paramref name="cell"/> (0-9, <see cref="BlankCell"/>, <see cref="MinusCell"/>) of a numbers sheet.</summary>
    public static Sprite DigitCell(SkinSheet sheet, int cell) => new(sheet, DigitWidth * cell, 0, DigitWidth, DigitHeight);

    /// <summary>Volume frame 0 (silent) to 27 (full).</summary>
    public static Sprite VolumeFrame(int frame) => new(SkinSheet.Volume, 0, 15 * frame, 68, 13);

    /// <summary>Balance frame 0 (centred) to 27 (all the way to one side); the sheet's first nine columns are not part of it.</summary>
    public static Sprite BalanceFrame(int frame) => new(SkinSheet.Balance, 9, 15 * frame, 38, 13);

    /// <summary>The text.bmp glyph at a row and column of its 5 x 6 cells.</summary>
    public static Sprite Glyph(int row, int column) =>
        new(SkinSheet.Text, PixelFont.CellWidth * column, PixelFont.CellHeight * row, PixelFont.CellWidth, PixelFont.CellHeight);

    /// <summary>Draws a sprite with its top left at (<paramref name="x"/>, <paramref name="y"/>); parts a small sheet lacks are left alone.</summary>
    public static void Draw(this SkinImage target, Skin skin, Sprite sprite, int x, int y) =>
        target.Draw(skin.Sheet(sprite.Sheet), sprite.X, sprite.Y, sprite.Width, sprite.Height, x, y);
}

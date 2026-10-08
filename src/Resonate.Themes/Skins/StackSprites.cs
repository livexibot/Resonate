namespace Resonate.Themes.Skins;

/// <summary>
/// Where the pictures of a classic skin's equalizer and playlist windows sit
/// in their sheets, as Winamp 2 cut them (webamp's measurements). Where each
/// is drawn lives in <see cref="EqualizerLayout"/> and <see cref="PlaylistLayout"/>.
/// </summary>
internal static class StackSprites
{
    // eqmain.bmp: the window, its bars and buttons
    public static readonly Sprite EqBackground = new(SkinSheet.EqMain, 0, 0, 275, 116);
    public static readonly Sprite EqTitleBarActive = new(SkinSheet.EqMain, 0, 134, 275, 14);
    public static readonly Sprite EqTitleBarInactive = new(SkinSheet.EqMain, 0, 149, 275, 14);
    public static readonly Sprite EqClose = new(SkinSheet.EqMain, 0, 116, 9, 9);
    public static readonly Sprite EqClosePressed = new(SkinSheet.EqMain, 0, 125, 9, 9);
    public static readonly Sprite EqOnOff = new(SkinSheet.EqMain, 10, 119, 26, 12);
    public static readonly Sprite EqOnOffPressed = new(SkinSheet.EqMain, 128, 119, 26, 12);
    public static readonly Sprite EqOnOn = new(SkinSheet.EqMain, 69, 119, 26, 12);
    public static readonly Sprite EqOnOnPressed = new(SkinSheet.EqMain, 187, 119, 26, 12);
    public static readonly Sprite EqAutoOff = new(SkinSheet.EqMain, 36, 119, 32, 12);
    public static readonly Sprite EqAutoOffPressed = new(SkinSheet.EqMain, 154, 119, 32, 12);
    public static readonly Sprite EqAutoOn = new(SkinSheet.EqMain, 95, 119, 32, 12);
    public static readonly Sprite EqAutoOnPressed = new(SkinSheet.EqMain, 213, 119, 32, 12);
    public static readonly Sprite EqPresets = new(SkinSheet.EqMain, 224, 164, 44, 12);
    public static readonly Sprite EqPresetsPressed = new(SkinSheet.EqMain, 224, 176, 44, 12);
    public static readonly Sprite EqThumb = new(SkinSheet.EqMain, 0, 164, 11, 11);
    public static readonly Sprite EqThumbPressed = new(SkinSheet.EqMain, 0, 176, 11, 11);

    // eqmain.bmp: the response graph, the colour of each of its rows, and the preamp's line
    public static readonly Sprite EqGraph = new(SkinSheet.EqMain, 0, 294, 113, 19);
    public static readonly Sprite EqGraphColours = new(SkinSheet.EqMain, 115, 294, 1, 19);
    public static readonly Sprite EqPreampLine = new(SkinSheet.EqMain, 0, 314, 113, 1);

    // eq_ex.bmp: the rolled-up bar, its mini sliders' thumbs (three looks each) and its buttons
    public static readonly Sprite EqShadeBarActive = new(SkinSheet.EqEx, 0, 0, 275, 14);
    public static readonly Sprite EqShadeBarInactive = new(SkinSheet.EqEx, 0, 15, 275, 14);
    public static readonly Sprite EqShadeVolumeLow = new(SkinSheet.EqEx, 1, 30, 3, 7);
    public static readonly Sprite EqShadeVolumeMiddle = new(SkinSheet.EqEx, 4, 30, 3, 7);
    public static readonly Sprite EqShadeVolumeHigh = new(SkinSheet.EqEx, 7, 30, 3, 7);
    public static readonly Sprite EqShadeBalanceLeft = new(SkinSheet.EqEx, 11, 30, 3, 7);
    public static readonly Sprite EqShadeBalanceMiddle = new(SkinSheet.EqEx, 14, 30, 3, 7);
    public static readonly Sprite EqShadeBalanceRight = new(SkinSheet.EqEx, 17, 30, 3, 7);
    public static readonly Sprite EqShadePressed = new(SkinSheet.EqEx, 1, 38, 9, 9);
    public static readonly Sprite EqUnshadePressed = new(SkinSheet.EqEx, 1, 47, 9, 9);
    public static readonly Sprite EqShadeClose = new(SkinSheet.EqEx, 11, 38, 9, 9);
    public static readonly Sprite EqShadeClosePressed = new(SkinSheet.EqEx, 11, 47, 9, 9);

    // pledit.bmp: the frame, lit while the window has focus
    public static readonly Sprite PlTopLeftActive = new(SkinSheet.PlEdit, 0, 0, 25, 20);
    public static readonly Sprite PlTitleActive = new(SkinSheet.PlEdit, 26, 0, 100, 20);
    public static readonly Sprite PlTopTileActive = new(SkinSheet.PlEdit, 127, 0, 25, 20);
    public static readonly Sprite PlTopRightActive = new(SkinSheet.PlEdit, 153, 0, 25, 20);
    public static readonly Sprite PlTopLeft = new(SkinSheet.PlEdit, 0, 21, 25, 20);
    public static readonly Sprite PlTitle = new(SkinSheet.PlEdit, 26, 21, 100, 20);
    public static readonly Sprite PlTopTile = new(SkinSheet.PlEdit, 127, 21, 25, 20);
    public static readonly Sprite PlTopRight = new(SkinSheet.PlEdit, 153, 21, 25, 20);
    public static readonly Sprite PlLeftTile = new(SkinSheet.PlEdit, 0, 42, 12, 29);
    public static readonly Sprite PlRightTile = new(SkinSheet.PlEdit, 31, 42, 20, 29);
    public static readonly Sprite PlBottomTile = new(SkinSheet.PlEdit, 179, 0, 25, 38);
    public static readonly Sprite PlBottomLeft = new(SkinSheet.PlEdit, 0, 72, 125, 38);
    public static readonly Sprite PlBottomRight = new(SkinSheet.PlEdit, 126, 72, 150, 38);

    // pledit.bmp: the scroll handle, the pressed title buttons, and the rolled-up bar
    public static readonly Sprite PlScrollHandle = new(SkinSheet.PlEdit, 52, 53, 8, 18);
    public static readonly Sprite PlScrollHandlePressed = new(SkinSheet.PlEdit, 61, 53, 8, 18);
    public static readonly Sprite PlClosePressed = new(SkinSheet.PlEdit, 52, 42, 9, 9);
    public static readonly Sprite PlShadePressed = new(SkinSheet.PlEdit, 62, 42, 9, 9);
    public static readonly Sprite PlUnshadePressed = new(SkinSheet.PlEdit, 150, 42, 9, 9);
    public static readonly Sprite PlShadeLeft = new(SkinSheet.PlEdit, 72, 42, 25, 14);
    public static readonly Sprite PlShadeTile = new(SkinSheet.PlEdit, 72, 57, 25, 14);
    public static readonly Sprite PlShadeRight = new(SkinSheet.PlEdit, 99, 57, 50, 14);
    public static readonly Sprite PlShadeRightActive = new(SkinSheet.PlEdit, 99, 42, 50, 14);

    /// <summary>The equalizer sliders' 28 frames, 0 (all the way down) to 27: two rows of fourteen, 15 pixels apart.</summary>
    public const int EqSliderFrames = 28;

    public static readonly ToggleSprites EqOnToggle = new(EqOnOff, EqOnOffPressed, EqOnOn, EqOnOnPressed);
    public static readonly ToggleSprites EqAutoToggle = new(EqAutoOff, EqAutoOffPressed, EqAutoOn, EqAutoOnPressed);

    /// <summary>Every sprite above, every slider frame included, for checks against the sheets.</summary>
    public static IReadOnlyList<Sprite> All { get; } =
    [
        EqBackground, EqTitleBarActive, EqTitleBarInactive, EqClose, EqClosePressed,
        EqOnOff, EqOnOffPressed, EqOnOn, EqOnOnPressed, EqAutoOff, EqAutoOffPressed, EqAutoOn, EqAutoOnPressed,
        EqPresets, EqPresetsPressed, EqThumb, EqThumbPressed, EqGraph, EqGraphColours, EqPreampLine,
        EqShadeBarActive, EqShadeBarInactive, EqShadeVolumeLow, EqShadeVolumeMiddle, EqShadeVolumeHigh,
        EqShadeBalanceLeft, EqShadeBalanceMiddle, EqShadeBalanceRight, EqShadePressed, EqUnshadePressed,
        EqShadeClose, EqShadeClosePressed,
        .. Enumerable.Range(0, EqSliderFrames).Select(EqSliderFrame),
        PlTopLeftActive, PlTitleActive, PlTopTileActive, PlTopRightActive, PlTopLeft, PlTitle, PlTopTile, PlTopRight,
        PlLeftTile, PlRightTile, PlBottomTile, PlBottomLeft, PlBottomRight,
        PlScrollHandle, PlScrollHandlePressed, PlClosePressed, PlShadePressed, PlUnshadePressed,
        PlShadeLeft, PlShadeTile, PlShadeRight, PlShadeRightActive,
    ];

    /// <summary>An equalizer slider's track for <paramref name="frame"/> 0 (bottom) to 27 (top).</summary>
    public static Sprite EqSliderFrame(int frame)
    {
        frame = Math.Clamp(frame, 0, EqSliderFrames - 1);
        return new Sprite(SkinSheet.EqMain, 13 + ((frame % 14) * 15), 164 + ((frame / 14) * 65), 14, 63);
    }
}

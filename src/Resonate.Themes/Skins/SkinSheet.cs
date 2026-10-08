namespace Resonate.Themes.Skins;

/// <summary>The pictures of a classic skin: the main window's, then the equalizer's and the playlist's.</summary>
public enum SkinSheet
{
    Main,
    TitleBar,
    CButtons,
    PlayPaus,
    MonoSter,
    Numbers,
    NumsEx,
    Text,
    PosBar,
    Volume,
    Balance,
    ShufRep,

    /// <summary>The equalizer window (eqmain.bmp).</summary>
    EqMain,

    /// <summary>The equalizer rolled up (eq_ex.bmp, from Winamp 2.9 on; older skins borrow the built-in one).</summary>
    EqEx,

    /// <summary>The playlist window's frame and buttons (pledit.bmp).</summary>
    PlEdit,
}

public static class SkinSheets
{
    public static IReadOnlyList<SkinSheet> All { get; } = Enum.GetValues<SkinSheet>();

    /// <summary>The file name inside a .wsz archive (matched without regard to case or folders).</summary>
    public static string FileName(SkinSheet sheet) => sheet switch
    {
        SkinSheet.Main => "main.bmp",
        SkinSheet.TitleBar => "titlebar.bmp",
        SkinSheet.CButtons => "cbuttons.bmp",
        SkinSheet.PlayPaus => "playpaus.bmp",
        SkinSheet.MonoSter => "monoster.bmp",
        SkinSheet.Numbers => "numbers.bmp",
        SkinSheet.NumsEx => "nums_ex.bmp",
        SkinSheet.Text => "text.bmp",
        SkinSheet.PosBar => "posbar.bmp",
        SkinSheet.Volume => "volume.bmp",
        SkinSheet.Balance => "balance.bmp",
        SkinSheet.ShufRep => "shufrep.bmp",
        SkinSheet.EqMain => "eqmain.bmp",
        SkinSheet.EqEx => "eq_ex.bmp",
        SkinSheet.PlEdit => "pledit.bmp",
        _ => throw new ArgumentOutOfRangeException(nameof(sheet)),
    };

    /// <summary>The size Winamp 2 skins draw at; real skins are sometimes smaller or larger.</summary>
    public static (int Width, int Height) ExpectedSize(SkinSheet sheet) => sheet switch
    {
        SkinSheet.Main => (275, 116),
        SkinSheet.TitleBar => (344, 87),
        SkinSheet.CButtons => (136, 36),
        SkinSheet.PlayPaus => (42, 9),
        SkinSheet.MonoSter => (56, 24),
        SkinSheet.Numbers => (99, 13),
        SkinSheet.NumsEx => (108, 13),
        SkinSheet.Text => (155, 18),
        SkinSheet.PosBar => (307, 10),
        SkinSheet.Volume => (68, 433),
        SkinSheet.Balance => (47, 433),
        SkinSheet.ShufRep => (92, 85),
        SkinSheet.EqMain => (275, 315),
        SkinSheet.EqEx => (275, 56),
        SkinSheet.PlEdit => (280, 186),
        _ => throw new ArgumentOutOfRangeException(nameof(sheet)),
    };
}

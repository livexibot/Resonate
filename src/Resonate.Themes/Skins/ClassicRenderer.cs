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

    /// <summary>
    /// Draws the whole window into <paramref name="target"/>, which is
    /// <see cref="Width"/> wide and <see cref="Height"/> (or, shaded,
    /// <see cref="ShadeHeight"/>) tall. The visualiser shows
    /// <paramref name="frame"/>; without one it is drawn at rest.
    /// </summary>
    public static void Render(Skin skin, ClassicView view, SkinImage target, VisualiserFrame? frame = null) =>
        throw new NotImplementedException();

    /// <summary>
    /// Draws only the visualiser, into a picture the size of
    /// <see cref="ClassicLayout.VisualiserArea"/>; the control lays it over
    /// the window and redraws it every frame while a local file plays.
    /// </summary>
    public static void RenderVisualiser(Skin skin, VisualiserMode mode, VisualiserFrame? frame, bool shaded, SkinImage target) =>
        throw new NotImplementedException();
}

using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// The user's two size settings (Settings, Look, Size). App size grows or
/// shrinks everything in the window, like a browser's zoom; Text size makes
/// only the text larger or smaller. Both are whole percentages on fixed
/// steps, kept whatever look is in use (not part of a look).
/// </summary>
public static class AppScale
{
    /// <summary>The usual size of both settings.</summary>
    public const int Normal = 100;

    /// <summary>
    /// The steps App size offers, smallest first: a browser's zoom steps, with
    /// fives near the usual size (the owner asked for more, 9 October 2026).
    /// </summary>
    public static IReadOnlyList<int> AppSizes { get; } = [50, 60, 67, 75, 80, 85, 90, 95, 100, 105, 110, 115, 120, 125, 133, 150, 175, 200, 225, 250, 300];

    /// <summary>
    /// The steps Cover size offers (the covers in song lists and the
    /// sidebar), smallest first; the owner asked for it, 9 October 2026.
    /// </summary>
    public static IReadOnlyList<int> CoverSizes { get; } = [75, 100, 125, 150, 175, 200];

    /// <summary>A cover usually <paramref name="usual"/> pixels wide at <paramref name="percent"/> Cover size, on a step.</summary>
    public static int Cover(int usual, int percent) => (int)Math.Round(usual * Nearest(percent, CoverSizes) / 100.0);

    /// <summary>
    /// A row that is usually <paramref name="usualRow"/> tall around a cover
    /// usually <paramref name="usualCover"/> wide, holding a cover
    /// <paramref name="cover"/> wide: it keeps the same room above and below
    /// a larger cover, so covers never touch from row to row, and never gets
    /// shorter than usual (the text still needs it).
    /// </summary>
    public static double CoverRow(double usualRow, double usualCover, double cover) =>
        Math.Max(usualRow, cover + (usualRow - usualCover));

    /// <summary>The steps Text size offers, smallest first.</summary>
    public static IReadOnlyList<int> TextSizes { get; } = [75, 80, 85, 90, 95, 100, 105, 110, 115, 120, 125, 135, 150, 175, 200];

    /// <summary>
    /// Every text size the interface uses at the usual Text size, each a
    /// <c>ResonateFontSize{n}</c> token in Themes/Tokens.xaml. Icons are not
    /// text and keep their sizes.
    /// </summary>
    public static IReadOnlyList<int> FontSizes { get; } = [10, 11, 12, 13, 14, 15, 18, 20, 24, 26, 32, 44, 46, 48, 60];

    /// <summary>The theme resource that holds text of <paramref name="size"/> at the usual Text size.</summary>
    public static string FontKey(int size) => "ResonateFontSize" + size.ToString(CultureInfo.InvariantCulture);

    /// <summary><paramref name="size"/> at <paramref name="textPercent"/> Text size.</summary>
    public static double Font(double size, int textPercent) => size * textPercent / 100.0;

    /// <summary>
    /// The step nearest to <paramref name="percent"/> (the smaller one when
    /// two are as near), so a hand-edited or old setting still lands on a step.
    /// </summary>
    public static int Nearest(int percent, IReadOnlyList<int> steps)
    {
        var best = steps[0];
        foreach (var step in steps)
        {
            if (Math.Abs(step - percent) < Math.Abs(best - percent))
            {
                best = step;
            }
        }

        return best;
    }

    /// <summary>The next step up from <paramref name="percent"/>, or the largest.</summary>
    public static int Larger(int percent, IReadOnlyList<int> steps)
    {
        foreach (var step in steps)
        {
            if (step > percent)
            {
                return step;
            }
        }

        return steps[^1];
    }

    /// <summary>The next step down from <paramref name="percent"/>, or the smallest.</summary>
    public static int Smaller(int percent, IReadOnlyList<int> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i] < percent)
            {
                return steps[i];
            }
        }

        return steps[0];
    }

    /// <summary>
    /// The smallest window, in pixels, that still gives the page the room of a
    /// <paramref name="minimum"/> window at the usual App size, but never more
    /// than <paramref name="available"/> (the screen's work area).
    /// </summary>
    public static int MinimumWindow(int minimum, int appPercent, int available) =>
        Math.Max(1, Math.Min((int)Math.Round(minimum * appPercent / 100.0), available));

    /// <summary>A step as the Settings list shows it, "125%".</summary>
    public static string Label(int percent) => percent.ToString(CultureInfo.InvariantCulture) + "%";
}

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

    /// <summary>The steps App size offers, smallest first.</summary>
    public static IReadOnlyList<int> AppSizes { get; } = [80, 90, 100, 110, 125, 150, 175, 200];

    /// <summary>The steps Text size offers, smallest first.</summary>
    public static IReadOnlyList<int> TextSizes { get; } = [90, 100, 110, 125, 150];

    /// <summary>
    /// Every text size the interface uses at the usual Text size, each a
    /// <c>ResonateFontSize{n}</c> token in Themes/Tokens.xaml. Icons are not
    /// text and keep their sizes.
    /// </summary>
    public static IReadOnlyList<int> FontSizes { get; } = [10, 12, 13, 14, 15, 18, 20, 24, 26, 32, 44, 46, 48, 60];

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

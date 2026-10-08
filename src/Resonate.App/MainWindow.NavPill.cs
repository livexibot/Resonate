using System.Numerics;
using Microsoft.UI.Xaml;

namespace Resonate.App;

/// <summary>
/// The pill behind the sidebar's chosen link (Home, Search, Liked Songs...):
/// it glides to the next link chosen, and fades out while a playlist or
/// Settings is open. One short compositor animation, nothing while idle.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan NavGlide = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan NavFade = TimeSpan.FromMilliseconds(150);

    // The pill is inset like the rows' own hover background (Margin in MainWindow.xaml).
    private const double NavPillInset = 2;

    private void MoveNavPill(bool glide)
    {
        var animate = _services.Theme.AnimationsEnabled;
        NavPill.OpacityTransition = animate ? new ScalarTransition { Duration = NavFade } : null;

        // Every link is a row of the same height, so the list's height gives
        // a row's without reaching for the rows themselves (which Native AOT
        // cannot cast reliably).
        var index = NavList.SelectedIndex;
        var count = NavItems.Count;
        var height = NavList.ActualHeight;
        if (index < 0 || count == 0 || height <= 0)
        {
            NavPill.Opacity = 0;
            return;
        }

        var row = height / count;
        NavPill.Height = Math.Max(row - (2 * NavPillInset), 0);

        // It glides only from where it shows; coming back, it fades in at its place.
        NavPill.TranslationTransition = glide && animate && NavPill.Opacity > 0
            ? new Vector3Transition { Duration = NavGlide }
            : null;
        NavPill.Translation = new Vector3(0, (float)(index * row), 0);
        NavPill.Opacity = 1;
    }

    /// <summary>Links were added or removed (Local Files), or the list was first laid out: the pill moves to its row at once.</summary>
    private void OnNavListSizeChanged(object sender, SizeChangedEventArgs e) => MoveNavPill(glide: false);
}

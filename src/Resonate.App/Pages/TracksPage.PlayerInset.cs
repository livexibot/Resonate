using Microsoft.UI.Xaml;

namespace Resonate.App.Pages;

/// <summary>Leaves room under the last song for a player hovering over the page (see <see cref="IPlayerInset"/>).</summary>
public sealed partial class TracksPage : IPlayerInset
{
    FrameworkElement IPlayerInset.PlayerSpacer => PlayerSpace;

    public void SetPlayerInset(double height) => PlayerInset.Apply(PlayerSpace, height);

    /// <summary>For the screenshot tour: scrolls to the end of the list, where the room for the player is.</summary>
    internal void ScrollToEndForTour()
    {
        if (TrackList.Items.Count > 0)
        {
            TrackList.ScrollIntoView(TrackList.Items[^1]);
        }

        PlayerSpace.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 1 });
    }
}

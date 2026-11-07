using Microsoft.UI.Xaml;

namespace Resonate.App.Pages;

/// <summary>Leaves room under the calendar for a player hovering over the page (see <see cref="IPlayerInset"/>).</summary>
public sealed partial class CalendarPage : IPlayerInset
{
    FrameworkElement IPlayerInset.PlayerSpacer => PlayerSpace;

    public void SetPlayerInset(double height) => PlayerInset.Apply(PlayerSpace, height);
}

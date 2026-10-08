using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Resonate.App.Controls;

/// <summary>
/// A card that rises a little under the pointer: it grows by 3 % and lifts
/// by 4 pixels, and any <see cref="HoverReveal"/> among its children (a play
/// button) fades in. Each is a short implicit animation run by the
/// compositor, so this thread does nothing per frame and nothing runs once
/// the card settles. With Windows' animations off the card stays still and
/// the reveal appears at once.
/// </summary>
public sealed partial class HoverLift : Grid
{
    private const float Grow = 1.03f;
    private const float Rise = -4;

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);

    private bool _over;

    public HoverLift()
    {
        // The whole card, gaps included, notices the pointer.
        Background = App.Services.Theme.GetBrush("ResonateTransparentBrush");
        PointerEntered += (_, _) => SetOver(true);
        PointerExited += (_, _) => SetOver(false);
        PointerCanceled += (_, _) => SetOver(false);
        PointerCaptureLost += (_, _) => SetOver(false);
        SizeChanged += (_, e) => CenterPoint = new Vector3((float)(e.NewSize.Width / 2), (float)(e.NewSize.Height / 2), 0);
    }

    private void SetOver(bool over)
    {
        if (over == _over)
        {
            return;
        }

        _over = over;
        var animate = App.Services.Theme.AnimationsEnabled;
        if (animate)
        {
            ScaleTransition ??= new Vector3Transition { Duration = Duration };
            TranslationTransition ??= new Vector3Transition { Duration = Duration };
            Scale = over ? new Vector3(Grow, Grow, 1) : Vector3.One;
            Translation = over ? new Vector3(0, Rise, 0) : Vector3.Zero;
        }
        else if (Scale != Vector3.One)
        {
            // Animations were turned off while the card was up.
            ScaleTransition = null;
            TranslationTransition = null;
            Scale = Vector3.One;
            Translation = Vector3.Zero;
        }

        // Only the card's own children: elements of this app's own type, so
        // the check holds in the published app too.
        foreach (var child in Children)
        {
            if (child is HoverReveal reveal)
            {
                reveal.Show(over, animate);
            }
        }
    }
}

/// <summary>Part of a <see cref="HoverLift"/> card shown only while the pointer is over the card, such as a play button.</summary>
public sealed partial class HoverReveal : Grid
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);

    public HoverReveal()
    {
        Opacity = 0;

        // A button inside also shows when the keyboard reaches it.
        GotFocus += (_, e) =>
        {
            if (e.OriginalSource is Control { FocusState: FocusState.Keyboard })
            {
                Show(true, App.Services.Theme.AnimationsEnabled);
            }
        };
        LostFocus += (_, _) => Show(false, App.Services.Theme.AnimationsEnabled);
    }

    internal void Show(bool shown, bool animate)
    {
        OpacityTransition = animate ? OpacityTransition ?? new ScalarTransition { Duration = Duration } : null;
        Opacity = shown ? 1 : 0;
    }
}

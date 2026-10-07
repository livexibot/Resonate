using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Resonate.App.Controls;

/// <summary>
/// Turns a now-playing cover like a record: one turn every seven seconds on
/// the compositor, paused where it is (no jump) when the music or the window
/// stops, and back upright when spinning is switched off. Used by the player
/// bar and the classic player.
/// </summary>
internal sealed class CoverSpin
{
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(7);

    private readonly UIElement _cover;
    private AnimationController? _spin;
    private float _size;

    public CoverSpin(UIElement cover) => _cover = cover;

    /// <summary>
    /// <paramref name="enabled"/>: the cover may spin at all (the user's
    /// switch and Windows' animation setting). <paramref name="moving"/>: it
    /// turns now (a song plays and the window is shown). <paramref name="size"/>:
    /// the cover's width and height, to turn around its centre.
    /// </summary>
    public void Update(bool enabled, bool moving, float size)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_cover);
        if (!enabled)
        {
            if (_spin is not null)
            {
                _spin = null;
                visual.StopAnimation("RotationAngleInDegrees");
                visual.RotationAngleInDegrees = 0;
            }

            return;
        }

        if (_spin is null || Math.Abs(_size - size) > 0.5f)
        {
            _size = size;
            visual.CenterPoint = new Vector3(size / 2, size / 2, 0);
        }

        if (_spin is null)
        {
            var spin = visual.Compositor.CreateScalarKeyFrameAnimation();
            spin.InsertKeyFrame(0, 0);
            spin.InsertKeyFrame(1, 360, visual.Compositor.CreateLinearEasingFunction());
            spin.Duration = Turn;
            spin.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngleInDegrees", spin);
            _spin = visual.TryGetAnimationController("RotationAngleInDegrees");
        }

        if (moving)
        {
            _spin?.Resume();
        }
        else
        {
            _spin?.Pause();
        }
    }
}

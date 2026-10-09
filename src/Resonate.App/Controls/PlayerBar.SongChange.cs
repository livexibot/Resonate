using System.Numerics;
using Resonate.Spotify.Playback;

namespace Resonate.App.Controls;

/// <summary>
/// How a new song arrives in the player bar when it was not swiped in
/// (Settings, Themes, Effects, Song change): it slides in, pops in, fades in,
/// or simply shows. One short composition animation on the song's own visual.
/// </summary>
public sealed partial class PlayerBar
{
    private static readonly TimeSpan SongChangeTime = TimeSpan.FromMilliseconds(320);

    private PlayerState? _songShown;

    /// <summary>Called with every state: animates only when another song shows and no swipe is moving it.</summary>
    private void AnimateSongChange(PlayerState state)
    {
        var before = _songShown;
        _songShown = state;
        if (before is null || state.Title is null || IsSameSong(state, before) || _songMotion != SongMotion.Still || !MayAnimate)
        {
            return;
        }

        var kind = App.Services.Settings.SongChangeAnimation;
        if (kind == "None")
        {
            return;
        }

        var visual = SongVisual;
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = SongChangeTime;
        visual.StartAnimation("Opacity", fade);

        if (kind == "Slide")
        {
            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0, new Vector3(28, 0, 0));
            move.InsertKeyFrame(1, Vector3.Zero, easing);
            move.Duration = SongChangeTime;
            visual.StartAnimation("Translation", move);
        }
        else if (kind == "Pop")
        {
            visual.CenterPoint = new Vector3(0, (float)NowPlaying.ActualHeight / 2, 0);
            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, new Vector3(0.9f, 0.9f, 1));
            grow.InsertKeyFrame(1, Vector3.One, easing);
            grow.Duration = SongChangeTime;
            visual.StartAnimation("Scale", grow);
        }
    }
}

using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Resonate.Spotify.Playback;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// Swiping the playing song (its cover and name) to skip, as on Spotify's
/// phone app, with a mouse, a finger or a pen: to the left for the next
/// song, to the right for the previous one. The song follows the pointer and
/// fades; let go far enough, or flick it, and it slides out, then the new
/// song slides in from the other side as soon as it shows (or after a
/// moment, when Previous starts the same song again). A press that barely
/// moves stays a click, so the title still opens what plays. The song moves
/// on the compositor, one short animation at a time, and is clipped to its
/// column so it never slides over the controls; nothing runs once it
/// settles. With Windows' animations off it still follows the pointer, and
/// lets go without sliding.
/// </summary>
public sealed partial class PlayerBar
{
    private static readonly TimeSpan SpringBack = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan SlideOut = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan SlideIn = TimeSpan.FromMilliseconds(260);

    // The new song is reported locally or through the Web API; after this
    // long the song area comes back whatever it shows.
    private static readonly TimeSpan NewSongWait = TimeSpan.FromMilliseconds(600);

    // Previous starts the playing song again once it has played this long, so
    // there is no new song to wait for.
    private static readonly TimeSpan RestartsAfter = TimeSpan.FromSeconds(3);

    private readonly SwipeSpeed _swipeSpeed = new();
    private Visual? _songVisual;
    private InsetClip? _songClip;
    private DispatcherQueueTimer? _songTimer;
    private SongMotion _songMotion;
    private uint? _swipePointer;
    private Point _swipeStart;
    private double _swipeOffset;
    // The last press became a swipe, so it is no click on the title.
    private bool _swipeMoved;
    private int _slideDirection;
    private PlayerState? _slideFrom;
    private bool _awaitNewSong;

    private enum SongMotion
    {
        Still,
        Following,
        SpringingBack,
        SlidingOut,
        Waiting,
        SlidingIn,
    }

    private static bool MayAnimate => App.Services.Theme.AnimationsEnabled || App.Services.Theme.AnimateRegardless;

    private Visual SongVisual
    {
        get
        {
            if (_songVisual is null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(NowPlaying, true);
                _songVisual = ElementCompositionPreview.GetElementVisual(NowPlaying);

                // The column's own visual clips, so the moving song stays inside it.
                var area = ElementCompositionPreview.GetElementVisual(NowPlayingArea);
                _songClip = area.Compositor.CreateInsetClip();
            }

            return _songVisual;
        }
    }

    private void OnSongPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_songMotion == SongMotion.Following)
        {
            // A second finger: the first one swipes.
            return;
        }

        _swipeMoved = false;
        var point = e.GetCurrentPoint(NowPlayingArea);
        if (!_shown.HasTrack || (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed))
        {
            return;
        }

        // Not handled: until it moves, the press may be a click on the title.
        _swipePointer = e.Pointer.PointerId;
        _swipeStart = point.Position;
        _swipeSpeed.Reset(point.Position.X, point.Timestamp);
    }

    private void OnSongPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_swipePointer != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(NowPlayingArea);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            // The button was let go somewhere this column did not hear.
            _swipePointer = null;
            return;
        }

        var dx = point.Position.X - _swipeStart.X;
        var dy = point.Position.Y - _swipeStart.Y;
        if (_songMotion != SongMotion.Following)
        {
            if (SongSwipe.IsVertical(dx, dy))
            {
                _swipePointer = null;
                return;
            }

            if (!SongSwipe.Starts(dx, dy) || !NowPlayingArea.CapturePointer(e.Pointer))
            {
                return;
            }

            _swipeMoved = true;
            BeginFollowing();
        }

        _swipeSpeed.Add(point.Position.X, point.Timestamp);
        ShowSwipe(dx);
        e.Handled = true;
    }

    private void OnSongPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_swipePointer != e.Pointer.PointerId)
        {
            return;
        }

        _swipePointer = null;
        if (_songMotion != SongMotion.Following)
        {
            return;
        }

        var point = e.GetCurrentPoint(NowPlayingArea);
        _swipeSpeed.Add(point.Position.X, point.Timestamp);
        e.Handled = true;

        // Decided before letting go, so the capture-lost event that follows has nothing to undo.
        LetGo(point.Position.X - _swipeStart.X, _swipeSpeed.At(point.Timestamp));
        NowPlayingArea.ReleasePointerCapture(e.Pointer);
    }

    private void OnSongPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_swipePointer != e.Pointer.PointerId)
        {
            return;
        }

        _swipePointer = null;
        if (_songMotion == SongMotion.Following)
        {
            LetGo(0, speed: 0);
        }
    }

    private void BeginFollowing()
    {
        var visual = SongVisual;
        _songTimer?.Stop();
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        _awaitNewSong = false;
        _songMotion = SongMotion.Following;
        ClipSong(true);
    }

    private void ShowSwipe(double dx)
    {
        var width = NowPlayingArea.ActualWidth;
        _swipeOffset = SongSwipe.Offset(dx, width);
        var visual = SongVisual;
        visual.Properties.InsertVector3("Translation", new Vector3((float)_swipeOffset, 0, 0));
        visual.Opacity = (float)SongSwipe.Opacity(_swipeOffset, width);
    }

    private void LetGo(double dx, double speed)
    {
        var outcome = _player is null ? SwipeOutcome.None : SongSwipe.Decide(dx, speed, NowPlayingArea.ActualWidth);
        if (outcome == SwipeOutcome.None)
        {
            Animate(SongMotion.SpringingBack, SpringBack, 0, 1);
            return;
        }

        var next = outcome == SwipeOutcome.Next;
        _slideDirection = next ? -1 : 1;
        _slideFrom = _shown;
        _awaitNewSong = next || _shown.PositionAt(DateTimeOffset.UtcNow) < RestartsAfter;
        _ = next ? _player!.NextAsync() : _player!.PreviousAsync();

        // On its way out, to the side it was swiped.
        var away = _slideDirection * Math.Max(NowPlayingArea.ActualWidth / 2, Math.Abs(_swipeOffset) + 24);
        Animate(SongMotion.SlidingOut, SlideOut, away, 0);
    }

    /// <summary>The song shows a new state: once it has slid out, the new song slides in.</summary>
    private void ShowSongChange(PlayerState state)
    {
        if (_songMotion == SongMotion.Waiting && _slideFrom is { } from && !IsSameSong(state, from))
        {
            SlideSongIn();
        }
    }

    private void SlideSongIn()
    {
        // From a little way off on the other side, where the next song waits.
        var from = -_slideDirection * Math.Min(NowPlayingArea.ActualWidth / 4, 64);
        SongVisual.Properties.InsertVector3("Translation", new Vector3((float)from, 0, 0));
        _slideFrom = null;
        Animate(SongMotion.SlidingIn, SlideIn, 0, 1);
    }

    /// <summary>Moves the song to <paramref name="x"/> and <paramref name="opacity"/>; the timer says when it is there.</summary>
    private void Animate(SongMotion motion, TimeSpan duration, double x, double opacity)
    {
        var visual = SongVisual;
        if (!MayAnimate)
        {
            // Nothing slides: the song is back at once, showing whatever plays.
            Settle();
            return;
        }

        var compositor = visual.Compositor;
        var easing = motion == SongMotion.SlidingOut
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(1, 1))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));

        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1, new Vector3((float)x, 0, 0), easing);
        move.Duration = duration;
        visual.StartAnimation("Translation", move);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, (float)opacity, easing);
        fade.Duration = duration;
        visual.StartAnimation("Opacity", fade);

        _songMotion = motion;
        StartSongTimer(duration);
    }

    private void StartSongTimer(TimeSpan after)
    {
        if (_songTimer is null)
        {
            _songTimer = DispatcherQueue.CreateTimer();
            _songTimer.IsRepeating = false;
            _songTimer.Tick += (_, _) => OnSongTimer();
        }

        _songTimer.Stop();
        _songTimer.Interval = after;
        _songTimer.Start();
    }

    private void OnSongTimer()
    {
        switch (_songMotion)
        {
            case SongMotion.SlidingOut when _awaitNewSong && _slideFrom is { } from && IsSameSong(_shown, from):
                _songMotion = SongMotion.Waiting;
                StartSongTimer(NewSongWait - SlideOut);
                break;
            case SongMotion.SlidingOut:
            case SongMotion.Waiting:
                SlideSongIn();
                break;
            case SongMotion.SpringingBack:
            case SongMotion.SlidingIn:
                Settle();
                break;
        }
    }

    /// <summary>The song at rest: in place, fully shown, and no longer clipped (so its cover's shadow shows whole).</summary>
    private void Settle()
    {
        _songTimer?.Stop();
        var visual = SongVisual;
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
        _slideFrom = null;
        _awaitNewSong = false;
        _songMotion = SongMotion.Still;
        ClipSong(false);
    }

    private void ClipSong(bool clip)
    {
        var area = ElementCompositionPreview.GetElementVisual(NowPlayingArea);
        area.Clip = clip ? _songClip : null;
    }
}

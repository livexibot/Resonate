using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Resonate.App.Controls;
using Resonate.App.Services;

namespace Resonate.App.Pages;

/// <summary>
/// The Home stage, a built-in plugin (see <see cref="BuiltInPlugins"/>):
/// Home opens on the song that plays (<see cref="NowPlayingStage"/>), as tall
/// as the page with the top of the greeting peeking under it, and one scroll
/// brings up the rest. As the page scrolls, the cover and the words move
/// away at half speed while the cover shrinks towards the player bar and
/// everything fades; the compositor follows the scroll exactly, with no
/// work on this thread. While the stage shows, the greeting's card leaves
/// the song to it. Turning the plugin off removes the stage at once.
/// </summary>
public sealed partial class HomePage
{
    // Room left under the stage, so the top of the greeting shows there is more below.
    private const double StagePeek = 112;
    private const double StageMinHeight = 380;

    // How far the cover shrinks as the stage scrolls away (written into an expression, so as text).
    private const string StageCoverShrink = "0.35";

    // The stage's height at the last visit, so later visits lay it out before the first frame.
    private static double _lastStageHeight;

    private NowPlayingStage? _stage;
    private CompositionPropertySet? _stageScroll;
    private bool _stageWatching;

    partial void OnStageNavigatedTo()
    {
        _services.BuiltIns.Changed += OnStagePluginChanged;
        SetStage(_services.BuiltIns.IsOn(BuiltInPlugins.HomeStage));
    }

    partial void OnStageNavigatedFrom() => _services.BuiltIns.Changed -= OnStagePluginChanged;

    partial void OnStageNowPlayingShown()
    {
        if (_stage is not null)
        {
            _stage.SetLastPlayed(_lastPlay);
            NowPlaying.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStagePluginChanged(object? sender, string id)
    {
        if (id == BuiltInPlugins.HomeStage)
        {
            SetStage(_services.BuiltIns.IsOn(id));
        }
    }

    private void SetStage(bool on)
    {
        if (on == (_stage is not null))
        {
            return;
        }

        if (on)
        {
            _stage = new NowPlayingStage(_services, StageKind.Home);
            if (_lastStageHeight > 0)
            {
                _stage.Height = _lastStageHeight;
            }

            _stage.SetLastPlayed(_lastPlay);
            _stage.Cover.SizeChanged += OnStageCoverSizeChanged;
            StageSlot.Child = _stage;
            StageSlot.Visibility = Visibility.Visible;
            WatchStage();
            FitStage();
            FollowScroll(_stage);
        }
        else if (_stage is { } stage)
        {
            StageSlot.Child = null;
            StageSlot.Visibility = Visibility.Collapsed;
            stage.Cover.SizeChanged -= OnStageCoverSizeChanged;
            _stage = null;
            _stageScroll = null;
        }

        // The greeting's card shows the song again, or leaves it to the stage.
        _nowKey = null;
        ShowNowPlaying();
    }

    /// <summary>Follows the page's size, its scrolling and the hovering player's room (only once the stage was first shown).</summary>
    private void WatchStage()
    {
        if (_stageWatching)
        {
            return;
        }

        _stageWatching = true;
        Scroller.SizeChanged += (_, _) => FitStage();
        Scroller.ViewChanged += (_, _) => NoteStageOnScreen();
        PlayerSpace.RegisterPropertyChangedCallback(HeightProperty, (_, _) => FitStage());
        PlayerSpace.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => FitStage());
    }

    /// <summary>As tall as the page shows, less the peek and the hovering player's room.</summary>
    private void FitStage()
    {
        if (_stage is null || Scroller.ActualHeight <= 0)
        {
            return;
        }

        var inset = PlayerSpace.Visibility == Visibility.Visible && !double.IsNaN(PlayerSpace.Height) ? PlayerSpace.Height : 0;
        var height = Math.Max(StageMinHeight, Scroller.ActualHeight - Body.Padding.Top - StagePeek - inset);
        _stage.Height = _lastStageHeight = Math.Floor(height);
        _stageScroll?.InsertScalar("Height", (float)_stage.Height);
        NoteStageOnScreen();
    }

    private void NoteStageOnScreen() =>
        _stage?.SetOnScreen(Scroller.VerticalOffset < Body.Padding.Top + _stage.Height);

    private void OnStageCoverSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_stage is not null)
        {
            // Shrinks towards its bottom left corner, where the player bar is.
            ElementCompositionPreview.GetElementVisual(_stage.Cover).CenterPoint = new Vector3(0, (float)e.NewSize.Height, 0);
        }
    }

    /// <summary>The scroll moves, shrinks and fades the stage's cover and words, on the compositor.</summary>
    private void FollowScroll(NowPlayingStage stage)
    {
        var scroll = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(Scroller);
        var body = ElementCompositionPreview.GetElementVisual(stage.Body);
        var cover = ElementCompositionPreview.GetElementVisual(stage.Cover);
        var compositor = body.Compositor;
        _stageScroll = compositor.CreatePropertySet();
        _stageScroll.InsertScalar("Height", (float)Math.Max(1, double.IsNaN(stage.Height) ? StageMinHeight : stage.Height));

        // How far the page has scrolled into the stage, from 0 to its height.
        const string Into = "Clamp(-scroll.Translation.Y, 0, Max(stage.Height, 1))";

        ElementCompositionPreview.SetIsTranslationEnabled(stage.Body, true);
        Start(body, "Translation", $"Vector3(0, {Into} * 0.5, 0)");
        Start(body, "Opacity", $"1 - Clamp({Into} / Max(stage.Height * 0.8, 1), 0, 1)");
        var shrink = $"(1 - ({StageCoverShrink} * {Into} / Max(stage.Height, 1)))";
        Start(cover, "Scale", $"Vector3({shrink}, {shrink}, 1)");

        void Start(Visual visual, string property, string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("scroll", scroll);
            animation.SetReferenceParameter("stage", _stageScroll);
            visual.StartAnimation(property, animation);
        }
    }
}

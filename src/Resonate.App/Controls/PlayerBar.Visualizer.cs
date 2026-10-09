using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// A visualizer along the bottom of the player bar, when the look asks for
/// one (<see cref="ThemeDefinition.PlayerVisualizer"/>): the same bars as
/// Home's stage, faint, in the look's two accents, behind the controls. It
/// moves only while music plays and the window shows, and hears Spotify
/// the same way Home's does; with Off it is not there at all.
/// </summary>
public sealed partial class PlayerBar
{
    private const double BarVisualizerOpacity = 0.45;

    private StageVisualizer? _barVisualizer;

    private void UpdateBarVisualizer()
    {
        var theme = App.Services.Theme;
        var style = theme.Current.PlayerVisualizer;
        if (style == VisualizerStyle.Off || !VisualizerShapes.FitsPlayerBar(style))
        {
            if (_barVisualizer is { } old)
            {
                old.SetRunning(false, live: false, animate: false);
                Bar.Children.Remove(old);
                App.Services.Visualiser.LiveChanged -= OnBarVisualizerLive;
                _barVisualizer = null;
            }

            return;
        }

        if (_barVisualizer is null)
        {
            _barVisualizer = new StageVisualizer(App.Services.Visualiser)
            {
                InBar = true,
                Opacity = BarVisualizerOpacity,
                Margin = new Thickness(0, 0, 0, 2),
            };
            Grid.SetColumnSpan(_barVisualizer, 3);
            Bar.Children.Insert(0, _barVisualizer);
            _barVisualizer.SizeChanged += (_, e) => _barVisualizer?.SetRoom(e.NewSize.Height);
            App.Services.Visualiser.LiveChanged += OnBarVisualizerLive;
        }

        _barVisualizer.DrawStyle = style;
        var palette = theme.Palette;
        _barVisualizer.SetColours([palette.Accent, palette.Accent2], animate: false);
        RunBarVisualizer();
    }

    private void OnBarVisualizerLive(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RunBarVisualizer);

    /// <summary>Moves while a song plays and the window can be seen; rests otherwise.</summary>
    private void RunBarVisualizer()
    {
        if (_barVisualizer is { } visualizer)
        {
            var animate = App.Services.Theme.AnimationsEnabled;
            visualizer.SetRunning(_shown.IsPlaying && _windowShown && animate, App.Services.Visualiser.IsLive, animate);
        }
    }
}

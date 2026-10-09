using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App;

/// <summary>
/// Beat glow, a built-in plugin: the window's edges glow in the look's
/// accent with the beat of the music (<see cref="BeatGlow"/>), from the same
/// sound the Home visualizer hears. It follows the sound only while music
/// plays, the window shows and the sound is heard; otherwise it is dark and
/// nothing runs. Never in the way of a click.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan GlowFrame = TimeSpan.FromMilliseconds(15);

    private Rectangle? _glow;
    private RadialGradientBrush? _glowBrush;
    private bool _glowRunning;
    private long _glowLast;
    private float _glowLevel;

    partial void SetUpBeatGlow()
    {
        FollowPlugin(BuiltInPlugins.BeatGlow, FollowBeatGlow);
        FollowPlayer(FollowBeatGlow);
        ShownChanged += (_, _) => FollowBeatGlow();
        _services.Visualiser.LiveChanged += (_, _) => FollowBeatGlow();
        _services.Theme.Changed += (_, _) => PaintGlow();
        FollowBeatGlow();
    }

    private void FollowBeatGlow()
    {
        var on = _services.BuiltIns.IsOn(BuiltInPlugins.BeatGlow);
        if (on && _glow is null)
        {
            _glowBrush = new RadialGradientBrush
            {
                Center = new global::Windows.Foundation.Point(0.5, 0.5),
                GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                RadiusX = 0.72,
                RadiusY = 0.72,
            };
            _glow = new Rectangle
            {
                Fill = _glowBrush,
                IsHitTestVisible = false,
                Opacity = 0,
            };
            Grid.SetRowSpan(_glow, 2);
            RootGrid.Children.Add(_glow);
            PaintGlow();
        }
        else if (!on && _glow is not null)
        {
            StopGlow();
            RootGrid.Children.Remove(_glow);
            _glow = null;
            _glowBrush = null;
            return;
        }

        if (!on)
        {
            return;
        }

        var run = IsShown && _services.Player.State.IsPlaying && _services.Theme.AnimationsEnabled;
        if (run && !_glowRunning)
        {
            _glowRunning = true;
            _services.Visualiser.SetWanted(this, true, stage: true);
            _glowLast = 0;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnGlowFrame;
        }
        else if (!run && _glowRunning)
        {
            StopGlow();
        }
    }

    private void StopGlow()
    {
        if (_glowRunning)
        {
            _glowRunning = false;
            _services.Visualiser.SetWanted(this, false, stage: true);
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnGlowFrame;
        }

        _glowLevel = 0;
        if (_glow is not null)
        {
            _glow.Opacity = 0;
        }
    }

    /// <summary>The look's accent, clear in the middle and strongest at the edges.</summary>
    private void PaintGlow()
    {
        if (_glowBrush is null)
        {
            return;
        }

        // The accent or the second accent, reaching as far in as the user's Size.
        var settings = _services.Settings;
        var palette = _services.Theme.Palette;
        var accent = (settings.BeatGlowColour == 1 ? palette.Accent2 : palette.Accent).Opaque;
        var inner = 1 - (Math.Clamp(settings.BeatGlowSize, 10, 60) / 100.0);
        _glowBrush.GradientStops.Clear();
        _glowBrush.GradientStops.Add(new GradientStop { Color = accent.WithAlpha(0).ToColor(), Offset = inner });
        _glowBrush.GradientStops.Add(new GradientStop { Color = accent.WithAlpha(0.35).ToColor(), Offset = inner + ((1 - inner) * 0.68) });
        _glowBrush.GradientStops.Add(new GradientStop { Color = accent.WithAlpha(0.75).ToColor(), Offset = 1 });
    }

    /// <summary>The glow's settings changed.</summary>
    internal void RefreshBeatGlow() => PaintGlow();

    private void OnGlowFrame(object? sender, object e)
    {
        var now = Stopwatch.GetTimestamp();
        if (_glowLast != 0 && Stopwatch.GetElapsedTime(_glowLast, now) < GlowFrame)
        {
            return;
        }

        var seconds = _glowLast == 0 ? 1 / 60f : (float)Math.Min(0.1, Stopwatch.GetElapsedTime(_glowLast, now).TotalSeconds);
        _glowLast = now;
        var frame = _services.Visualiser.Stage.Read();
        var heard = _services.Visualiser.IsLive && !frame.IsAtRest && Stopwatch.GetElapsedTime(frame.Timestamp) < TimeSpan.FromMilliseconds(250);
        var target = heard ? BeatGlow.Bass(frame.Bars.AsSpan(0, frame.BarCount)) : 0;
        _glowLevel = BeatGlow.Follow(_glowLevel, target, seconds);
        var opacity = _glowLevel * Math.Clamp(_services.Settings.BeatGlowStrength, 0, 100) / 100.0;
        if (_glow is not null && Math.Abs(_glow.Opacity - opacity) > 0.004)
        {
            _glow.Opacity = opacity;
        }
    }
}

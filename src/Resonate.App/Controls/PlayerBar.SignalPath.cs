using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Pages;
using Resonate.App.Services;
using Resonate.Spotify.Audio;
using Resonate.Themes;
using Windows.UI;
using Launcher = Windows.System.Launcher;

namespace Resonate.App.Controls;

/// <summary>
/// Signal path, a built-in plugin: a slim pill in the player bar with a
/// coloured dot (green lossless, amber adjusted, red not lossless, grey
/// can't tell) and a short word; clicking it unfolds the chain from the
/// music to the output, each step with a tip and a button that opens the
/// right setting. Only a dot in the compact bar, none in the mini bar.
/// Built in code, so Native AOT never looks up a XAML-created type.
/// </summary>
public sealed partial class PlayerBar
{
    // Mid tones that keep 3:1 against light and dark looks alike.
    private static readonly Color LosslessColor = Color.FromArgb(0xFF, 0x2F, 0x9E, 0x5C);
    private static readonly Color AdjustedColor = Color.FromArgb(0xFF, 0xC2, 0x7C, 0x0E);
    private static readonly Color NotLosslessColor = Color.FromArgb(0xFF, 0xE0, 0x47, 0x4C);
    private static readonly Color UnknownColor = Color.FromArgb(0xFF, 0x8B, 0x8D, 0x91);

    private SignalPathMonitor? _signalPath;
    private Button? _signalPill;
    private Border? _signalDot;
    private TextBlock? _signalBadge;
    private Flyout? _signalFlyout;
    private SignalVerdict? _signalVerdict;
    private PlayerWidthClass? _signalWidthClass;

    /// <summary>Shows the pill for <paramref name="monitor"/>'s verdicts, or takes it away (null).</summary>
    internal void AttachSignalPath(SignalPathMonitor? monitor)
    {
        if (_signalPath is { } old)
        {
            old.Changed -= OnSignalChanged;
            Bar.SizeChanged -= OnSignalBarSizeChanged;
        }

        _signalPath = monitor;
        if (monitor is null)
        {
            _signalFlyout?.Hide();
            SignalSlot.Child = null;
            SignalSlot.Visibility = Visibility.Collapsed;
            _signalPill = null;
            _signalDot = null;
            _signalBadge = null;
            _signalFlyout = null;
            _signalVerdict = null;
            return;
        }

        BuildSignalPill();
        monitor.Changed += OnSignalChanged;
        Bar.SizeChanged += OnSignalBarSizeChanged;
        ShowSignal();
    }

    private void BuildSignalPill()
    {
        _signalDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,

            // A new verdict (another device, a new setting) fades in, on the compositor.
            BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(300) },
        };
        _signalBadge = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 60,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = App.Services.Theme.GetBrush("ResonateTextSecondaryBrush"),
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(_signalDot);
        content.Children.Add(_signalBadge);

        _signalFlyout = new Flyout { Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        _signalFlyout.Opening += OnSignalFlyoutOpening;
        _signalPill = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateSubtleButtonStyle"],
            Content = content,
            MinHeight = 0,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = _signalFlyout,
        };
        SignalSlot.Child = _signalPill;
    }

    private void OnSignalChanged(object? sender, EventArgs e) => ShowSignal();

    private void OnSignalBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // OnBarSizeChanged (registered first) has already picked the width class.
        if (_signalWidthClass != _widthClass)
        {
            ShowSignal();
        }
    }

    private void ShowSignal()
    {
        if (_signalPill is null || _signalDot is null || _signalBadge is null)
        {
            return;
        }

        _signalWidthClass = _widthClass;
        var report = _signalPath?.Report;
        if (report is null || _widthClass == PlayerWidthClass.Mini)
        {
            _signalFlyout?.Hide();
            SignalSlot.Visibility = Visibility.Collapsed;
            return;
        }

        var compact = _widthClass != PlayerWidthClass.Full;
        _signalBadge.Text = report.Badge;
        _signalBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _signalPill.Padding = compact ? new Thickness(9, 0, 9, 0) : new Thickness(10, 0, 11, 0);
        if (_signalVerdict != report.Verdict)
        {
            _signalVerdict = report.Verdict;
            _signalDot.Background = new SolidColorBrush(VerdictColor(report.Verdict));
        }

        ToolTipService.SetToolTip(_signalPill, report.Summary);
        AutomationProperties.SetName(_signalPill, "Signal path: " + report.Title);
        SignalSlot.Visibility = Visibility.Visible;
        if (_signalFlyout is { IsOpen: true })
        {
            FillSignalFlyout(report);
        }
    }

    private void OnSignalFlyoutOpening(object? sender, object e)
    {
        // A fresh look at Spotify's settings and the output; the chain updates if they changed.
        _signalPath?.Refresh();
        if (_signalPath?.Report is { } report)
        {
            FillSignalFlyout(report);
        }
    }

    /// <summary>The verdict, a sentence about it, and the chain from the music to the output, at the user's Text size.</summary>
    private void FillSignalFlyout(SignalReport report)
    {
        if (_signalFlyout is null)
        {
            return;
        }

        var text = App.Services.Theme.TextScale;
        var panel = new StackPanel { Width = 340 * text, Spacing = 12 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(Dot(VerdictColor(report.Verdict), 10));
        heading.Children.Add(new TextBlock { Text = report.Title, FontSize = 16 * text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = report.Summary, FontSize = 14 * text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });

        if (report.Steps.Count > 0)
        {
            var chain = new StackPanel { Spacing = 2 };
            foreach (var step in report.Steps)
            {
                chain.Children.Add(StepRow(step, text));
            }

            panel.Children.Add(chain);
        }

        _signalFlyout.Content = panel;
    }

    /// <summary>One step: its dot, name and value; when it needs attention, a soft glow, a tip and a button.</summary>
    /// <param name="textScale">Text size, as a factor.</param>
    private Grid StepRow(SignalStep step, double textScale)
    {
        var color = StateColor(step.State);
        var attention = step.State is SignalStepState.Adjusted or SignalStepState.Problem;
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Background = attention ? new SolidColorBrush(color with { A = 0x26 }) : null,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = Dot(color, 8);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 6, 0, 0);
        row.Children.Add(dot);

        var text = new StackPanel { Spacing = 2 };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = step.Name, FontSize = 12 * textScale, Opacity = 0.7 });
        text.Children.Add(new TextBlock { Text = step.Value, FontSize = 14 * textScale, TextWrapping = TextWrapping.Wrap });
        if (step.State != SignalStepState.Good)
        {
            if (step.Tip is { } tip)
            {
                text.Children.Add(new TextBlock { Text = tip, FontSize = 12 * textScale, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            }

            if (FixButton(step.Fix, textScale) is { } fix)
            {
                text.Children.Add(fix);
            }
        }

        row.Children.Add(text);
        return row;
    }

    /// <summary>A link to where the step is fixed: Spotify's settings, Resonate's equalizer, or Windows' sound settings.</summary>
    private HyperlinkButton? FixButton(SignalFix fix, double textScale)
    {
        (string Text, Action Open)? link = fix switch
        {
            // With "Spotify Web API only" Resonate leaves the Spotify app alone, so it offers nothing that opens it.
            SignalFix.SpotifySettings when App.Services.UsesSpotifyApp => ("Open Spotify's settings", () => _ = SpotifySettingsLink.OpenAsync()),
            SignalFix.Equalizer => ("Equalizer settings", () => App.MainWindow?.OpenSettings(SettingsSection.Equalizer)),
            SignalFix.SoundSettings => ("Windows sound settings", () => _ = Launcher.LaunchUriAsync(new Uri("ms-settings:sound"))),
            _ => null,
        };
        if (link is not { } chosen)
        {
            return null;
        }

        var button = new HyperlinkButton { Content = chosen.Text, FontSize = 12 * textScale, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 0) };
        button.Click += (_, _) =>
        {
            _signalFlyout?.Hide();
            chosen.Open();
        };
        return button;
    }

    private static Border Dot(Color color, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        VerticalAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(color),
    };

    private static Color VerdictColor(SignalVerdict verdict) => verdict switch
    {
        SignalVerdict.Lossless => LosslessColor,
        SignalVerdict.Adjusted => AdjustedColor,
        SignalVerdict.NotLossless => NotLosslessColor,
        _ => UnknownColor,
    };

    private static Color StateColor(SignalStepState state) => state switch
    {
        SignalStepState.Good => LosslessColor,
        SignalStepState.Adjusted => AdjustedColor,
        SignalStepState.Problem => NotLosslessColor,
        _ => UnknownColor,
    };
}

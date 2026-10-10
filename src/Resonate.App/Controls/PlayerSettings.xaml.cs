using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The Player tab of Settings. Where the player sits, its progress bar,
/// play button, cover, glow and the visualizer styles are part of the look
/// (a preset is copied first, as under Customize); the song change
/// animation, whether Home shows a visualizer and how the visualizers
/// follow the music belong to the user, whatever look is in use.
/// </summary>
public sealed partial class PlayerSettings : UserControl
{
    private readonly AppServices _services = App.Services;
    private readonly ThemeService _theme = App.Services.Theme;
    private readonly Dictionary<ComboBox, Func<ThemeDefinition, string, ThemeDefinition>> _choices;
    private readonly List<Action> _resets = [];

    // True while controls are being set (and while the panel is built), so that is not taken as a change.
    private bool _loading = true;

    public PlayerSettings()
    {
        InitializeComponent();

        _choices = new()
        {
            [ProgressChoice] = (look, tag) => look with { Progress = Enum.Parse<ProgressStyle>(tag) },
            [PlayButtonChoice] = (look, tag) => look with { PlayButton = Enum.Parse<PlayButtonStyle>(tag) },
            [CoverChoice] = (look, tag) => look with { Cover = Enum.Parse<CoverStyle>(tag) },
            [PlayerVisualizerChoice] = (look, tag) => look with { PlayerVisualizer = Enum.Parse<VisualizerStyle>(tag) },
        };

        // Home lists every style (Off hides it); the player bar those that need no cover beside them.
        StageVisualizerChoice.Items.Add(new ComboBoxItem { Content = "Off", Tag = nameof(VisualizerStyle.Off) });
        PlayerVisualizerChoice.Items.Add(new ComboBoxItem { Content = "Off", Tag = nameof(VisualizerStyle.Off) });
        foreach (var style in VisualizerShapes.Offered)
        {
            StageVisualizerChoice.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style.ToString() });
            if (VisualizerShapes.FitsPlayerBar(style))
            {
                PlayerVisualizerChoice.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style.ToString() });
            }
        }

        foreach (var row in StageSettings.HomeVisualizer(_services))
        {
            HomeVisualizerRows.Children.Add(row);
        }

        foreach (var row in StageSettings.PlayerVisualizer(_services))
        {
            PlayerVisualizerRows.Children.Add(row);
        }

        // Reset buttons: the glow goes back to the preset's, the size and place to automatic.
        _resets.Add(ResetButton.Attach(PlayerGlowSlider, () => Math.Round(ThemePresets.Origin(_theme.Current).PlayerGlow * 100), "Glow"));
        _resets.Add(ResetButton.Attach(ProgressGlowSlider, () => Math.Round(ThemePresets.Origin(_theme.Current).ProgressGlow * 100), "Progress glow"));
        _resets.Add(ResetButton.Attach(PlayerWidthBox, "Width"));
        _resets.Add(ResetButton.Attach(PlayerHeightBox, "Height"));
        _resets.Add(ResetButton.Attach(PlayerXBox, "X"));
        _resets.Add(ResetButton.Attach(PlayerYBox, "Y"));

        // Only while shown, so the theme never keeps a closed Settings page alive.
        Loaded += (_, _) =>
        {
            // Loaded can come twice in a row; each handler is held once.
            _theme.Changed -= OnThemeChanged;
            _theme.Changed += OnThemeChanged;
            Refresh();
        };
        Unloaded += (_, _) => _theme.Changed -= OnThemeChanged;
        _loading = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _loading = true;
        try
        {
            var look = _theme.Current;
            var (edge, type) = Split(look.PlayerLayout);
            Select(PlayerEdgeChoice, edge);
            Select(PlayerTypeChoice, type);

            // The sidebar's player has one kind only.
            PlayerTypeChoice.IsEnabled = edge != "Sidebar";
            PlayerWidthBox.Value = look.PlayerWidth ?? double.NaN;
            PlayerHeightBox.Value = look.PlayerHeight ?? double.NaN;
            PlayerXBox.Value = look.PlayerOffsetX ?? double.NaN;
            PlayerYBox.Value = look.PlayerOffsetY ?? double.NaN;

            Select(ProgressChoice, look.Progress.ToString());
            Select(PlayButtonChoice, look.PlayButton.ToString());
            Select(CoverChoice, look.Cover.ToString());
            ProgressPreview.BarStyle = look.Progress;
            PlayerGlowSlider.Value = Math.Round(look.PlayerGlow * 100);
            PlayerGlowText.Text = $"{PlayerGlowSlider.Value:0}%";
            ProgressGlowSlider.Value = Math.Round(look.ProgressGlow * 100);
            ProgressGlowText.Text = $"{ProgressGlowSlider.Value:0}%";
            ProgressPreview.Glow = look.ProgressGlow;
            Select(SongChangeChoice, _services.Settings.SongChangeAnimation);
            ButtonsAboveVolumeSwitch.IsOn = _theme.ButtonsAboveVolume;

            Select(StageVisualizerChoice, _services.Settings.HomeStageVisualizer ? look.StageVisualizer.ToString() : nameof(VisualizerStyle.Off));
            Select(PlayerVisualizerChoice, look.PlayerVisualizer.ToString());
            foreach (var reset in _resets)
            {
                reset();
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && sender is ComboBox combo && combo.SelectedItem is ComboBoxItem { Tag: string tag } && _choices.TryGetValue(combo, out var change))
        {
            _theme.Edit(look => change(look, tag), smooth: true);
        }
    }

    /// <summary>Off hides Home's visualizer (the user's choice); a style shows it, drawn as the look says.</summary>
    private void OnStageVisualizerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StageVisualizerChoice.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var style = Enum.Parse<VisualizerStyle>(tag);
        var on = style != VisualizerStyle.Off;
        if (on != _services.Settings.HomeStageVisualizer)
        {
            _services.Settings.HomeStageVisualizer = on;
            _services.SaveSettings();
            NowPlayingStage.NotifyOptionsChanged();
        }

        if (on && style != _theme.Current.StageVisualizer)
        {
            _theme.Edit(look => look with { StageVisualizer = style }, smooth: true);
        }
    }

    private void OnGlowChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        PlayerGlowText.Text = $"{e.NewValue:0}%";
        if (!_loading)
        {
            var glow = e.NewValue / 100;
            _theme.Edit(look => look with { PlayerGlow = glow });
        }
    }

    private void OnProgressGlowChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ProgressGlowText.Text = $"{e.NewValue:0}%";
        if (!_loading)
        {
            var glow = e.NewValue / 100;
            _theme.Edit(look => look with { ProgressGlow = glow });
        }
    }

    // The song change animation is the user's, not part of a look, so it skips Edit (which would make a custom copy).
    private void OnSongChangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SongChangeChoice.SelectedItem is ComboBoxItem { Tag: string song })
        {
            _services.Settings.SongChangeAnimation = song;
            _services.SaveSettings();
        }
    }

    private void OnButtonsAboveVolumeToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _theme.ButtonsAboveVolume = ButtonsAboveVolumeSwitch.IsOn;
        }
    }

    private void OnPlayerLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading
            && PlayerEdgeChoice.SelectedItem is ComboBoxItem { Tag: string edge }
            && PlayerTypeChoice.SelectedItem is ComboBoxItem { Tag: string type })
        {
            var layout = Join(edge, type);
            _theme.Edit(look => look with { PlayerLayout = layout }, smooth: true);
        }
    }

    private void OnPlayerAdvancedChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        _theme.Edit(look => look with
        {
            PlayerWidth = Value(PlayerWidthBox),
            PlayerHeight = Value(PlayerHeightBox),
            PlayerOffsetX = Value(PlayerXBox),
            PlayerOffsetY = Value(PlayerYBox),
        });

        static double? Value(NumberBox box) => double.IsNaN(box.Value) ? null : Math.Round(box.Value);
    }

    private void OnPlayerAdvancedResetClick(object sender, RoutedEventArgs e) =>
        _theme.Edit(look => look with { PlayerWidth = null, PlayerHeight = null, PlayerOffsetX = null, PlayerOffsetY = null });

    /// <summary>Where the player sits (Top, Bottom, Left, Right, Sidebar) and what kind it is (Docked, Inset, Floating).</summary>
    private static (string Edge, string Type) Split(PlayerLayout layout) => layout switch
    {
        PlayerLayout.Top => ("Top", "Docked"),
        PlayerLayout.FloatingTop => ("Top", "Inset"),
        PlayerLayout.HoveringTop => ("Top", "Floating"),
        PlayerLayout.Floating => ("Bottom", "Inset"),
        PlayerLayout.Hovering => ("Bottom", "Floating"),
        PlayerLayout.Left => ("Left", "Docked"),
        PlayerLayout.InsetLeft => ("Left", "Inset"),
        PlayerLayout.CornerLeft => ("Left", "Floating"),
        PlayerLayout.Right => ("Right", "Docked"),
        PlayerLayout.InsetRight => ("Right", "Inset"),
        PlayerLayout.Corner => ("Right", "Floating"),
        PlayerLayout.Sidebar => ("Sidebar", "Inset"),
        _ => ("Bottom", "Docked"),
    };

    private static PlayerLayout Join(string edge, string type) => (edge, type) switch
    {
        ("Top", "Docked") => PlayerLayout.Top,
        ("Top", "Inset") => PlayerLayout.FloatingTop,
        ("Top", "Floating") => PlayerLayout.HoveringTop,
        ("Bottom", "Inset") => PlayerLayout.Floating,
        ("Bottom", "Floating") => PlayerLayout.Hovering,
        ("Left", "Docked") => PlayerLayout.Left,
        ("Left", "Inset") => PlayerLayout.InsetLeft,
        ("Left", "Floating") => PlayerLayout.CornerLeft,
        ("Right", "Docked") => PlayerLayout.Right,
        ("Right", "Inset") => PlayerLayout.InsetRight,
        ("Right", "Floating") => PlayerLayout.Corner,
        ("Sidebar", _) => PlayerLayout.Sidebar,
        _ => PlayerLayout.Docked,
    };

    private static void Select(ComboBox choice, string tag) =>
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == tag);
}

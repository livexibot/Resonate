using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The settings of the Home stage, its visualizer and the away screen,
/// built in code: the visualizer's under Settings, Player, the stage's
/// background under Layout, and the away screen's under its plugin.
/// </summary>
internal static class StageSettings
{
    /// <summary>The minutes the away screen can wait, the first being the shortest.</summary>
    public static readonly int[] AwayMinutes = [2, 5, 10, 15];

    /// <summary>
    /// Home's visualizer (Settings, Player, Home visualizer): how it follows
    /// the music, how many of its parts there are and how large (Amount and
    /// Size mean bars, dots, sparks, columns or rings, whatever the style),
    /// whether the visualizers hear Spotify, and its width, height and place
    /// under Advanced.
    /// </summary>
    public static IEnumerable<FrameworkElement> HomeVisualizer(AppServices services)
    {
        var settings = services.Settings;
        return
        [
            Slider("Sensitivity", 50, 200, () => settings.HomeStageSensitivity, v => settings.HomeStageSensitivity = v, "%", services),
            Slider("Smoothing", 0, 100, () => settings.HomeStageSmoothing, v => settings.HomeStageSmoothing = v, "%", services),
            Slider("Amount", 16, 96, () => settings.HomeStageBars, v => settings.HomeStageBars = v, string.Empty, services),
            Slider("Size", 20, 90, () => settings.HomeStageBarWidth, v => settings.HomeStageBarWidth = v, "%", services),
            Switch(
                "Listen to Spotify",
                string.Empty,
                () => settings.HomeStageListens,
                on =>
                {
                    settings.HomeStageListens = on;
                    services.Visualiser.ListensToSpotify = on;
                },
                services),
            Advanced(
                Slider("Width", 20, 100, () => settings.HomeStageWidth, v => settings.HomeStageWidth = v, "%", services),
                Slider("Height", 10, 60, () => settings.HomeStageHeight, v => settings.HomeStageHeight = v, "%", services),
                Slider("Max height", 40, 600, () => settings.HomeStageMaxHeight, v => settings.HomeStageMaxHeight = v, "px", services),
                Slider("X", -800, 800, () => settings.HomeStageX, v => settings.HomeStageX = v, "px", services),
                Slider("Y", -400, 400, () => settings.HomeStageY, v => settings.HomeStageY = v, "px", services)),
        ];
    }

    /// <summary>The player bar's visualizer (Settings, Player, Player visualizer): its own Sensitivity, Smoothing, Amount and Size, and X and Y under Advanced.</summary>
    public static IEnumerable<FrameworkElement> PlayerVisualizer(AppServices services)
    {
        var settings = services.Settings;
        return
        [
            Slider("Sensitivity", 50, 200, () => settings.PlayerVisualizerSensitivity, v => settings.PlayerVisualizerSensitivity = v, "%", services),
            Slider("Smoothing", 0, 100, () => settings.PlayerVisualizerSmoothing, v => settings.PlayerVisualizerSmoothing = v, "%", services),
            Slider("Amount", 16, 96, () => settings.PlayerVisualizerAmount, v => settings.PlayerVisualizerAmount = v, string.Empty, services),
            Slider("Size", 20, 90, () => settings.PlayerVisualizerSize, v => settings.PlayerVisualizerSize = v, "%", services),
            Advanced(
                Slider("X", -800, 800, () => settings.PlayerVisualizerX, v => settings.PlayerVisualizerX = v, "px", services),
                Slider("Y", -100, 100, () => settings.PlayerVisualizerY, v => settings.PlayerVisualizerY = v, "px", services)),
        ];
    }

    /// <summary>A folded "Advanced" section holding <paramref name="rows"/>.</summary>
    private static Expander Advanced(params FrameworkElement[] rows)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return new Expander
        {
            Header = "Advanced",
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = panel,
        };
    }

    /// <summary>The Home stage's background: the blurred cover behind the clouds (Settings, Layout, Pages).</summary>
    public static FrameworkElement HomeStage(AppServices services) => Switch(
        "Blurred cover on Home",
        string.Empty,
        () => services.Settings.HomeStageBlurredCover,
        on => services.Settings.HomeStageBlurredCover = on,
        services);

    /// <summary>The away screen: how long to wait before it shows.</summary>
    public static FrameworkElement AwayScreen(AppServices services)
    {
        var combo = new ComboBox { MinWidth = 140 };
        foreach (var minutes in AwayMinutes)
        {
            combo.Items.Add($"{minutes} minutes");
        }

        combo.SelectedIndex = Math.Max(0, Array.IndexOf(AwayMinutes, AwayAfter(services.Settings)));
        AutomationProperties.SetName(combo, "Show after");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && AwayMinutes[combo.SelectedIndex] != services.Settings.AwayScreenMinutes)
            {
                services.Settings.AwayScreenMinutes = AwayMinutes[combo.SelectedIndex];
                services.SaveSettings();
            }
        };
        return new SettingRow
        {
            Header = "Show after",
            Description = "Without touching the mouse or keyboard.",
            Content = combo,
        };
    }

    /// <summary>A stage option as a switch: saved and shown on every stage at once.</summary>
    private static SettingRow Switch(string header, string description, Func<bool> read, Action<bool> write, AppServices services)
    {
        var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, IsOn = read() };
        AutomationProperties.SetName(toggle, header);
        toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn != read())
            {
                write(toggle.IsOn);
                services.SaveSettings();
                NowPlayingStage.NotifyOptionsChanged();
            }
        };
        return new SettingRow { Header = header, Description = description, Content = toggle };
    }

    /// <summary>A stage option as a slider of whole numbers: saved, and shown on every stage, as it moves.</summary>
    private static SettingRow Slider(string header, int minimum, int maximum, Func<int> read, Action<int> write, string unit, AppServices services)
    {
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = 1,
            Width = 180,
            Value = Math.Clamp(read(), minimum, maximum),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, header);
        var value = new TextBlock
        {
            MinWidth = 40,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"],
            Text = $"{read()}{(unit.Length > 0 ? " " + unit : string.Empty)}",
        };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            value.Text = $"{v}{(unit.Length > 0 ? " " + unit : string.Empty)}";
            if (v != read())
            {
                write(v);
                services.SaveSettings();
                NowPlayingStage.NotifyOptionsChanged();
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, value } };
        return new SettingRow { Header = header, Content = row };
    }

    /// <summary>The away screen's wait in minutes: one of <see cref="AwayMinutes"/> (5 for anything else in the file).</summary>
    public static int AwayAfter(AppSettings settings) =>
        Array.IndexOf(AwayMinutes, settings.AwayScreenMinutes) >= 0 ? settings.AwayScreenMinutes : 5;
}

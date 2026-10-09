using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The settings of the Home stage and the away screen (built-in plugins),
/// shown under their switches in Settings, Plugins. Built in code, like
/// the rest of that section.
/// </summary>
internal static class StageSettings
{
    /// <summary>The minutes the away screen can wait, the first being the shortest.</summary>
    public static readonly int[] AwayMinutes = [2, 5, 10, 15];

    /// <summary>The Home stage: the visualizer and whether it hears Spotify, and the blurred cover behind the clouds.</summary>
    public static FrameworkElement HomeStage(AppServices services) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Switch(
                "Visualizer",
                string.Empty,
                () => services.Settings.HomeStageVisualizer,
                on => services.Settings.HomeStageVisualizer = on,
                services),
            Slider("Sensitivity", 50, 200, () => services.Settings.HomeStageSensitivity, v => services.Settings.HomeStageSensitivity = v, "%", services),
            Slider("Smoothing", 0, 100, () => services.Settings.HomeStageSmoothing, v => services.Settings.HomeStageSmoothing = v, "%", services),
            Slider("Bars", 16, 96, () => services.Settings.HomeStageBars, v => services.Settings.HomeStageBars = v, string.Empty, services),
            Slider("Bar width", 20, 90, () => services.Settings.HomeStageBarWidth, v => services.Settings.HomeStageBarWidth = v, "%", services),
            Switch(
                "Listen to Spotify",
                "The bars hear Spotify's sound only. Nothing is kept.",
                () => services.Settings.HomeStageListens,
                on =>
                {
                    services.Settings.HomeStageListens = on;
                    services.Visualiser.ListensToSpotify = on;
                },
                services),
            Switch(
                "Blurred cover",
                string.Empty,
                () => services.Settings.HomeStageBlurredCover,
                on => services.Settings.HomeStageBlurredCover = on,
                services),
        },
    };

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
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", IsOn = read() };
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

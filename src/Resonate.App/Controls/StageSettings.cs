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

    /// <summary>The Home stage: the visualizer, and the blurred cover behind the clouds.</summary>
    public static FrameworkElement HomeStage(AppServices services) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Switch(
                "Visualizer",
                "Moves with your own music files, and on its own for Spotify songs.",
                () => services.Settings.HomeStageVisualizer,
                on => services.Settings.HomeStageVisualizer = on,
                services),
            Switch(
                "Blurred cover",
                "The cover, blurred, behind the clouds.",
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

    /// <summary>The away screen's wait in minutes: one of <see cref="AwayMinutes"/> (5 for anything else in the file).</summary>
    public static int AwayAfter(AppSettings settings) =>
        Array.IndexOf(AwayMinutes, settings.AwayScreenMinutes) >= 0 ? settings.AwayScreenMinutes : 5;
}

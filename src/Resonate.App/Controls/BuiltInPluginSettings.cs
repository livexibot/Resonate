using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The settings a built-in plugin opens in a popup from the gear beside its
/// switch in Settings, Plugins, while it is on (the owner's request,
/// 9 October 2026); null for none. Built in code, like the rest of the
/// Plugins section.
/// </summary>
internal static class BuiltInPluginSettings
{
    /// <summary>The screensaver's waits, in minutes.</summary>
    public static readonly int[] ScreensaverMinutes = [1, 2, 5, 10, 15, 30];

    private static readonly string[] Backgrounds = ["Song", "Cover", "Colour"];

    /// <summary>Whether <paramref name="id"/> has settings (so its gear shows).</summary>
    public static bool Has(string id) => id is BuiltInPlugins.AwayScreen or BuiltInPlugins.SummonBar or BuiltInPlugins.SongNotifications
        or BuiltInPlugins.KeepAwake or BuiltInPlugins.NowPlayingFile or BuiltInPlugins.QuietHours or BuiltInPlugins.DesktopLyrics
        or BuiltInPlugins.BeatGlow or BuiltInPlugins.MediaShortcuts or BuiltInPlugins.HistoryExport;

    public static FrameworkElement? Create(string id, AppServices services) => id switch
    {
        BuiltInPlugins.AwayScreen => Screensaver(services),
        BuiltInPlugins.SummonBar => SummonBarSettings.Create(services),
        BuiltInPlugins.SongNotifications => Stack(PluginRows.Switch(
            "Also while Resonate is in front", () => services.Settings.SongNotificationsAlways, on => services.Settings.SongNotificationsAlways = on, services)),
        BuiltInPlugins.KeepAwake => Stack(PluginRows.Switch(
            "Keep the screen on too", () => services.Settings.KeepAwakeDisplay, on => services.Settings.KeepAwakeDisplay = on, services, () => App.MainWindow?.FollowKeepAwake())),
        BuiltInPlugins.NowPlayingFile => Stack(
            PluginRows.Text("File", NowPlayingFile.DefaultPath, () => services.Settings.NowPlayingFilePath, path => services.Settings.NowPlayingFilePath = path, services, () => App.MainWindow?.WriteNowPlaying()),
            PluginRows.Text("Text", "{artist} - {title}", () => services.Settings.NowPlayingFormat, text => services.Settings.NowPlayingFormat = text ?? "{artist} - {title}", services, () => App.MainWindow?.WriteNowPlaying())),
        BuiltInPlugins.QuietHours => Stack(
            PluginRows.Choice("From", Hours(), () => services.Settings.QuietHoursFrom, hour => services.Settings.QuietHoursFrom = hour, services, () => App.MainWindow?.FollowQuietHours()),
            PluginRows.Choice("Until", Hours(), () => services.Settings.QuietHoursTo, hour => services.Settings.QuietHoursTo = hour, services, () => App.MainWindow?.FollowQuietHours()),
            PluginRows.Slider("Loudest", 5, 100, () => services.Settings.QuietHoursVolume, v => services.Settings.QuietHoursVolume = v, new AppSettings().QuietHoursVolume, "%", services, () => App.MainWindow?.FollowQuietHours())),
        BuiltInPlugins.DesktopLyrics => Stack(PluginRows.Slider(
            "Size", 60, 250, () => services.Settings.DesktopLyricsSize, v => services.Settings.DesktopLyricsSize = v, new AppSettings().DesktopLyricsSize, "%", services, () => App.MainWindow?.RefreshDesktopLyrics())),
        BuiltInPlugins.BeatGlow => Stack(PluginRows.Slider(
            "Strength", 10, 100, () => services.Settings.BeatGlowStrength, v => services.Settings.BeatGlowStrength = v, new AppSettings().BeatGlowStrength, "%", services)),
        BuiltInPlugins.MediaShortcuts => MediaShortcuts(services),
        BuiltInPlugins.HistoryExport => Stack(PluginRows.Button("Listening history", "Export…", () => _ = App.MainWindow?.ExportHistoryAsync())),
        _ => null,
    };

    /// <summary>The screensaver's wait in minutes: one of <see cref="ScreensaverMinutes"/> (5 for anything else in the file).</summary>
    public static int ScreensaverAfter(AppSettings settings) =>
        Array.IndexOf(ScreensaverMinutes, settings.AwayScreenMinutes) >= 0 ? settings.AwayScreenMinutes : 5;

    private static StackPanel Stack(params FrameworkElement[] rows)
    {
        var panel = new StackPanel { Spacing = 6, MinWidth = 420 };
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return panel;
    }

    private static string[] Hours() => [.. Enumerable.Range(0, 24).Select(h => new DateTime(2000, 1, 1, h, 0, 0).ToString("t", System.Globalization.CultureInfo.CurrentCulture))];

    private static StackPanel Screensaver(AppServices services)
    {
        var settings = services.Settings;
        var styles = VisualizerShapes.Offered;
        var visualizerLabels = new List<string> { "Same as Home", "Off" };
        visualizerLabels.AddRange(styles.Select(s => s.ToString()));
        int VisualizerIndex()
        {
            if (settings.ScreensaverVisualizer is null)
            {
                return 0;
            }

            if (settings.ScreensaverVisualizer == "Off")
            {
                return 1;
            }

            var at = styles.ToList().FindIndex(s => s.ToString() == settings.ScreensaverVisualizer);
            return at < 0 ? 0 : at + 2;
        }

        var colour = PluginRows.Colour(
            "Colour",
            () => ThemeColor.TryParse(settings.ScreensaverColour, out var c) ? c : ThemeColor.Black,
            c => settings.ScreensaverColour = c.ToString(),
            services);
        void ShowColour() => colour.Visibility = settings.ScreensaverBackground == "Colour" && !settings.ScreensaverOled ? Visibility.Visible : Visibility.Collapsed;
        ShowColour();

        var panel = Stack(
            PluginRows.Choice(
                "Show after",
                [.. ScreensaverMinutes.Select(m => m == 1 ? "1 minute" : $"{m} minutes")],
                () => Math.Max(0, Array.IndexOf(ScreensaverMinutes, ScreensaverAfter(settings))),
                i => settings.AwayScreenMinutes = ScreensaverMinutes[i],
                services),
            PluginRows.Switch("Only while music plays", () => settings.ScreensaverOnlyWhilePlaying, on => settings.ScreensaverOnlyWhilePlaying = on, services, () => App.MainWindow?.FollowScreensaver()),
            PluginRows.Choice(
                "Visualizer",
                visualizerLabels,
                VisualizerIndex,
                i => settings.ScreensaverVisualizer = i switch
                {
                    0 => null,
                    1 => "Off",
                    _ => styles[i - 2].ToString(),
                },
                services),
            PluginRows.Choice(
                "Background",
                ["Song colours", "Blurred cover", "Colour"],
                () => Math.Max(0, Array.IndexOf(Backgrounds, settings.ScreensaverBackground)),
                i => settings.ScreensaverBackground = Backgrounds[i],
                services,
                ShowColour),
            colour,
            PluginRows.Switch("OLED mode", () => settings.ScreensaverOled, on => settings.ScreensaverOled = on, services, ShowColour),
            PluginRows.Switch("Clock", () => settings.ScreensaverClock, on => settings.ScreensaverClock = on, services),
            PluginRows.Button("Try it", "Show now", () => App.MainWindow?.ShowScreensaverNow()));
        return panel;
    }

    private static StackPanel MediaShortcuts(AppServices services)
    {
        var settings = services.Settings;
        ShortcutBox Box(MediaShortcut action, string header) => new(
            header,
            null,
            () => MediaShortcutKeys.Read(settings, action),
            shortcut => App.MainWindow?.SetMediaShortcut(action, shortcut),
            () => App.MainWindow?.PauseMediaShortcuts(),
            () => App.MainWindow?.ResumeMediaShortcuts(),
            services);
        return Stack(
            Box(MediaShortcut.PlayPause, "Play or pause"),
            Box(MediaShortcut.Next, "Next song"),
            Box(MediaShortcut.Previous, "Previous song"),
            Box(MediaShortcut.VolumeUp, "Volume up"),
            Box(MediaShortcut.VolumeDown, "Volume down"),
            Box(MediaShortcut.Like, "Like the song"));
    }
}

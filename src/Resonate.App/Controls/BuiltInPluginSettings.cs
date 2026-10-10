using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    private static readonly string[] Days = ["Every day", "Weekdays", "Weekends"];

    /// <summary>Whether <paramref name="id"/> has settings (so its gear shows): every plugin has some.</summary>
    public static bool Has(string id) => id is not (BuiltInPlugins.Lyrics or BuiltInPlugins.HomeStage);

    public static FrameworkElement? Create(string id, AppServices services)
    {
        var settings = services.Settings;
        var fresh = new AppSettings();
        static void Changed() => App.MainWindow?.PluginOptionsChanged();
        SettingRow Switch(string header, Func<bool> read, Action<bool> write) => PluginRows.Switch(header, read, write, services, Changed);
        SettingRow Slider(string header, int minimum, int maximum, Func<int> read, Action<int> write, int fallback, string unit) =>
            PluginRows.Slider(header, minimum, maximum, read, write, fallback, unit, services, Changed);
        SettingRow Choice(string header, IReadOnlyList<string> labels, Func<int> read, Action<int> write) => PluginRows.Choice(header, labels, read, write, services, Changed);

        return id switch
        {
            BuiltInPlugins.AwayScreen => Screensaver(services),
            BuiltInPlugins.Rediscover => Stack(
                Switch("On this day", () => settings.RediscoverOnThisDay, on => settings.RediscoverOnThisDay = on),
                Switch("Gathering dust", () => settings.RediscoverDust, on => settings.RediscoverDust = on),
                Switch("Deep cuts", () => settings.RediscoverDeepCuts, on => settings.RediscoverDeepCuts = on)),
            BuiltInPlugins.ArtistOrbit => Stack(
                Slider("Artists", 3, 10, () => settings.RelatedArtistsCount, v => settings.RelatedArtistsCount = v, fresh.RelatedArtistsCount, string.Empty),
                Switch("Liked albums", () => settings.RelatedArtistsAlbums, on => settings.RelatedArtistsAlbums = on)),
            BuiltInPlugins.SmartPlaylists => Stack(
                Switch("In the sidebar", () => settings.SmartPlaylistsInSidebar, on => settings.SmartPlaylistsInSidebar = on),
                Switch("New smart playlist link", () => settings.SmartPlaylistsNewLink, on => settings.SmartPlaylistsNewLink = on)),
            BuiltInPlugins.WindowShapes => Stack(
                Switch("Button in the title bar", () => settings.WindowShapesButton, on => settings.WindowShapesButton = on),
                Switch("Column shape", () => settings.WindowShapesColumn, on => settings.WindowShapesColumn = on),
                Switch("Strip shape", () => settings.WindowShapesStrip, on => settings.WindowShapesStrip = on)),
            BuiltInPlugins.SummonBar => Stack(
                SummonBarSettings.Create(services),
                Switch("Search Spotify too", () => settings.QuickSearchSpotify, on => settings.QuickSearchSpotify = on)),
            BuiltInPlugins.SignalPath => Stack(
                Choice("Show", ["Always", "Only when not lossless"], () => settings.LosslessBadgeOnlyWhenNot ? 1 : 0, i => settings.LosslessBadgeOnlyWhenNot = i == 1),
                Switch("Word beside the dot", () => settings.LosslessBadgeText, on => settings.LosslessBadgeText = on)),
            BuiltInPlugins.PauseOnLock => Stack(
                Switch("Play on after unlocking", () => settings.PauseOnLockResume, on => settings.PauseOnLockResume = on)),
            BuiltInPlugins.PauseOnUnplug => Stack(
                Switch("Play on when they come back", () => settings.PauseOnUnplugResume, on => settings.PauseOnUnplugResume = on)),
            BuiltInPlugins.PlayerLyrics => Stack(
                Switch("Next line", () => settings.PlayerLyricsNextLine, on => settings.PlayerLyricsNextLine = on)),
            BuiltInPlugins.SongNotifications => Stack(
                Switch("Also while Resonate is in front", () => settings.SongNotificationsAlways, on => settings.SongNotificationsAlways = on),
                Switch("Cover", () => settings.SongNotificationsCover, on => settings.SongNotificationsCover = on),
                Switch("Album", () => settings.SongNotificationsAlbum, on => settings.SongNotificationsAlbum = on),
                Switch("Sound", () => settings.SongNotificationsSound, on => settings.SongNotificationsSound = on)),
            BuiltInPlugins.KeepAwake => Stack(
                Switch("Keep the screen on too", () => settings.KeepAwakeDisplay, on => settings.KeepAwakeDisplay = on),
                Switch("Also while paused", () => settings.KeepAwakeWhilePaused, on => settings.KeepAwakeWhilePaused = on)),
            BuiltInPlugins.NowPlayingFile => Stack(
                PluginRows.Text("File", NowPlayingFile.DefaultPath, () => settings.NowPlayingFilePath, path => settings.NowPlayingFilePath = path, services, Changed),
                PluginRows.Text("Text", "{artist} - {title}", () => settings.NowPlayingFormat, text => settings.NowPlayingFormat = text ?? "{artist} - {title}", services, Changed),
                Switch("Empty while paused", () => settings.NowPlayingClearWhenPaused, on => settings.NowPlayingClearWhenPaused = on)),
            BuiltInPlugins.QuietHours => Stack(
                Choice("From", Hours(), () => settings.QuietHoursFrom, hour => settings.QuietHoursFrom = hour),
                Choice("Until", Hours(), () => settings.QuietHoursTo, hour => settings.QuietHoursTo = hour),
                Choice("Days", Days, () => Math.Clamp(settings.QuietHoursDays, 0, 2), i => settings.QuietHoursDays = i),
                Slider("Loudest", 5, 100, () => settings.QuietHoursVolume, v => settings.QuietHoursVolume = v, fresh.QuietHoursVolume, "%")),
            BuiltInPlugins.PauseForSounds => Stack(
                Slider("Wait before pausing", 1, 10, () => settings.PauseForSoundsWait, v => settings.PauseForSoundsWait = v, fresh.PauseForSoundsWait, "s"),
                Slider("Play on after", 1, 30, () => settings.PauseForSoundsResume, v => settings.PauseForSoundsResume = v, fresh.PauseForSoundsResume, "s"),
                Switch("Lower the volume instead", () => settings.PauseForSoundsLower, on => settings.PauseForSoundsLower = on),
                Slider("Lower to", 5, 80, () => settings.PauseForSoundsLowerTo, v => settings.PauseForSoundsLowerTo = v, fresh.PauseForSoundsLowerTo, "%")),
            BuiltInPlugins.DesktopLyrics => Stack(
                Slider("Size", 60, 250, () => settings.DesktopLyricsSize, v => settings.DesktopLyricsSize = v, fresh.DesktopLyricsSize, "%"),
                Slider("Background", 0, 100, () => settings.DesktopLyricsBackground, v => settings.DesktopLyricsBackground = v, fresh.DesktopLyricsBackground, "%"),
                Switch("Next line", () => settings.DesktopLyricsNextLine, on => settings.DesktopLyricsNextLine = on)),
            BuiltInPlugins.BeatGlow => Stack(
                Slider("Strength", 10, 100, () => settings.BeatGlowStrength, v => settings.BeatGlowStrength = v, fresh.BeatGlowStrength, "%"),
                Slider("Size", 10, 60, () => settings.BeatGlowSize, v => settings.BeatGlowSize = v, fresh.BeatGlowSize, "%"),
                Choice("Colour", ["Accent", "Second accent"], () => Math.Clamp(settings.BeatGlowColour, 0, 1), i => settings.BeatGlowColour = i)),
            BuiltInPlugins.MediaShortcuts => MediaShortcuts(services),
            BuiltInPlugins.HistoryExport => Stack(
                Choice("Plays from", ["All time", "The last 7 days", "The last 30 days", "The last year"], () => Math.Max(0, Array.IndexOf(ExportDays, settings.HistoryExportDays)), i => settings.HistoryExportDays = ExportDays[i]),
                PluginRows.Button("Listening history", "Export…", () => _ = App.MainWindow?.ExportHistoryAsync())),
            BuiltInPlugins.Alarm => Alarm(services),
            BuiltInPlugins.FocusTimer => Stack(
                FocusRow(),
                Slider("Focus", 5, 90, () => settings.FocusMinutes, v => settings.FocusMinutes = v, fresh.FocusMinutes, "min"),
                Slider("Break", 1, 30, () => settings.FocusBreakMinutes, v => settings.FocusBreakMinutes = v, fresh.FocusBreakMinutes, "min"),
                Slider("Rounds", 1, 12, () => settings.FocusRounds, v => settings.FocusRounds = v, fresh.FocusRounds, string.Empty),
                Switch("Pause the music for breaks", () => settings.FocusPauseOnBreak, on => settings.FocusPauseOnBreak = on),
                Switch("Windows notifications", () => settings.FocusNotify, on => settings.FocusNotify = on)),
            BuiltInPlugins.SkipIntros => Stack(
                Slider("Skip the first", 0, 60, () => settings.SkipIntroSeconds, v => settings.SkipIntroSeconds = v, fresh.SkipIntroSeconds, "s"),
                Slider("Skip the last", 0, 60, () => settings.SkipOutroSeconds, v => settings.SkipOutroSeconds = v, fresh.SkipOutroSeconds, "s"),
                Slider("Songs longer than", 30, 600, () => settings.SkipShortestSeconds, v => settings.SkipShortestSeconds = v, fresh.SkipShortestSeconds, "s")),
            BuiltInPlugins.DeviceVolume => Stack(
                Slider("New devices start at", 0, 100, () => settings.DeviceVolumeNew, v => settings.DeviceVolumeNew = v, fresh.DeviceVolumeNew, "%"),
                Switch("Say when the volume changes", () => settings.DeviceVolumeMessage, on => settings.DeviceVolumeMessage = on),
                PluginRows.Button($"{settings.DeviceVolumes.Count} remembered", "Forget all", () => App.MainWindow?.ForgetDeviceVolumes())),
            _ => null,
        };
    }

    /// <summary>Export history's ranges in days (0 for all of it).</summary>
    private static readonly int[] ExportDays = [0, 7, 30, 365];

    /// <summary>The focus timer's state and its Start or Stop button.</summary>
    private static SettingRow FocusRow()
    {
        var button = new Button();
        var row = new SettingRow { Content = button };
        void Show()
        {
            var main = App.MainWindow;
            button.Content = main?.FocusRunning == true ? "Stop" : "Start";
            row.Header = main?.FocusStatus ?? "Not running";
        }

        button.Click += (_, _) =>
        {
            App.MainWindow?.ToggleFocus();
            Show();
        };
        Show();
        return row;
    }

    /// <summary>The alarm: when, which days, what plays and how.</summary>
    private static StackPanel Alarm(AppServices services)
    {
        var settings = services.Settings;
        var fresh = new AppSettings();
        static void Changed() => App.MainWindow?.PluginOptionsChanged();

        // Liked Songs, then the playlists in the library.
        var playlists = services.Library.Snapshot?.Playlists.ToList() ?? [];
        var labels = new List<string> { "Liked Songs" };
        labels.AddRange(playlists.Select(p => p.Name));
        int Picked()
        {
            var at = playlists.FindIndex(p => p.Id == settings.AlarmPlaylist);
            return at < 0 ? 0 : at + 1;
        }

        // The time in Windows' own picker, five minutes at a time.
        var time = new TimePicker
        {
            MinuteIncrement = 5,
            Time = new TimeSpan(Math.Clamp(settings.AlarmHour, 0, 23), Math.Clamp(settings.AlarmMinute, 0, 59), 0),
        };
        AutomationProperties.SetName(time, "Alarm time");
        time.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is { } picked)
            {
                settings.AlarmHour = picked.Hours;
                settings.AlarmMinute = picked.Minutes;
                services.SaveSettings();
                Changed();
            }
        };

        return Stack(
            new SettingRow { Header = "Time", Content = time },
            PluginRows.Choice("Days", Days, () => Math.Clamp(settings.AlarmDays, 0, 2), i => settings.AlarmDays = i, services, Changed),
            PluginRows.Choice("Play", labels, Picked, i => settings.AlarmPlaylist = i == 0 ? null : playlists[i - 1].Id, services, Changed),
            PluginRows.Switch("Shuffle", () => settings.AlarmShuffle, on => settings.AlarmShuffle = on, services, Changed),
            PluginRows.Slider("Fade in", 0, 15, () => settings.AlarmFadeMinutes, v => settings.AlarmFadeMinutes = v, fresh.AlarmFadeMinutes, " min", services, Changed),
            PluginRows.Slider("Volume", 5, 100, () => settings.AlarmVolume, v => settings.AlarmVolume = v, fresh.AlarmVolume, "%", services, Changed),
            PluginRows.Button("Try it", "Play now", () => _ = App.MainWindow?.RingAlarmAsync()));
    }

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
            PluginRows.Slider("Brightness", 20, 100, () => settings.ScreensaverBrightness, v => settings.ScreensaverBrightness = v, new AppSettings().ScreensaverBrightness, "%", services),
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

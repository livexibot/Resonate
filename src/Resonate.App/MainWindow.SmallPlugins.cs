using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Microsoft.Windows.Storage.Pickers;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App;

/// <summary>
/// The smaller built-in plugins the owner asked for on 9 October 2026 (see
/// <see cref="BuiltInPlugins"/>): Keep PC awake, Now playing file, Quiet
/// hours and Export history, each running only while it is on; and Start
/// with Windows, part of the app since then (Settings, General, on at first).
/// </summary>
public sealed partial class MainWindow
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x1;
    private const uint EsDisplayRequired = 0x2;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Resonate";

    private uint _keepAwakeState = EsContinuous;
    private string? _nowPlayingWritten;
    private DispatcherQueueTimer? _quietTimer;

    // Keep PC awake

    partial void SetUpKeepAwake()
    {
        FollowPlugin(BuiltInPlugins.KeepAwake, FollowKeepAwake);
        FollowPlayer(FollowKeepAwake);
        Closed += (_, _) => SetThreadExecutionState(EsContinuous);
        FollowKeepAwake();
    }

    /// <summary>While music plays, Windows is told the PC (and with the setting, the screen) is in use; otherwise it may sleep as usual.</summary>
    internal void FollowKeepAwake()
    {
        var awake = _services.BuiltIns.IsOn(BuiltInPlugins.KeepAwake) && (_services.Player.State.IsPlaying || _services.Settings.KeepAwakeWhilePaused);
        var state = awake ? EsContinuous | EsSystemRequired | (_services.Settings.KeepAwakeDisplay ? EsDisplayRequired : 0) : EsContinuous;
        if (state != _keepAwakeState)
        {
            _keepAwakeState = state;
            SetThreadExecutionState(state);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    // Now playing file

    partial void SetUpNowPlayingFile()
    {
        FollowPlugin(BuiltInPlugins.NowPlayingFile, () =>
        {
            _nowPlayingWritten = null;
            WriteNowPlaying(clear: !_services.BuiltIns.IsOn(BuiltInPlugins.NowPlayingFile));
        });
        FollowPlayer(() => WriteNowPlaying());
        WriteNowPlaying();
    }

    /// <summary>Writes the playing song (nothing while paused) to the file, when it changed; with <paramref name="clear"/> the file is emptied.</summary>
    internal void WriteNowPlaying(bool clear = false)
    {
        if (!clear && !_services.BuiltIns.IsOn(BuiltInPlugins.NowPlayingFile))
        {
            return;
        }

        var settings = _services.Settings;
        var state = _services.Player.State;
        var text = clear || (!state.IsPlaying && settings.NowPlayingClearWhenPaused) ? string.Empty : NowPlayingText.Format(settings.NowPlayingFormat, state.Title, state.Artists, state.Album);
        if (text == _nowPlayingWritten)
        {
            return;
        }

        _nowPlayingWritten = text;
        var path = settings.NowPlayingFilePath ?? NowPlayingFile.DefaultPath;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A folder that cannot be written: tried again with the next song.
                DispatcherQueue.TryEnqueue(() => _nowPlayingWritten = null);
            }
        });
    }

    // Quiet hours

    partial void SetUpQuietHours()
    {
        FollowPlugin(BuiltInPlugins.QuietHours, FollowQuietHours);
        FollowPlayer(CapQuietVolume);
        FollowQuietHours();
    }

    /// <summary>Checks the hour once a minute while on, and keeps the volume under the cap during quiet hours.</summary>
    internal void FollowQuietHours()
    {
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.QuietHours))
        {
            _quietTimer?.Stop();
            return;
        }

        if (_quietTimer is null)
        {
            _quietTimer = DispatcherQueue.CreateTimer();
            _quietTimer.Interval = TimeSpan.FromMinutes(1);
            _quietTimer.Tick += (_, _) => CapQuietVolume();
        }

        _quietTimer.Start();
        CapQuietVolume();
    }

    private void CapQuietVolume()
    {
        var settings = _services.Settings;
        var now = DateTime.Now;
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.QuietHours) || !QuietHours.OnDay(settings.QuietHoursDays, now.DayOfWeek)
            || !QuietHours.IsQuiet(settings.QuietHoursFrom, settings.QuietHoursTo, now.Hour))
        {
            return;
        }

        var volume = _services.Player.State.Volume;
        var capped = QuietHours.Cap(volume, settings.QuietHoursVolume);
        if (volume - capped > 0.005)
        {
            _ = _services.Player.SetVolumeAsync(capped);
        }
    }

    // Start with Windows

    partial void SetUpStartWithWindows()
    {
        // At every start of the installed copy, so the address is this copy's (an update keeps it, a reinstall may not).
        if (!_services.IsDemo && StartupOptions.Current.DataFolder is null && _services.Updates.IsInstalled)
        {
            FollowStartWithWindows();
        }
    }

    /// <summary>The switch in Settings, General.</summary>
    internal void SetStartWithWindows(bool on)
    {
        _services.Settings.StartWithWindows = on;
        _services.SaveSettings();

        // A local build never takes the installed copy's place.
        if (_services.IsDemo || StartupOptions.Current.DataFolder is not null || !_services.Updates.IsInstalled)
        {
            ShowMessage("Start with Windows only works in the installed Resonate.", InfoBarSeverity.Informational);
            return;
        }

        FollowStartWithWindows();
    }

    /// <summary>Puts Resonate in Windows' list of programs to start at sign-in (this user only), or takes it out.</summary>
    private void FollowStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (_services.Settings.StartWithWindows && Environment.ProcessPath is { } exe)
            {
                key.SetValue(RunValue, $"\"{exe}\" --background");
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ShowMessage("Windows did not let Resonate change its sign-in programs.", InfoBarSeverity.Warning);
        }
    }

    /// <summary>Started with Windows (--background): minimised to the taskbar.</summary>
    internal void GoToBackground() => _presenter?.Minimize();

    // Export history

    /// <summary>Saves the listening history as a CSV file where the user picks.</summary>
    internal async Task ExportHistoryAsync()
    {
        string? path;
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = $"Resonate listening history {DateTime.Now:yyyy-MM-dd}",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add("Spreadsheet (CSV)", [".csv"]);
            path = (await picker.PickSaveFileAsync())?.Path;
        }
        catch (COMException)
        {
            ShowMessage("Windows could not open the save window.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // All of it, or the last days the user picked.
        var days = _services.Settings.HistoryExportDays;
        var since = days > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : DateTimeOffset.MinValue;
        var plays = _services.Home.History.Plays.Where(p => p.PlayedAt >= since).ToList();
        try
        {
            await Task.Run(() => File.WriteAllText(path, HistoryCsv.Write(plays), new UTF8Encoding(true)));
            ShowMessage($"Saved {plays.Count} plays.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("The file could not be saved there.", InfoBarSeverity.Warning);
        }
    }
}

/// <summary>Where Now playing file writes when the user has not picked a file.</summary>
internal static class NowPlayingFile
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Resonate", "Now playing.txt");
}

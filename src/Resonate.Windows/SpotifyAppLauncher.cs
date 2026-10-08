using System.Diagnostics;
using Resonate.Spotify.Playback;
using Resonate.Windows.Interop;
using Windows.Management.Deployment;

namespace Resonate.Windows;

/// <summary>
/// Finds the Spotify desktop app (installer or Microsoft Store version),
/// starts it in the background when it is not running, closes it, and
/// restarts it for settings it only reads when it starts.
/// </summary>
public sealed class SpotifyAppLauncher : ISpotifyAppLauncher, ISpotifyAppRestarter, IDisposable
{
    /// <summary>The Microsoft Store version's package family name.</summary>
    public const string StorePackageFamilyName = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";

    private const string ProcessName = "Spotify";

    /// <summary>How long Spotify gets to close by itself before it is ended.</summary>
    private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(6);

    /// <summary>How long ending its processes may take.</summary>
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(6);

    private static readonly int CurrentSessionId = GetCurrentSessionId();

    private readonly SemaphoreSlim _launching = new(1, 1);
    private readonly SpotifyBackground? _background;

    /// <param name="background">Hides Spotify's window once it appears; without it, the window is minimised instead.</param>
    public SpotifyAppLauncher(SpotifyBackground? background = null) => _background = background;

    /// <summary>Spotify runs for this Windows user (another signed-in user's Spotify does not play here).</summary>
    public bool IsRunning => SessionProcessIds().Count > 0;

    public Action? BeforeStart { get; set; }

    /// <summary>
    /// The folders where the two versions of Spotify keep their settings
    /// (each has a "Users" folder with one settings file per account).
    /// </summary>
    public static IReadOnlyList<string> SettingsFolders() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            StorePackageFamilyName,
            "LocalState",
            "Spotify"),
    ];

    /// <summary>The installer version's program, when installed.</summary>
    public static string? FindInstallerVersion()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Whether either version of Spotify is installed.</summary>
    public static bool IsInstalled() => FindInstallerVersion() is not null || FindStorePackage() is not null;

    public async Task<SpotifyAppStatus> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        await _launching.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return IsRunning ? SpotifyAppStatus.Running : await StartLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _launching.Release();
        }
    }

    public async Task<SpotifyRestartStatus> RestartAsync(Action whileClosed, CancellationToken cancellationToken)
    {
        await _launching.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await CloseLockedAsync(cancellationToken).ConfigureAwait(false))
            {
                return SpotifyRestartStatus.CouldNotClose;
            }

            whileClosed();
            return await StartLockedAsync(cancellationToken).ConfigureAwait(false) switch
            {
                SpotifyAppStatus.Started or SpotifyAppStatus.Running => SpotifyRestartStatus.Restarted,
                SpotifyAppStatus.NotInstalled => SpotifyRestartStatus.NotInstalled,
                _ => SpotifyRestartStatus.CouldNotStart,
            };
        }
        finally
        {
            _launching.Release();
        }
    }

    public async Task<bool> CloseAsync(CancellationToken cancellationToken)
    {
        await _launching.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CloseLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _launching.Release();
        }
    }

    public void Dispose() => _launching.Dispose();

    /// <summary>Starts Spotify in the background. Only while holding <see cref="_launching"/>.</summary>
    private async Task<SpotifyAppStatus> StartLockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (FindInstallerVersion() is { } exe)
            {
                BeforeStart?.Invoke();

                // "--minimized" is what Spotify's own "start minimised" setting
                // uses; a hidden start window keeps the first frame off screen.
                using var started = Process.Start(new ProcessStartInfo(exe, "--minimized")
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                });
                KeepOutOfTheWay();
                return SpotifyAppStatus.Started;
            }

            if (FindStorePackage() is { } package)
            {
                BeforeStart?.Invoke();
                var entries = await package.GetAppListEntriesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                if (entries.Count > 0 && await entries[0].LaunchAsync().AsTask(cancellationToken).ConfigureAwait(false))
                {
                    KeepOutOfTheWay();
                    return SpotifyAppStatus.Started;
                }

                return SpotifyAppStatus.Failed;
            }

            return SpotifyAppStatus.NotInstalled;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or IOException)
        {
            return SpotifyAppStatus.Failed;
        }
    }

    private void KeepOutOfTheWay()
    {
        if (_background is { KeepHidden: true })
        {
            _background.WatchClosely();
        }
        else
        {
            _ = KeepMinimizedAsync(CancellationToken.None);
        }
    }

    private static global::Windows.ApplicationModel.Package? FindStorePackage()
    {
        try
        {
            // An empty user SID means the current user; this needs no special rights.
            return new PackageManager().FindPackagesForUser(string.Empty, StorePackageFamilyName).FirstOrDefault();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Spotify may still open its window (the Store version has no
    /// "minimised" switch). Minimise it without taking focus from Resonate.
    /// </summary>
    private static async Task KeepMinimizedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                foreach (var process in processes)
                {
                    var window = process.MainWindowHandle;
                    if (window != 0 && User32.IsWindowVisible(window) && !User32.IsIconic(window))
                    {
                        User32.ShowWindowAsync(window, User32.SwShowMinNoActive);
                        return;
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Asks Spotify to close, as its window's close button would, and ends
    /// its processes if they are still there after a few seconds (for example
    /// when Spotify is set to minimise to the tray when closed). True once
    /// every Spotify process of this Windows session is gone. Only while
    /// holding <see cref="_launching"/>.
    /// </summary>
    private static async Task<bool> CloseLockedAsync(CancellationToken cancellationToken)
    {
        var running = SessionProcessIds();
        if (running.Count == 0)
        {
            return true;
        }

        foreach (var window in MainWindows(running))
        {
            WindowMessages.PostMessage(window, WindowMessages.WmClose, 0, 0);
        }

        if (await WaitUntilClosedAsync(CloseGracePeriod, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == CurrentSessionId)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Already gone, or not Resonate's to end; checked below.
                }
            }
        }

        return await WaitUntilClosedAsync(EndTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> WaitUntilClosedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (SessionProcessIds().Count > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Spotify's processes in this Windows session (another signed-in user's Spotify is left alone).</summary>
    private static HashSet<uint> SessionProcessIds()
    {
        var ids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == CurrentSessionId)
                    {
                        ids.Add((uint)process.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // It exited meanwhile.
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Spotify's main windows: top-level Chromium windows with a title and no
    /// owner, hidden ones included (Resonate usually keeps Spotify's window
    /// hidden). Helper windows, which also have titles, are left alone.
    /// </summary>
    private static List<nint> MainWindows(HashSet<uint> processIds)
    {
        var found = new List<nint>();
        nint window = 0;
        while ((window = Windowing.FindWindowEx(0, window, null, null)) != 0)
        {
            Windowing.GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId)
                && Windowing.GetWindow(window, Windowing.GwOwner) == 0
                && Windowing.GetWindowTextLength(window) > 0
                && WindowMessages.ClassName(window).StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
            {
                found.Add(window);
            }
        }

        return found;
    }

    private static int GetCurrentSessionId()
    {
        using var current = Process.GetCurrentProcess();
        return current.SessionId;
    }
}

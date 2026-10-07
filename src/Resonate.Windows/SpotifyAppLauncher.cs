using System.Diagnostics;
using Resonate.Spotify.Playback;
using Resonate.Windows.Interop;
using Windows.Management.Deployment;

namespace Resonate.Windows;

/// <summary>
/// Finds the Spotify desktop app (installer or Microsoft Store version) and
/// starts it in the background when it is not running.
/// </summary>
public sealed class SpotifyAppLauncher : ISpotifyAppLauncher, IDisposable
{
    /// <summary>The Microsoft Store version's package family name.</summary>
    public const string StorePackageFamilyName = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";

    private const string ProcessName = "Spotify";

    private readonly SemaphoreSlim _launching = new(1, 1);

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
    }

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
            if (IsRunning)
            {
                return SpotifyAppStatus.Running;
            }

            if (FindInstallerVersion() is { } exe)
            {
                // "--minimized" is what Spotify's own "start minimised" setting uses.
                using var started = Process.Start(new ProcessStartInfo(exe, "--minimized")
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                });
                _ = KeepMinimizedAsync(CancellationToken.None);
                return SpotifyAppStatus.Started;
            }

            if (FindStorePackage() is { } package)
            {
                var entries = await package.GetAppListEntriesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                if (entries.Count > 0 && await entries[0].LaunchAsync().AsTask(cancellationToken).ConfigureAwait(false))
                {
                    _ = KeepMinimizedAsync(CancellationToken.None);
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
        finally
        {
            _launching.Release();
        }
    }

    public void Dispose() => _launching.Dispose();

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
}

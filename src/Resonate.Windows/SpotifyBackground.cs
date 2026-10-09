using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Resonate.Spotify.Playback;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Keeps the Spotify app running in the background: its window is hidden
/// (so it has no taskbar button) unless the user is looking at it, and while
/// it is hidden the processes that only draw that window run in Windows'
/// efficiency mode with their unused memory released. Spotify's main process
/// and its audio service are never touched, so playback is unaffected.
/// </summary>
public sealed class SpotifyBackground : ISpotifyAppWindow, IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CloseWatchInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SavingsInterval = TimeSpan.FromSeconds(20);

    // The renderer and GPU process grow back while music plays; their unused memory is handed back this often.
    private static readonly TimeSpan TrimInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ShowGracePeriod = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, bool> _isSpotify = [];
    private readonly HashSet<uint> _saving = [];
    private readonly HashSet<nint> _hiddenByUs = [];
    private DateTime _processCacheClearedAt = DateTime.UtcNow;
    private DateTime _nextSavingsPass;
    private DateTime _nextTrim;
    private DateTime _doNotHideUntil;
    private int _closeWatchTicks;
    private volatile bool _keepHidden = true;
    private volatile bool _saveResources = true;
    private volatile bool _enabled = true;
    private bool _started;

    // Completed to wake the watcher when it is enabled again.
    private TaskCompletionSource _enabledAgain = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (value)
            {
                _enabled = true;
                Volatile.Read(ref _enabledAgain).TrySetResult();
                return;
            }

            // A fresh signal before the flag, so the watcher never waits on one already used.
            Volatile.Write(ref _enabledAgain, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _enabled = false;

            // Give back what Resonate changed, off the caller's thread (it opens processes).
            _ = Task.Run(() =>
            {
                try
                {
                    RestoreHiddenWindows();
                    RestoreSavings();
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
                {
                    // A window or process went away meanwhile: nothing left to give back.
                }
            });
        }
    }

    public bool KeepHidden
    {
        get => _keepHidden;
        set
        {
            _keepHidden = value;
            if (!value)
            {
                RestoreHiddenWindows();
            }
        }
    }

    public bool SaveResources
    {
        get => _saveResources;
        set
        {
            _saveResources = value;
            if (!value)
            {
                RestoreSavings();
            }
        }
    }

    /// <summary>Starts watching in the background.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = Task.Run(WatchAsync);
    }

    /// <summary>
    /// Checks often for a while, for example right after Spotify starts, so
    /// its window is hidden before it is noticed.
    /// </summary>
    public void WatchClosely() =>
        Interlocked.Exchange(ref _closeWatchTicks, (int)(TimeSpan.FromSeconds(15) / CloseWatchInterval));

    public bool ShowSpotify()
    {
        // Full speed while the user looks at it.
        RestoreSavings();

        // Until it is in front, the watcher would otherwise hide it again.
        _doNotHideUntil = DateTime.UtcNow + ShowGracePeriod;

        var window = FindSpotifyWindow();
        if (window == 0)
        {
            // Not running (or no window yet): opening a Spotify link starts it in front.
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Win32Exception)
            {
                // Nothing opens Spotify links: Spotify is not installed.
                return false;
            }
        }

        User32.ShowWindowAsync(window, Windowing.SwRestore);
        Windowing.SetForegroundWindow(window);
        return true;
    }

    public void Dispose()
    {
        _stopping.Cancel();

        // Leave Spotify hidden (it keeps playing), but at full speed again.
        RestoreSavings();
    }

    private async Task WatchAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var fast = Interlocked.Decrement(ref _closeWatchTicks) >= 0;
            try
            {
                if (!_enabled)
                {
                    // "Spotify Web API only": the Spotify app is left alone, and nothing is checked.
                    await Volatile.Read(ref _enabledAgain).Task.WaitAsync(_stopping.Token).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(fast ? CloseWatchInterval : WatchInterval, _stopping.Token).ConfigureAwait(false);
                if (_enabled)
                {
                    Tick();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
            {
                // A process or window went away mid-check; the next tick starts fresh.
            }
        }
    }

    private void Tick()
    {
        // Both switched off in Settings: nothing to hide or slow down, so no need to look at every window.
        if (!_keepHidden && !_saveResources)
        {
            return;
        }

        var foreground = Windowing.GetForegroundWindow();
        var onScreen = false;
        foreach (var window in SpotifyTaskbarWindows(visibleOnly: true))
        {
            if (window == foreground)
            {
                // The user is looking at Spotify: leave it alone until they switch away.
                onScreen = true;
                continue;
            }

            if (_keepHidden && DateTime.UtcNow >= _doNotHideUntil)
            {
                User32.ShowWindowAsync(window, Windowing.SwHide);
                lock (_gate)
                {
                    _hiddenByUs.Add(window);
                }
            }
            else if (!User32.IsIconic(window))
            {
                // Shown on purpose (keeping hidden is off) and not minimised.
                onScreen = true;
            }
        }

        if (onScreen)
        {
            RestoreSavings();
        }
        else if (_saveResources && DateTime.UtcNow >= _nextSavingsPass)
        {
            _nextSavingsPass = DateTime.UtcNow + SavingsInterval;
            ApplySavings();
        }
    }

    /// <summary>Spotify's top-level windows that get a taskbar button (not menus, tooltips or helpers).</summary>
    private List<nint> SpotifyTaskbarWindows(bool visibleOnly)
    {
        var found = new List<nint>();
        nint window = 0;
        while ((window = Windowing.FindWindowEx(0, window, null, null)) != 0)
        {
            if (visibleOnly && !User32.IsWindowVisible(window))
            {
                continue;
            }

            Windowing.GetWindowThreadProcessId(window, out var processId);
            if (!IsSpotifyProcess(processId) || !GetsTaskbarButton(window))
            {
                continue;
            }

            found.Add(window);
        }

        return found;
    }

    private nint FindSpotifyWindow()
    {
        nint[] hidden;
        lock (_gate)
        {
            // Spotify may have restarted since; forget windows that are gone.
            _hiddenByUs.RemoveWhere(w => !Windowing.IsWindow(w));
            hidden = [.. _hiddenByUs];
        }

        foreach (var window in hidden)
        {
            // Window handles are reused, so check it still belongs to Spotify.
            Windowing.GetWindowThreadProcessId(window, out var processId);
            if (IsSpotifyProcess(processId))
            {
                return window;
            }
        }

        // Spotify's main window has a title (the song, or "Spotify Premium").
        return SpotifyTaskbarWindows(visibleOnly: false).FirstOrDefault(w => Windowing.GetWindowTextLength(w) > 0);
    }

    private static bool GetsTaskbarButton(nint window)
    {
        var style = (long)Windowing.GetWindowLongPtr(window, Windowing.GwlExStyle);
        if ((style & Windowing.WsExAppWindow) != 0)
        {
            return true;
        }

        return (style & Windowing.WsExToolWindow) == 0 && Windowing.GetWindow(window, Windowing.GwOwner) == 0;
    }

    private bool IsSpotifyProcess(uint processId)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _processCacheClearedAt > ProcessCacheLifetime)
            {
                // Process IDs are reused; forget old answers now and then.
                _isSpotify.Clear();
                _processCacheClearedAt = DateTime.UtcNow;
            }

            if (_isSpotify.TryGetValue(processId, out var known))
            {
                return known;
            }
        }

        var isSpotify = ProcessImage.IsSpotify(processId);
        lock (_gate)
        {
            _isSpotify[processId] = isSpotify;
        }

        return isSpotify;
    }

    private void ApplySavings()
    {
        var trim = DateTime.UtcNow >= _nextTrim;
        if (trim)
        {
            _nextTrim = DateTime.UtcNow + TrimInterval;
        }

        var processes = Process.GetProcessesByName("Spotify");
        try
        {
            var alive = processes.Select(p => (uint)p.Id).ToHashSet();
            lock (_gate)
            {
                _saving.IntersectWith(alive);
            }

            foreach (var processId in alive)
            {
                bool saving;
                lock (_gate)
                {
                    saving = _saving.Contains(processId);
                }

                if (saving)
                {
                    if (trim)
                    {
                        TrimWorkingSet(processId);
                    }

                    continue;
                }

                if (!SpotifyProcesses.MaySaveResources(SpotifyProcesses.Classify(CommandLine(processId))))
                {
                    continue;
                }

                if (SetEfficiencyMode(processId, on: true))
                {
                    lock (_gate)
                    {
                        _saving.Add(processId);
                    }
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

    private void RestoreSavings()
    {
        uint[] saving;
        lock (_gate)
        {
            saving = [.. _saving];
            _saving.Clear();
        }

        foreach (var processId in saving)
        {
            SetEfficiencyMode(processId, on: false);
        }

        // Look again soon after the user is done with Spotify.
        _nextSavingsPass = DateTime.UtcNow + SavingsInterval;
    }

    private void RestoreHiddenWindows()
    {
        nint[] hidden;
        lock (_gate)
        {
            hidden = [.. _hiddenByUs];
            _hiddenByUs.Clear();
        }

        foreach (var window in hidden.Where(Windowing.IsWindow))
        {
            // Back on the taskbar, minimised, without taking focus.
            User32.ShowWindowAsync(window, Windowing.SwShowMinNoActive);
        }
    }

    /// <summary>Hands a saving process's unused memory back to Windows (it pages back in as needed).</summary>
    private static void TrimWorkingSet(uint processId)
    {
        var process = Processes.OpenProcess(Processes.SetQuota | Processes.QueryLimitedInformation, false, processId);
        if (process != 0)
        {
            Processes.EmptyWorkingSet(process);
            Processes.CloseHandle(process);
        }
    }

    /// <summary>
    /// Efficiency mode, as Task Manager sets it: lowest priority and the
    /// power-saving (EcoQoS) execution speed; going in, the process's unused
    /// memory is also handed back to Windows.
    /// </summary>
    private static bool SetEfficiencyMode(uint processId, bool on)
    {
        var process = Processes.OpenProcess(Processes.SetInformation | Processes.SetQuota | Processes.QueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return false;
        }

        try
        {
            var throttling = new Processes.PowerThrottlingState
            {
                Version = Processes.PowerThrottlingCurrentVersion,
                ControlMask = Processes.PowerThrottlingExecutionSpeed,
                StateMask = on ? Processes.PowerThrottlingExecutionSpeed : 0,
            };
            var throttled = Processes.SetProcessInformation(
                process,
                Processes.ProcessPowerThrottling,
                ref throttling,
                (uint)Unsafe.SizeOf<Processes.PowerThrottlingState>());
            var prioritised = Processes.SetPriorityClass(process, on ? Processes.IdlePriorityClass : Processes.NormalPriorityClass);
            if (on)
            {
                Processes.EmptyWorkingSet(process);
            }

            return throttled || prioritised;
        }
        finally
        {
            Processes.CloseHandle(process);
        }
    }

    internal static unsafe string? CommandLine(uint processId)
    {
        var process = Processes.OpenProcess(Processes.QueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            Processes.NtQueryInformationProcess(process, Processes.ProcessCommandLineInformation, null, 0, out var needed);
            if (needed == 0 || needed > 1024 * 1024)
            {
                return null;
            }

            var buffer = NativeMemory.Alloc(needed);
            try
            {
                if (Processes.NtQueryInformationProcess(process, Processes.ProcessCommandLineInformation, buffer, needed, out _) < 0)
                {
                    return null;
                }

                var text = (Processes.UnicodeString*)buffer;
                return text->Buffer == 0 ? null : new string((char*)text->Buffer, 0, text->Length / 2);
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }
        finally
        {
            Processes.CloseHandle(process);
        }
    }
}

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>
/// The page Resonate's own player runs in with "Spotify Web API only"
/// (<see cref="OwnPlayer"/>): Spotify's Web Playback SDK in a WebView2 that
/// nobody sees. It is not part of the interface (Resonate never shows a
/// browser): its window is a message-only window, it opens nothing but
/// Assets/WebPlayer/player.html and the scripts Spotify's player loads, keeps
/// nothing on disk between runs (InPrivate), and allows no pop-ups,
/// downloads, developer tools or permissions other than playing sound.
/// WebView2 lives on the interface thread; every member may be called from
/// any thread and passes the work there.
/// </summary>
internal sealed partial class WebPlayerPage : IWebPlayerPage
{
    private const string HostName = "player.resonate.example";
    private const string PageUri = "https://" + HostName + "/player.html";

    /// <summary>A message-only window: "an invisible WebView" in WebView2's own words.</summary>
    private const ulong MessageOnlyWindow = unchecked((ulong)-3L);

    /// <summary>
    /// Plays without a click (nobody can click a hidden page), and keeps its
    /// timers running while hidden, so Spotify keeps hearing from the device
    /// when the music is paused for a long time.
    /// </summary>
    private const string BrowserArguments =
        "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-features=IntensiveWakeUpThrottling";

    /// <summary>Starting WebView2 and opening the page each get this long.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A page that does not answer for this long counts as failed; WebView2
    /// says so every few seconds, also when the PC is only busy for a moment.
    /// </summary>
    private static readonly TimeSpan UnresponsiveLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a closed page's WebView2 objects are kept: WebView2 crashed
    /// Resonate when they were let go while a page that had not finished
    /// opening was closed (9 October 2026, an access violation in
    /// Microsoft.Web.WebView2.Core.dll), so they outlive their page a while.
    /// </summary>
    private static readonly TimeSpan KeepClosed = TimeSpan.FromMinutes(5);

    // Closed pages' WebView2 objects and when they closed; on the interface thread.
    private static readonly List<(long At, object?[] Objects)> Closed = [];

    // The open page's WebView2 browser process, for the Home stage's visualizer.
    private static int _browserProcessId;

    private readonly DispatcherQueue _dispatcher;
    private readonly string _userDataFolder;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _web;
    private bool _marked;
    private int _ownProcessId;
    private string? _crashReports;
    private int _disposed;

    // On the interface thread: when the page stopped answering, and when WebView2 last said so.
    private long _unresponsiveSince;
    private long _unresponsiveLast;

    /// <param name="dispatcher">The interface thread's queue; WebView2 runs there.</param>
    /// <param name="userDataFolder">WebView2's own folder (it keeps no browsing data there, see InPrivate).</param>
    public WebPlayerPage(DispatcherQueue dispatcher, string userDataFolder)
    {
        _dispatcher = dispatcher;
        _userDataFolder = userDataFolder;
    }

    public event EventHandler<string>? MessageReceived;

    public event EventHandler<string>? Failed;

    /// <summary>The WebView2 runtime's version, once the page is open.</summary>
    public string? BrowserVersion { get; private set; }

    /// <summary>
    /// The WebView2 browser process of the page that is open (0 when none):
    /// Spotify's sound plays in its tree, which the Home stage's visualizer
    /// hears (SpotifySoundListener). Any thread.
    /// </summary>
    public static int BrowserProcessId => Volatile.Read(ref _browserProcessId);

    /// <summary>Told each step of opening the page, for CI's check and the playback log; never anything a message carries.</summary>
    public Action<string>? Trace { get; init; }

    /// <summary>
    /// How many runs of Resonate in a row ended while a page was open in
    /// <paramref name="userDataFolder"/> without closing it: Resonate itself
    /// ended unexpectedly then, maybe because of the page. Any thread.
    /// </summary>
    public static int UncleanEnds(string userDataFolder)
    {
        try
        {
            var file = MarkerFile(userDataFolder);
            if (!File.Exists(file))
            {
                return 0;
            }

            return int.TryParse(File.ReadAllText(file), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? Math.Max(1, count) : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Starts counting <see cref="UncleanEnds"/> again from none.</summary>
    public static void ForgetUncleanEnds(string userDataFolder)
    {
        try
        {
            File.Delete(MarkerFile(userDataFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Counted again next time.
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// CI's check (--web-player-check): opens the page, checks that protected
    /// audio works and that Spotify's player starts and turns down a made-up
    /// token, and says how it went in one line starting with "OK " or "FAIL ".
    /// </summary>
    public static async Task<string> CheckAsync(DispatcherQueue dispatcher, string userDataFolder, TimeSpan timeout, Action<string> trace)
    {
        var page = new WebPlayerPage(dispatcher, userDataFolder) { Trace = trace };

        // Says every 15 s whether the interface thread still answers, so a hang shows where it is.
        var watchdog = new Timer(
            _ =>
            {
                var answered = new ManualResetEventSlim();
                var queued = dispatcher.TryEnqueue(answered.Set);
                trace(queued && answered.Wait(TimeSpan.FromSeconds(2)) ? "still waiting; the interface thread answers" : "still waiting; the interface thread does not answer");
            },
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));

        // Both wait for what they started, so nothing is told after the outcome.
        await using var stopWatchdog = watchdog.ConfigureAwait(false);
        await using (page.ConfigureAwait(false))
        {
            var answer = new TaskCompletionSource<WebPlayerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            page.MessageReceived += (_, json) =>
            {
                if (WebPlayerMessage.Parse(json) is { Type: "check" } message)
                {
                    answer.TrySetResult(message);
                }
            };
            page.Failed += (_, what) => answer.TrySetException(new InvalidOperationException(what));

            using var cancel = new CancellationTokenSource(timeout);
            trace("opening the page");
            await page.LoadAsync(cancel.Token).ConfigureAwait(false);
            trace("the page listens; checking");
            page.Post(WebPlayerCommands.Check());
            var result = await answer.Task.WaitAsync(cancel.Token).ConfigureAwait(false);
            trace("the page answered; closing it");

            // A made-up token: Spotify's player started, reached Spotify and was turned down.
            var ok = result is { Widevine: "ok", Sdk: "authentication_error" };
            return $"{(ok ? "OK" : "FAIL")} WebView2 {page.BrowserVersion}; protected audio: {result.Widevine}; Spotify's player: {result.Sdk}";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await OpenAsync();
                opened.TrySetResult();
            }
            catch (Exception ex)
            {
                opened.TrySetException(ex);
            }
        }))
        {
            throw new ObjectDisposedException(nameof(WebPlayerPage), "Resonate is closing.");
        }

        await opened.Task.WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);
        await _loaded.Task.WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);
    }

    public void Post(string json)
    {
        if (!IsDisposed)
        {
            Send(json);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loaded.TrySetCanceled();
        if (_loaded.Task.IsCompletedSuccessfully)
        {
            // Spotify's player says goodbye first, so Spotify drops the device at once.
            Send(WebPlayerCommands.Stop());
            try
            {
                await _stopped.Task.WaitAsync(StopWait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Closing anyway.
            }
        }

        _dispatcher.TryEnqueue(Close);
    }

    /// <summary>Whether this computer has the WebView2 runtime (Windows 11 always does).</summary>
    private static string? FindRuntime()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException)
        {
            // C#/WinRT turns "no runtime" (ERROR_FILE_NOT_FOUND) into FileNotFoundException.
            return null;
        }
    }

    private async Task OpenAsync()
    {
        Trace?.Invoke("looking for the WebView2 runtime");
        if (FindRuntime() is not { } runtime)
        {
            throw new WebPlayerUnavailableException("The WebView2 runtime is not installed.");
        }

        Trace?.Invoke($"WebView2 runtime {runtime}; starting it");
        Directory.CreateDirectory(_userDataFolder);
        MarkOpen();
        CoreWebView2Environment environment;
        try
        {
            // Crash reports stay on this PC rather than going to Microsoft: they may hold the access token.
            environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                _userDataFolder,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArguments, IsCustomCrashReportingEnabled = true });
        }
        catch (FileNotFoundException ex)
        {
            throw new WebPlayerUnavailableException("The WebView2 runtime is not installed.", ex);
        }

        _environment = environment;
        BrowserVersion = environment.BrowserVersionString;
        _crashReports = environment.FailureReportFolderPath;
        DeleteCrashReports(_crashReports);

        Trace?.Invoke("started; making the hidden view");
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        if (IsDisposed)
        {
            throw new OperationCanceledException("The player was stopped while it opened.");
        }

        var controller = await environment.CreateCoreWebView2ControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle(MessageOnlyWindow),
            options);
        Trace?.Invoke("the hidden view exists; opening the page");
        _controller = controller;
        if (IsDisposed)
        {
            Close();
            throw new OperationCanceledException("The player was stopped while it opened.");
        }

        // Hidden for good: nothing is drawn, and sound plays as it does in a background tab.
        controller.IsVisible = false;
        var web = controller.CoreWebView2;
        _web = web;
        _ownProcessId = (int)web.BrowserProcessId;
        Volatile.Write(ref _browserProcessId, _ownProcessId);
        var settings = web.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        // No SmartScreen look-ups at Microsoft: Resonate talks only to Spotify here, and opens nothing but its own page.
        settings.IsReputationCheckingRequired = false;

        web.SetVirtualHostNameToFolderMapping(
            HostName,
            Path.Combine(AppContext.BaseDirectory, "Assets", "WebPlayer"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        web.WebMessageReceived += OnWebMessageReceived;
        web.NavigationStarting += OnNavigationStarting;
        web.NavigationCompleted += OnNavigationCompleted;
        web.NewWindowRequested += OnNewWindowRequested;
        web.DownloadStarting += OnDownloadStarting;
        web.PermissionRequested += OnPermissionRequested;
        web.ProcessFailed += OnProcessFailed;
        web.Navigate(PageUri);
    }

    private static void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e) =>
        e.Cancel = !string.Equals(e.Uri, PageUri, StringComparison.Ordinal);

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Trace?.Invoke($"page opened: {e.IsSuccess} ({e.WebErrorStatus})");
        if (!e.IsSuccess)
        {
            _loaded.TrySetException(new InvalidOperationException($"The player page did not open ({e.WebErrorStatus})."));
        }
    }

    private static void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private static void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs e) => e.Cancel = true;

    private static void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs e) =>
        e.State = e.PermissionKind == CoreWebView2PermissionKind.Autoplay
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only from the page itself, not from a frame Spotify's player opens.
        if (!string.Equals(e.Source, PageUri, StringComparison.Ordinal))
        {
            return;
        }

        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        _unresponsiveSince = 0;
        var type = WebPlayerMessage.Parse(json)?.Type;
        if (type != "state")
        {
            Trace?.Invoke($"the page says {type}");
        }

        switch (type)
        {
            case "loaded":
                _loaded.TrySetResult();
                return;
            case "stopped":
                _stopped.TrySetResult();
                return;
        }

        MessageReceived?.Invoke(this, json);
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive && !StaysUnresponsive())
        {
            return;
        }

        // Spotify's player runs in the page and in a frame of its own; WebView2 starts its helpers again by itself.
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            var what = $"The player's {e.ProcessFailedKind} ({e.Reason}).";
            Trace?.Invoke(what);
            _loaded.TrySetException(new InvalidOperationException(what));
            Failed?.Invoke(this, what);
        }
    }

    /// <summary>
    /// Whether the page has not answered for <see cref="UnresponsiveLimit"/>:
    /// a busy moment passes, and the music plays on meanwhile.
    /// </summary>
    private bool StaysUnresponsive()
    {
        var now = Environment.TickCount64;

        // A new spell when WebView2 had stopped saying so (it repeats every few seconds while it lasts).
        if (_unresponsiveSince == 0 || now - _unresponsiveLast > 15_000)
        {
            _unresponsiveSince = now;
        }

        _unresponsiveLast = now;
        return now - _unresponsiveSince >= (long)UnresponsiveLimit.TotalMilliseconds;
    }

    private void Send(string json)
    {
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                _controller?.CoreWebView2.PostWebMessageAsString(json);
            }
            catch (COMException)
            {
                // The page is gone; Failed says so.
            }
        });
    }

    /// <summary>Deletes the crash reports a page left (they may hold the access token); a file still in use stays until next time.</summary>
    private static void DeleteCrashReports(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Next time.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next time.
        }
    }

    /// <summary>
    /// Closes the page, once: its handlers go first, so WebView2 tells this
    /// page nothing more, and its WebView2 objects are kept a while (see
    /// <see cref="KeepClosed"/>). On the interface thread.
    /// </summary>
    private void Close()
    {
        var web = _web;
        var controller = _controller;
        var environment = _environment;
        _web = null;
        _controller = null;
        _environment = null;
        if (web is not null)
        {
            try
            {
                web.WebMessageReceived -= OnWebMessageReceived;
                web.NavigationStarting -= OnNavigationStarting;
                web.NavigationCompleted -= OnNavigationCompleted;
                web.NewWindowRequested -= OnNewWindowRequested;
                web.DownloadStarting -= OnDownloadStarting;
                web.PermissionRequested -= OnPermissionRequested;
                web.ProcessFailed -= OnProcessFailed;
            }
            catch (COMException)
            {
                // Its browser is gone, and with it every handler.
            }
        }

        if (controller is not null)
        {
            try
            {
                controller.Close();
            }
            catch (COMException)
            {
                // Already closed by its browser.
            }
        }

        if (web is not null || controller is not null || environment is not null)
        {
            var now = Environment.TickCount64;
            Closed.RemoveAll(closed => now - closed.At > (long)KeepClosed.TotalMilliseconds);
            Closed.Add((now, [web, controller, environment]));
        }

        // Only this page's: a newer page may already have taken its place.
        Interlocked.CompareExchange(ref _browserProcessId, 0, _ownProcessId);
        DeleteCrashReports(_crashReports);
        if (_marked)
        {
            _marked = false;
            ForgetUncleanEnds(_userDataFolder);
        }
    }

    private static string MarkerFile(string userDataFolder) => Path.Combine(userDataFolder, "open.count");

    /// <summary>Counts this run as one that ended with a page open, until <see cref="Close"/> says otherwise.</summary>
    private void MarkOpen()
    {
        try
        {
            File.WriteAllText(MarkerFile(_userDataFolder), (UncleanEnds(_userDataFolder) + 1).ToString(CultureInfo.InvariantCulture));
            _marked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not counted.
        }
    }
}

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

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

    private readonly DispatcherQueue _dispatcher;
    private readonly string _userDataFolder;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CoreWebView2Controller? _controller;
    private int _disposed;

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

    /// <summary>Told each step of opening the page, for CI's check; never anything a message carries.</summary>
    public Action<string>? Trace { get; init; }

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
        using var watchdog = new Timer(
            _ =>
            {
                var answered = new ManualResetEventSlim();
                var queued = dispatcher.TryEnqueue(answered.Set);
                trace(queued && answered.Wait(TimeSpan.FromSeconds(2)) ? "still waiting; the interface thread answers" : "still waiting; the interface thread does not answer");
            },
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));
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

        await opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        catch (COMException)
        {
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
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            string.Empty,
            _userDataFolder,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArguments });
        BrowserVersion = environment.BrowserVersionString;

        Trace?.Invoke("started; making the hidden view");
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        var controller = await environment.CreateCoreWebView2ControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle(MessageOnlyWindow),
            options);
        Trace?.Invoke("the hidden view exists; opening the page");
        if (IsDisposed)
        {
            controller.Close();
            throw new OperationCanceledException("The player was stopped while it opened.");
        }

        _controller = controller;

        // Hidden for good: nothing is drawn, and sound plays as it does in a background tab.
        controller.IsVisible = false;
        var web = controller.CoreWebView2;
        var settings = web.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        web.SetVirtualHostNameToFolderMapping(
            HostName,
            Path.Combine(AppContext.BaseDirectory, "Assets", "WebPlayer"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        web.WebMessageReceived += OnWebMessageReceived;
        web.NavigationStarting += (_, e) => e.Cancel = !string.Equals(e.Uri, PageUri, StringComparison.Ordinal);
        web.NavigationCompleted += (_, e) =>
        {
            Trace?.Invoke($"page opened: {e.IsSuccess} ({e.WebErrorStatus})");
            if (!e.IsSuccess)
            {
                _loaded.TrySetException(new InvalidOperationException($"The player page did not open ({e.WebErrorStatus})."));
            }
        };
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.PermissionRequested += (_, e) => e.State = e.PermissionKind == CoreWebView2PermissionKind.Autoplay
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
        web.ProcessFailed += OnProcessFailed;
        web.Navigate(PageUri);
    }

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

        var type = WebPlayerMessage.Parse(json)?.Type;
        Trace?.Invoke($"the page says {type}");
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

    private void Close()
    {
        if (_controller is { } controller)
        {
            _controller = null;
            controller.Close();
        }
    }
}

using Resonate.Spotify.Auth;

namespace Resonate.Spotify.Playback;

/// <summary>What Resonate's own player is doing (see <see cref="OwnPlayer"/>).</summary>
public enum OwnPlayerStatus
{
    /// <summary>Not running: Windows' media controls are in use, it is switched off, or nobody is signed in.</summary>
    Off,

    /// <summary>Opening Spotify's player and connecting it.</summary>
    Starting,

    /// <summary>Spotify lists it as a device, so music can play on this computer.</summary>
    Ready,

    /// <summary>The sign-in lacks the permission to play, or Spotify turned it down: sign in again.</summary>
    NeedsSignIn,

    /// <summary>Spotify only lets Premium accounts play here.</summary>
    NeedsPremium,

    /// <summary>This computer cannot run it (no WebView2 runtime, or no protected-audio support).</summary>
    Unsupported,

    /// <summary>It stopped working (no internet, a crash) and tries again by itself.</summary>
    Failed,
}

/// <summary>The device of Resonate's own player, as <see cref="WebDeviceResolver"/> sees it.</summary>
public interface IOwnDevice
{
    /// <summary>The name Spotify lists the device under.</summary>
    string Name { get; }

    /// <summary>Its Spotify Connect device ID while it is ready, else null.</summary>
    string? DeviceId { get; }

    /// <summary>
    /// The device ID, waiting up to <paramref name="timeout"/> while the
    /// player is still starting; null at once when it is not starting.
    /// </summary>
    Task<string?> WaitForDeviceAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Resonate's own player for "Spotify Web API only" (the owner's request,
/// 8 October 2026): Spotify's Web Playback SDK in a hidden page
/// (<see cref="IWebPlayerPage"/>), which Spotify lists as a Connect device.
/// Resonate plays on it through the Web API like on any other device, so
/// music sounds on this computer with the Spotify app closed, at the web
/// player's quality rather than Lossless. Starting and stopping run one
/// after another; the page's messages arrive on any thread. After a failure
/// it tries again by itself, waiting longer each time.
/// </summary>
public sealed class OwnPlayer : IOwnDevice, IDisposable
{
    public const string DefaultName = "Resonate";

    /// <summary>Spotify's player gets this long to connect before it counts as failed.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(45);

    /// <summary>The wait before trying again after the first, second and later failures in a row.</summary>
    internal static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1)];

    private readonly Func<IWebPlayerPage> _createPage;
    private readonly IAccessTokenSource _tokens;
    private readonly Func<bool> _canPlay;
    private readonly TimeProvider _time;
    private readonly SerialWorker _lane = new();
    private readonly CancellationTokenSource _disposing = new();
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private bool _wanted;
    private bool _disposed;
    private IWebPlayerPage? _page;
    private CancellationTokenSource? _session;
    private OwnPlayerStatus _status;
    private string? _deviceId;
    private string? _lastToken;
    private string? _rejectedToken;
    private bool _triedNewToken;
    private int _failures;
    private int _stops;
    private int _connectAttempt;
    private TaskCompletionSource<string?> _deviceWaiter = NewWaiter();

    /// <param name="createPage">Makes a new page for each start (the hidden WebView2 on Windows).</param>
    /// <param name="tokens">Access tokens for Spotify's player; the sign-in needs the "streaming" permission.</param>
    /// <param name="canPlay">False when the sign-in lacks the permission to play; then it asks to sign in again instead of starting.</param>
    public OwnPlayer(
        Func<IWebPlayerPage> createPage,
        IAccessTokenSource tokens,
        Func<bool>? canPlay = null,
        string name = DefaultName,
        TimeProvider? time = null)
    {
        _createPage = createPage;
        _tokens = tokens;
        _canPlay = canPlay ?? (() => true);
        Name = name;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on any thread after <see cref="Status"/> changed.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Raised on any thread when Spotify's player says what it plays changed
    /// (a new song, paused, resumed), so the interface can ask Spotify at
    /// once rather than at the next poll.
    /// </summary>
    public event EventHandler? PlaybackChanged;

    public string Name { get; }

    public OwnPlayerStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public string? DeviceId
    {
        get
        {
            lock (_gate)
            {
                return _status == OwnPlayerStatus.Ready ? _deviceId : null;
            }
        }
    }

    /// <summary>
    /// Starts the player unless it runs already, or says the sign-in needs
    /// renewing. Also starts it again after it stopped for a reason
    /// (<see cref="OwnPlayerStatus.NeedsSignIn"/> and the rest).
    /// </summary>
    public Task StartAsync()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _wanted = true;
            changed = _page is null && SetStatusLocked(OwnPlayerStatus.Starting);
        }

        if (changed)
        {
            RaiseStatusChanged();
        }

        return _lane.Enqueue(StartInLaneAsync);
    }

    /// <summary>Stops the player and closes its page; Spotify drops the device.</summary>
    public Task StopAsync()
    {
        lock (_gate)
        {
            _wanted = false;
            _stops++;
        }

        return _lane.Enqueue(_ => StopInLaneAsync());
    }

    public async Task<string?> WaitForDeviceAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<string?> waiter;
        lock (_gate)
        {
            if (_status == OwnPlayerStatus.Ready)
            {
                return _deviceId;
            }

            if (_status != OwnPlayerStatus.Starting)
            {
                return null;
            }

            waiter = _deviceWaiter.Task;
        }

        try
        {
            return await waiter.WaitAsync(timeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        IWebPlayerPage? page;
        CancellationTokenSource? session;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _wanted = false;
            page = _page;
            _page = null;
            session = _session;
            _session = null;
            _deviceId = null;
            SetStatusLocked(OwnPlayerStatus.Off);
        }

        _lane.Dispose();

        // Cancel without disposing: a start or a token request may still hold them.
        _disposing.Cancel();
        session?.Cancel();
        if (page is not null)
        {
            _ = CloseQuietlyAsync(page);
        }
    }

    private static TaskCompletionSource<string?> NewWaiter() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task CloseQuietlyAsync(IWebPlayerPage page)
    {
        try
        {
            await page.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing anyway.
        }
    }

    private async Task StartInLaneAsync(CancellationToken stopping)
    {
        lock (_gate)
        {
            if (_disposed || !_wanted || _page is not null)
            {
                return;
            }
        }

        if (!_canPlay())
        {
            SetStatus(OwnPlayerStatus.NeedsSignIn);
            return;
        }

        var page = _createPage();
        var session = CancellationTokenSource.CreateLinkedTokenSource(stopping, _disposing.Token);
        int attempt;
        lock (_gate)
        {
            _page = page;
            _session = session;
            _deviceId = null;
            _triedNewToken = false;
            _rejectedToken = null;
            SetStatusLocked(OwnPlayerStatus.Starting);
            attempt = ++_connectAttempt;
        }

        RaiseStatusChanged();
        page.MessageReceived += (_, json) => OnMessage(page, json);
        page.Failed += (_, _) => _ = _lane.Enqueue(_ => FailInLaneAsync(page));

        try
        {
            await page.LoadAsync(session.Token).ConfigureAwait(false);
        }
        catch (WebPlayerUnavailableException)
        {
            await CloseInLaneAsync(page, OwnPlayerStatus.Unsupported).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            await FailInLaneAsync(page).ConfigureAwait(false);
            return;
        }

        page.Post(WebPlayerCommands.Start(Name, volume: 1));
        _ = WatchConnectAsync(page, attempt, session.Token);
    }

    private async Task StopInLaneAsync()
    {
        IWebPlayerPage? page;
        bool changed = false;
        lock (_gate)
        {
            _failures = 0;
            page = _page;
            if (page is null)
            {
                changed = SetStatusLocked(OwnPlayerStatus.Off);
            }
        }

        if (page is not null)
        {
            await CloseInLaneAsync(page, OwnPlayerStatus.Off).ConfigureAwait(false);
        }
        else if (changed)
        {
            RaiseStatusChanged();
        }
    }

    /// <summary>Closes <paramref name="page"/> if it is still the one in use, and says why with <paramref name="status"/>.</summary>
    private async Task CloseInLaneAsync(IWebPlayerPage page, OwnPlayerStatus status)
    {
        CancellationTokenSource? session;
        bool changed;
        lock (_gate)
        {
            if (!ReferenceEquals(_page, page))
            {
                return;
            }

            _page = null;
            session = _session;
            _session = null;
            _deviceId = null;
            changed = SetStatusLocked(status);
        }

        if (session is not null)
        {
            // Cancelled, not disposed: a token request may still hold it.
            await session.CancelAsync().ConfigureAwait(false);
        }

        if (changed)
        {
            RaiseStatusChanged();
        }

        await CloseQuietlyAsync(page).ConfigureAwait(false);
    }

    /// <summary>Closes a page that stopped working and tries again later, waiting longer after each failure in a row.</summary>
    private async Task FailInLaneAsync(IWebPlayerPage page)
    {
        int failures;
        int stops;
        lock (_gate)
        {
            if (!ReferenceEquals(_page, page))
            {
                return;
            }

            failures = ++_failures;
            stops = _stops;
        }

        // The wait starts first; it checks again when it ends.
        _ = RetryAsync(RetryDelays[Math.Min(failures, RetryDelays.Length) - 1], stops);
        await CloseInLaneAsync(page, OwnPlayerStatus.Failed).ConfigureAwait(false);
    }

    private async Task RetryAsync(TimeSpan delay, int stops)
    {
        try
        {
            await Task.Delay(delay, _time, _disposing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool again;
        lock (_gate)
        {
            // Not when it was stopped (or stopped and started) meanwhile.
            again = _wanted && !_disposed && _page is null && _status == OwnPlayerStatus.Failed && _stops == stops;
        }

        if (again)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Spotify's player that never connects counts as failed.</summary>
    private async Task WatchConnectAsync(IWebPlayerPage page, int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConnectTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool stuck;
        lock (_gate)
        {
            stuck = ReferenceEquals(_page, page) && _status == OwnPlayerStatus.Starting && _connectAttempt == attempt;
        }

        if (stuck)
        {
            _ = _lane.Enqueue(_ => FailInLaneAsync(page));
        }
    }

    private void OnMessage(IWebPlayerPage page, string json)
    {
        if (WebPlayerMessage.Parse(json) is not { } message)
        {
            return;
        }

        switch (message.Type)
        {
            case "token":
                _ = SendTokenAsync(page);
                break;
            case "ready" when !string.IsNullOrEmpty(message.DeviceId):
                OnReady(page, message.DeviceId);
                break;
            case "notReady":
                OnNotReady(page);
                break;
            case "error":
                OnError(page, message.Kind);
                break;
            case "state":
                if (IsCurrent(page))
                {
                    PlaybackChanged?.Invoke(this, EventArgs.Empty);
                }

                break;
        }
    }

    private void OnReady(IWebPlayerPage page, string deviceId)
    {
        bool changed;
        lock (_gate)
        {
            if (!ReferenceEquals(_page, page))
            {
                return;
            }

            _deviceId = deviceId;
            _failures = 0;
            _triedNewToken = false;
            changed = SetStatusLocked(OwnPlayerStatus.Ready);
            if (!changed)
            {
                // Ready again under a new device ID.
                _deviceWaiter.TrySetResult(deviceId);
            }
        }

        RaiseStatusChanged();
    }

    /// <summary>Spotify's player lost its connection; it connects again by itself.</summary>
    private void OnNotReady(IWebPlayerPage page)
    {
        int attempt;
        CancellationToken token;
        lock (_gate)
        {
            if (!ReferenceEquals(_page, page) || _session is null || !SetStatusLocked(OwnPlayerStatus.Starting))
            {
                return;
            }

            _deviceId = null;
            attempt = ++_connectAttempt;
            token = _session.Token;
        }

        RaiseStatusChanged();
        _ = WatchConnectAsync(page, attempt, token);
    }

    private void OnError(IWebPlayerPage page, string? kind)
    {
        if (!IsCurrent(page))
        {
            return;
        }

        switch (kind)
        {
            case "initialization":
                // No protected-audio support in this computer's WebView2.
                _ = _lane.Enqueue(_ => CloseInLaneAsync(page, OwnPlayerStatus.Unsupported));
                break;
            case "authentication":
                bool retry;
                lock (_gate)
                {
                    retry = !_triedNewToken;
                    _triedNewToken = true;
                    _rejectedToken = _lastToken;
                }

                if (retry)
                {
                    // Once with a renewed token; the sign-in may only have expired.
                    page.Post(WebPlayerCommands.Reconnect());
                }
                else
                {
                    _ = _lane.Enqueue(_ => CloseInLaneAsync(page, OwnPlayerStatus.NeedsSignIn));
                }

                break;
            case "account":
                _ = _lane.Enqueue(_ => CloseInLaneAsync(page, OwnPlayerStatus.NeedsPremium));
                break;
            case "playback":
                // One song could not play; Spotify moves on, and the interface asks what plays now.
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
                break;
            default:
                // Spotify's player could not be downloaded (offline) or broke: later, again.
                _ = _lane.Enqueue(_ => FailInLaneAsync(page));
                break;
        }
    }

    /// <summary>Answers Spotify's player when it asks for an access token. The token goes only to the page, never to a log.</summary>
    private async Task SendTokenAsync(IWebPlayerPage page)
    {
        string? rejected;
        CancellationToken token;
        lock (_gate)
        {
            if (!ReferenceEquals(_page, page) || _session is null)
            {
                return;
            }

            rejected = _rejectedToken;
            token = _session.Token;
        }

        try
        {
            var accessToken = await _tokens.GetAccessTokenAsync(rejected, token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!ReferenceEquals(_page, page))
                {
                    return;
                }

                _lastToken = accessToken;
                _rejectedToken = null;
            }

            page.Post(WebPlayerCommands.Token(accessToken));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped meanwhile.
        }
        catch (SpotifyAuthException)
        {
            _ = _lane.Enqueue(_ => CloseInLaneAsync(page, OwnPlayerStatus.NeedsSignIn));
        }
        catch (Exception)
        {
            // Offline, most likely: later, again.
            _ = _lane.Enqueue(_ => FailInLaneAsync(page));
        }
    }

    private bool IsCurrent(IWebPlayerPage page)
    {
        lock (_gate)
        {
            return ReferenceEquals(_page, page);
        }
    }

    private void SetStatus(OwnPlayerStatus status)
    {
        bool changed;
        lock (_gate)
        {
            changed = SetStatusLocked(status);
        }

        if (changed)
        {
            RaiseStatusChanged();
        }
    }

    /// <summary>Also lets <see cref="WaitForDeviceAsync"/> callers go once the player is ready, or will not be.</summary>
    private bool SetStatusLocked(OwnPlayerStatus status)
    {
        if (_status == status)
        {
            return false;
        }

        _status = status;
        if (status == OwnPlayerStatus.Ready)
        {
            _deviceWaiter.TrySetResult(_deviceId);
        }
        else if (status == OwnPlayerStatus.Starting)
        {
            if (_deviceWaiter.Task.IsCompleted)
            {
                _deviceWaiter = NewWaiter();
            }
        }
        else
        {
            _deviceWaiter.TrySetResult(null);
        }

        return true;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}

using System.Diagnostics;
using Resonate.Spotify.Auth;

namespace Resonate.App.Services;

/// <summary>
/// The Spotify sign-in for whichever developer app (client ID) the user set
/// up. Hands out access tokens to the Web API client.
/// </summary>
public sealed class AccountService : IAccessTokenSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokens;
    private readonly bool _alwaysSignedIn;
    private readonly Lock _signInGate = new();
    private SpotifySession? _session;

    // The browser sign-in under way, if any, and the whole of it (it holds the loopback port until it ends).
    private CancellationTokenSource? _pendingSignIn;
    private Task _pendingSignInDone = Task.CompletedTask;

    public AccountService(HttpClient http, ITokenStore tokens, string? clientId, bool alwaysSignedIn = false)
    {
        _http = http;
        _tokens = tokens;
        _alwaysSignedIn = alwaysSignedIn;
        if (SpotifyAuthOptions.IsValidClientId(clientId))
        {
            Configure(clientId!);
        }
    }

    /// <summary>Raised on any thread when the saved sign-in stopped working.</summary>
    public event EventHandler? SignedOut;

    public bool IsSignedIn => _alwaysSignedIn || _session?.IsSignedIn == true;

    /// <summary>
    /// Permissions newer features need that the saved sign-in lacks; signing
    /// in again grants them. Empty in demo mode.
    /// </summary>
    public IReadOnlyList<string> MissingScopes =>
        _alwaysSignedIn || _session is null ? [] : _session.MissingScopes(SpotifyAuthOptions.DefaultScopes);

    /// <summary>Uses <paramref name="clientId"/> from now on. A different app needs a new sign-in.</summary>
    public void Configure(string clientId)
    {
        CancelPendingSignIn();
        if (_session is not null)
        {
            _session.SignedOut -= OnSessionSignedOut;
            _session.Dispose();
        }

        var options = new SpotifyAuthOptions { ClientId = clientId };
        _session = new SpotifySession(new SpotifyAuthClient(_http, options), _tokens);
        _session.SignedOut += OnSessionSignedOut;
    }

    /// <summary>
    /// Signs in through the browser; also while signed in, to grant the
    /// permissions a newer Resonate asks for without signing out. A sign-in
    /// started while another waits for the browser replaces it.
    /// </summary>
    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        var session = _session ?? throw new SpotifyAuthException("no_client_id", "Enter your Spotify app's client ID first.");
        using var mine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (_signInGate)
        {
            _pendingSignIn?.Cancel();
            _pendingSignIn = mine;
            previous = _pendingSignInDone;
            _pendingSignInDone = done.Task;
        }

        try
        {
            // The sign-in this one replaces lets go of the loopback port first (it never fails: see the finally below).
            await previous.ConfigureAwait(false);
            mine.Token.ThrowIfCancellationRequested();
            await session.SignInAsync(OpenInBrowser, mine.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_signInGate)
            {
                if (ReferenceEquals(_pendingSignIn, mine))
                {
                    _pendingSignIn = null;
                }
            }

            done.TrySetResult();
        }
    }

    /// <summary>Signs out; a sign-in still waiting for the browser stops, so it cannot sign in again behind the user's back.</summary>
    public void SignOut()
    {
        CancelPendingSignIn();
        _session?.SignOut();
    }

    public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken) =>
        _session is null
            ? Task.FromException<string>(new SpotifyAuthException("not_signed_in", "Sign in to Spotify first."))
            : _session.GetAccessTokenAsync(rejectedToken, cancellationToken);

    public void Dispose() => _session?.Dispose();

    /// <summary>Opens a page in the user's default browser (Resonate never embeds one).</summary>
    public static void OpenInBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    private void OnSessionSignedOut(object? sender, EventArgs e) => SignedOut?.Invoke(this, EventArgs.Empty);

    private void CancelPendingSignIn()
    {
        lock (_signInGate)
        {
            _pendingSignIn?.Cancel();
        }
    }
}

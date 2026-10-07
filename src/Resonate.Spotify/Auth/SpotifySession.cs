namespace Resonate.Spotify.Auth;

/// <summary>Hands out a valid access token for Web API calls.</summary>
public interface IAccessTokenSource
{
    /// <summary>
    /// Returns a usable access token, renewing it first when it is about to
    /// expire. Pass the token that Spotify just rejected (HTTP 401) as
    /// <paramref name="rejectedToken"/> to force a renewal.
    /// </summary>
    Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken);
}

/// <summary>
/// The signed-in Spotify account: signs in through the browser, keeps the
/// token in the <see cref="ITokenStore"/>, and renews it when needed.
/// </summary>
public sealed class SpotifySession : IAccessTokenSource, IDisposable
{
    private readonly SpotifyAuthClient _auth;
    private readonly ITokenStore _store;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private SpotifyToken? _token;

    public SpotifySession(SpotifyAuthClient auth, ITokenStore store, TimeProvider? time = null)
    {
        _auth = auth;
        _store = store;
        _time = time ?? TimeProvider.System;
        _token = store.Load();
    }

    /// <summary>Raised when the saved sign-in stops working and the user must sign in again.</summary>
    public event EventHandler? SignedOut;

    public bool IsSignedIn => Volatile.Read(ref _token) is not null;

    /// <summary>
    /// Opens the browser at Spotify's approval page and waits for the user to
    /// approve. <paramref name="openBrowser"/> shows the page in the user's
    /// default browser (Resonate never embeds one).
    /// </summary>
    public async Task SignInAsync(Action<Uri> openBrowser, CancellationToken cancellationToken)
    {
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();

        using var listener = new LoopbackCallbackListener(_auth.Options.RedirectUri);
        try
        {
            listener.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            throw new SpotifyAuthException(
                $"Another program is using port {_auth.Options.RedirectUri.Port}, which Resonate needs for signing in. Close it and try again.",
                ex);
        }

        openBrowser(_auth.BuildAuthorizeUri(Pkce.CreateChallenge(verifier), state));

        var code = await listener.WaitForCodeAsync(state, cancellationToken).ConfigureAwait(false);
        var token = await _auth.ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
        Store(token);
    }

    public void SignOut()
    {
        Volatile.Write(ref _token, null);
        _store.Clear();
    }

    public async Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        var token = Volatile.Read(ref _token) ?? throw new SpotifyAuthException("not_signed_in", "Sign in to Spotify first.");
        if (!token.NeedsRefresh(_time.GetUtcNow()) && token.AccessToken != rejectedToken)
        {
            return token.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have renewed it while this one waited.
            token = Volatile.Read(ref _token) ?? throw new SpotifyAuthException("not_signed_in", "Sign in to Spotify first.");
            if (!token.NeedsRefresh(_time.GetUtcNow()) && token.AccessToken != rejectedToken)
            {
                return token.AccessToken;
            }

            SpotifyToken renewed;
            try
            {
                renewed = await _auth.RefreshAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (SpotifyAuthException ex) when (ex.RequiresSignIn)
            {
                SignOut();
                SignedOut?.Invoke(this, EventArgs.Empty);
                throw;
            }

            Store(renewed);
            return renewed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();

    private void Store(SpotifyToken token)
    {
        Volatile.Write(ref _token, token);
        _store.Save(token);
    }
}

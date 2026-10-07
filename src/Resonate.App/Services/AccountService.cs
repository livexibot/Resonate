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
    private SpotifySession? _session;

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

    /// <summary>Uses <paramref name="clientId"/> from now on. A different app needs a new sign-in.</summary>
    public void Configure(string clientId)
    {
        if (_session is not null)
        {
            _session.SignedOut -= OnSessionSignedOut;
            _session.Dispose();
        }

        var options = new SpotifyAuthOptions { ClientId = clientId };
        _session = new SpotifySession(new SpotifyAuthClient(_http, options), _tokens);
        _session.SignedOut += OnSessionSignedOut;
    }

    public Task SignInAsync(CancellationToken cancellationToken) =>
        _session is null
            ? Task.FromException(new SpotifyAuthException("no_client_id", "Enter your Spotify app's client ID first."))
            : _session.SignInAsync(OpenInBrowser, cancellationToken);

    public void SignOut() => _session?.SignOut();

    public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken) =>
        _session is null
            ? Task.FromException<string>(new SpotifyAuthException("not_signed_in", "Sign in to Spotify first."))
            : _session.GetAccessTokenAsync(rejectedToken, cancellationToken);

    public void Dispose() => _session?.Dispose();

    /// <summary>Opens a page in the user's default browser (Resonate never embeds one).</summary>
    public static void OpenInBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    private void OnSessionSignedOut(object? sender, EventArgs e) => SignedOut?.Invoke(this, EventArgs.Empty);
}

using System.Net.Http.Json;
using System.Text.Json;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Auth;

/// <summary>
/// The OAuth authorization code flow with PKCE against Spotify's accounts
/// service. No client secret is involved, so the client ID is not a secret.
/// </summary>
public sealed class SpotifyAuthClient
{
    private readonly HttpClient _http;
    private readonly SpotifyAuthOptions _options;
    private readonly TimeProvider _time;

    public SpotifyAuthClient(HttpClient http, SpotifyAuthOptions options, TimeProvider? time = null)
    {
        _http = http;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public SpotifyAuthOptions Options => _options;

    /// <summary>The page the user's browser opens to approve Resonate.</summary>
    public Uri BuildAuthorizeUri(string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["scope"] = string.Join(' ', _options.Scopes),
            ["redirect_uri"] = _options.RedirectUri.ToString(),
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge,
        };
        var builder = new UriBuilder(_options.AuthorizeEndpoint) { Query = ToQueryString(query) };
        return builder.Uri;
    }

    public Task<SpotifyToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri.ToString(),
                ["client_id"] = _options.ClientId,
                ["code_verifier"] = codeVerifier,
            },
            previousRefreshToken: null,
            cancellationToken);

    /// <summary>
    /// Renews the access token. Spotify may hand out a new refresh token and
    /// retire the old one, so callers must store the token this returns.
    /// </summary>
    public Task<SpotifyToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId,
            },
            previousRefreshToken: refreshToken,
            cancellationToken);

    private async Task<SpotifyToken> RequestTokenAsync(
        Dictionary<string, string> form,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_options.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            OAuthError? error = null;
            try
            {
                error = await response.Content
                    .ReadFromJsonAsync(SpotifyJsonContext.Default.OAuthError, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // Not a JSON error body; report the status code below.
            }

            // The error description is safe to show: it never contains a token.
            throw new SpotifyAuthException(
                error?.Error ?? "http_" + (int)response.StatusCode,
                error?.ErrorDescription ?? $"Spotify's sign-in service answered {(int)response.StatusCode}.");
        }

        var token = await response.Content
            .ReadFromJsonAsync(SpotifyJsonContext.Default.TokenResponse, cancellationToken)
            .ConfigureAwait(false);

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new SpotifyAuthException("invalid_response", "Spotify's sign-in service sent an empty answer.");
        }

        var refresh = string.IsNullOrEmpty(token.RefreshToken) ? previousRefreshToken : token.RefreshToken;
        if (string.IsNullOrEmpty(refresh))
        {
            throw new SpotifyAuthException("invalid_response", "Spotify did not send a refresh token.");
        }

        return new SpotifyToken(
            token.AccessToken,
            refresh,
            _time.GetUtcNow().AddSeconds(token.ExpiresIn),
            token.Scope ?? string.Empty);
    }

    internal static string ToQueryString(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join('&', values.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
}

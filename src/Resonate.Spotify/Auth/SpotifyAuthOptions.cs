namespace Resonate.Spotify.Auth;

/// <summary>How Resonate signs in to the user's own Spotify developer app.</summary>
public sealed record SpotifyAuthOptions
{
    /// <summary>
    /// The port of the loopback redirect. Spotify only accepts loopback IP
    /// literals over plain HTTP (not "localhost"), so the redirect URI the
    /// user registers is exactly <see cref="DefaultRedirectUri"/>.
    /// </summary>
    public const int DefaultRedirectPort = 43821;

    public const string DefaultRedirectUri = "http://127.0.0.1:43821/callback";

    /// <summary>The scopes Resonate asks for, and why.</summary>
    public static readonly IReadOnlyList<string> DefaultScopes =
    [
        "user-read-playback-state",    // devices and what is playing
        "user-modify-playback-state",  // play a song or playlist on this computer
        "user-read-currently-playing",
        "playlist-read-private",       // the playlists in the sidebar
        "playlist-read-collaborative",
        "user-library-read",           // Liked Songs
        "user-library-modify",         // like and unlike songs
        "playlist-modify-private",     // reorder, add to and create your own playlists
        "playlist-modify-public",
        "user-top-read",               // your top artists and songs, for the daily mixes
        "user-read-recently-played",   // listening statistics on the Home page
    ];

    public required string ClientId { get; init; }

    public Uri RedirectUri { get; init; } = new(DefaultRedirectUri);

    public IReadOnlyList<string> Scopes { get; init; } = DefaultScopes;

    public Uri AuthorizeEndpoint { get; init; } = new("https://accounts.spotify.com/authorize");

    public Uri TokenEndpoint { get; init; } = new("https://accounts.spotify.com/api/token");

    /// <summary>A client ID is 32 hexadecimal characters.</summary>
    public static bool IsValidClientId(string? clientId) =>
        clientId is { Length: 32 } && clientId.All(char.IsAsciiHexDigit);
}

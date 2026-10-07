namespace Resonate.Spotify.Auth;

/// <summary>Sign-in failed or the saved sign-in is no longer valid.</summary>
public sealed class SpotifyAuthException : Exception
{
    public SpotifyAuthException()
    {
    }

    public SpotifyAuthException(string message)
        : base(message)
    {
    }

    public SpotifyAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SpotifyAuthException(string error, string message)
        : base(message) => Error = error;

    /// <summary>The OAuth error code, for example <c>invalid_grant</c> or <c>access_denied</c>.</summary>
    public string? Error { get; }

    /// <summary>
    /// The refresh token was revoked or expired (Spotify refresh tokens last
    /// six months). The user has to sign in again.
    /// </summary>
    public bool RequiresSignIn => Error is "invalid_grant" or "invalid_client";
}

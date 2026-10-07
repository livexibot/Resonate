namespace Resonate.Spotify.Auth;

/// <summary>
/// Keeps the sign-in between launches. On Windows this is the Credential
/// Manager; tokens are never written to plain files.
/// </summary>
public interface ITokenStore
{
    SpotifyToken? Load();

    void Save(SpotifyToken token);

    void Clear();
}

/// <summary>Keeps the token in memory only. Used by tests and the demo mode.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private SpotifyToken? _token;

    public SpotifyToken? Load() => _token;

    public void Save(SpotifyToken token) => _token = token;

    public void Clear() => _token = null;
}

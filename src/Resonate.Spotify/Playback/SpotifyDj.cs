namespace Resonate.Spotify.Playback;

/// <summary>
/// Spotify's DJ. Only Spotify's own apps can start it (the Web API's play
/// command is silently declined and stops the music), so Resonate opens it
/// in the Spotify app and takes over the controls once it plays.
/// </summary>
public static class SpotifyDj
{
    /// <summary>What Spotify reports as the context while DJ plays, and the link that opens DJ in the Spotify app.</summary>
    public const string ContextUri = "spotify:playlist:37i9dQZF1EYkqdzj48dyYq";

    public static bool IsPlaying(PlayerState state) => state.ContextUri == ContextUri;
}

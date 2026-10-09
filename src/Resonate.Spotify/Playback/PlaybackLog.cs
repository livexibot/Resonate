namespace Resonate.Spotify.Playback;

/// <summary>
/// A short record of what the player heard from Spotify and what failed,
/// so a playback problem on the owner's PC can be traced afterwards. Lines
/// say only whether a session, device or song was there, never names,
/// tokens or answers. The app decides where they go (<see cref="Sink"/>);
/// without one nothing is kept.
/// </summary>
public static class PlaybackLog
{
    /// <summary>Receives each line, on whatever thread noted it.</summary>
    public static Action<string>? Sink { get; set; }

    public static void Note(string line) => Sink?.Invoke(line);
}

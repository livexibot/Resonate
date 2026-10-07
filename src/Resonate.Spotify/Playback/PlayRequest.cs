using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>
/// "Play this list, starting here": what a page asks for when the user plays
/// a song or a whole list. The player decides how (inside a Spotify context,
/// as Resonate's own ordered list, or with the local files player).
/// </summary>
/// <param name="Tracks">The list as the page shows it (sorted and filtered as the user sees it).</param>
/// <param name="StartIndex">The song the user picked, or -1 to start at the beginning (or anywhere, when shuffled).</param>
/// <param name="ContextUri">The Spotify playlist or album the list is, when it is one and the page shows it in its own order.</param>
/// <param name="SourceName">What it plays from, in words ("Liked Songs", a playlist's name, "Daily Mix 2").</param>
public sealed record PlayRequest(
    IReadOnlyList<TrackInfo> Tracks,
    int StartIndex,
    string? ContextUri,
    string? SourceName)
{
    /// <summary>
    /// True for "Shuffle play" (a truly random order, see <see cref="TrueShuffle"/>),
    /// false for "Play" in the order shown, null to keep the player's shuffle setting.
    /// </summary>
    public bool? Shuffle { get; init; }

    /// <summary>The song to start with, when the user picked one.</summary>
    public TrackInfo? StartTrack => StartIndex >= 0 && StartIndex < Tracks.Count ? Tracks[StartIndex] : null;

    /// <summary>Every song in the list is a file on this computer, played by Resonate itself.</summary>
    public bool IsLocalFiles => Tracks.Count > 0 && Tracks.All(t => t.FilePath is not null);
}

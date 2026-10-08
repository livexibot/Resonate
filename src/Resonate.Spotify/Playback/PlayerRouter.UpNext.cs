using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Up next (a built-in plugin): edits go to the player that has the music.
/// See <see cref="IUpNext"/>.
/// </summary>
public sealed partial class PlayerRouter
{
    /// <summary>What plays next in the player that has the music, or null when it can not be edited (music started outside Resonate).</summary>
    public UpNextList? UpNext => ActiveUpNext?.UpNext;

    private IUpNext? ActiveUpNext => ActiveSource == PlaybackSource.LocalFiles ? _local as IUpNext : Spotify;

    public bool MoveUpNext(UpNextPick song, int to) => ActiveUpNext?.MoveUpNext(song, to) == true;

    public bool RemoveUpNext(IReadOnlyList<UpNextPick> songs) => ActiveUpNext?.RemoveUpNext(songs) == true;

    public bool ClearUpNext() => ActiveUpNext?.ClearUpNext() == true;

    public bool ShuffleUpNext() => ActiveUpNext?.ShuffleUpNext() == true;

    /// <summary>While the user drags a song, Spotify is not sent the order (see <see cref="PlayerController.HoldUpNext"/>).</summary>
    public void HoldUpNext(bool held) => Spotify.HoldUpNext(held);

    /// <summary>
    /// "Play next" or "Add to queue" with Up next on: into the order
    /// Resonate plays when it can (files into the local files player's
    /// queue), otherwise into Spotify's own queue, which can only add at
    /// its end.
    /// </summary>
    public async Task AddToUpNextAsync(TrackInfo track, bool playNext)
    {
        var player = track.FilePath is not null ? _local as IUpNext : Spotify;
        if (player?.AddToUpNext(track, playNext) == true)
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        await AddToQueueAsync(track).ConfigureAwait(false);
    }
}

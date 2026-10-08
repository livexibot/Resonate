using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Up next (a built-in plugin) for the local files player: its own queue is
/// edited directly, and the engine is told which file follows now.
/// </summary>
public sealed partial class LocalPlayer : IUpNext
{
    public UpNextList? UpNext
    {
        get
        {
            lock (_gate)
            {
                return _queue.Current is { } current ? new UpNextList(current, _queue.Upcoming, CanEdit: true) : null;
            }
        }
    }

    public bool MoveUpNext(UpNextPick song, int to) => EditUpNext(q => q.MoveUpNext(song, to));

    public bool RemoveUpNext(IReadOnlyList<UpNextPick> songs) => EditUpNext(q => q.RemoveUpNext(songs));

    public bool ClearUpNext() => EditUpNext(q => q.ClearUpNext());

    public bool ShuffleUpNext() => EditUpNext(q => q.ShuffleUpNext());

    public bool AddToUpNext(TrackInfo track, bool playNext)
    {
        if (track.FilePath is null)
        {
            return false;
        }

        // "Add to queue", or "Play next" with nothing chosen yet, is the usual queue.
        if (!playNext || !EditUpNext(q => q.PlayNext(track)))
        {
            _ = AddToQueueAsync(track);
        }

        return true;
    }

    private bool EditUpNext(Func<LocalPlayQueue, bool> edit)
    {
        lock (_gate)
        {
            if (!edit(_queue))
            {
                return false;
            }
        }

        _ = _transport.Enqueue(_ =>
        {
            UpdateNext();
            return Task.CompletedTask;
        });
        return true;
    }
}

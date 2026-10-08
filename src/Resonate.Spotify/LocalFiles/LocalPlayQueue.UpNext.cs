using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Up next (a built-in plugin): edits to what plays next. The edits work on
/// <see cref="Upcoming"/> as shown; afterwards the songs still queued stay
/// queued, and the list becomes the songs played so far, the current one and
/// the rest in the new order (with repeat on, the current one and the rest,
/// since the songs played before are among the upcoming ones then).
/// </summary>
public sealed partial class LocalPlayQueue
{
    public bool MoveUpNext(UpNextPick song, int to) =>
        Edit((upcoming, queued) => UpNextEdits.IsAt(upcoming, song) && to >= 0 && to < upcoming.Count
            ? UpNextEdits.Move(upcoming, queued, song.Index, to)
            : -1);

    public bool RemoveUpNext(IReadOnlyList<UpNextPick> songs) =>
        Edit((upcoming, queued) => songs.Count > 0 && songs.All(s => UpNextEdits.IsAt(upcoming, s))
            ? UpNextEdits.Remove(upcoming, queued, songs.Select(s => s.Index))
            : -1);

    public bool ClearUpNext() =>
        Edit((upcoming, _) =>
        {
            upcoming.Clear();
            return 0;
        });

    public bool ShuffleUpNext() => Edit((upcoming, _) => UpNextEdits.Shuffle(upcoming, _random));

    /// <summary>"Play next": right after the current song, ahead of anything queued before.</summary>
    public bool PlayNext(TrackInfo track) => Edit((upcoming, queued) => UpNextEdits.Insert(upcoming, queued, track, playNext: true));

    private bool Edit(Func<List<TrackInfo>, int, int> edit)
    {
        if (Current is not { } current)
        {
            return false;
        }

        var upcoming = Upcoming.ToList();
        var queued = _queued.Count;
        var nowQueued = edit(upcoming, queued);
        if (nowQueued < 0)
        {
            return false;
        }

        var played = new List<TrackInfo>();
        if (Repeat == RepeatMode.Off)
        {
            // After a queued song, the list song before it has played too.
            var end = _currentIsQueued ? _position : _position - 1;
            for (var i = 0; i <= end && i < _order.Count; i++)
            {
                played.Add(_list[_order[i]]);
            }
        }

        _queued.Clear();
        foreach (var song in upcoming.Take(nowQueued))
        {
            _queued.Enqueue(song);
        }

        _list = [.. played, current, .. upcoming.Skip(nowQueued)];
        _order = [.. Enumerable.Range(0, _list.Count)];
        _position = played.Count;
        _currentIsQueued = false;
        return true;
    }
}

using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// What plays now and next in the local files player: a list in the order
/// shown or truly shuffled (<see cref="TrueShuffle"/>), songs added with "Add
/// to queue" (they play next, in the order added), and repeat. Not thread
/// safe; the player guards it.
/// </summary>
public sealed partial class LocalPlayQueue
{
    private readonly Func<int, int>? _random;
    private readonly Queue<TrackInfo> _queued = new();
    private IReadOnlyList<TrackInfo> _list = [];
    private List<int> _order = [];
    private int _position = -1;
    private bool _currentIsQueued;

    /// <param name="random">A random integer in [0, max) for shuffling; the system's cryptographic generator when null.</param>
    public LocalPlayQueue(Func<int, int>? random = null) => _random = random;

    /// <summary>The song playing (or ready to play), or null when nothing was chosen.</summary>
    public TrackInfo? Current { get; private set; }

    public bool Shuffle { get; private set; }

    public RepeatMode Repeat { get; set; }

    /// <summary>The list as it was given (the order shown on the page).</summary>
    public IReadOnlyList<TrackInfo> List => _list;

    /// <summary>The songs added with "Add to queue" that have not played yet.</summary>
    public IReadOnlyList<TrackInfo> Queued => [.. _queued];

    /// <summary>The songs that play next, in order: the queue first, then the rest of the list (from the start again with repeat on).</summary>
    public IReadOnlyList<TrackInfo> Upcoming
    {
        get
        {
            var upcoming = new List<TrackInfo>(_queued);
            if (_order.Count == 0)
            {
                return upcoming;
            }

            for (var i = _position + 1; i < _order.Count; i++)
            {
                upcoming.Add(_list[_order[i]]);
            }

            if (Repeat != RepeatMode.Off)
            {
                // After a queued song, the list song before it has not played again yet.
                var wrapEnd = _currentIsQueued ? _position : _position - 1;
                for (var i = 0; i <= wrapEnd; i++)
                {
                    upcoming.Add(_list[_order[i]]);
                }
            }

            return upcoming;
        }
    }

    /// <summary>
    /// Plays <paramref name="tracks"/> from <paramref name="startIndex"/> (-1
    /// for the start, or anywhere when shuffled). Songs already queued with
    /// "Add to queue" stay queued, as in Spotify.
    /// </summary>
    public TrackInfo? Load(IReadOnlyList<TrackInfo> tracks, int startIndex, bool shuffle)
    {
        _list = [.. tracks];
        Shuffle = shuffle;
        _currentIsQueued = false;
        if (_list.Count == 0)
        {
            _order = [];
            _position = -1;
            Current = null;
            return null;
        }

        var start = startIndex >= 0 && startIndex < _list.Count ? startIndex : -1;
        var indexes = Enumerable.Range(0, _list.Count).ToList();
        if (shuffle)
        {
            _order = start >= 0 ? TrueShuffle.ShuffleAfter(indexes, start, _random) : TrueShuffle.Shuffle(indexes, _random);
            _position = 0;
        }
        else
        {
            _order = indexes;
            _position = Math.Max(start, 0);
        }

        Current = _list[_order[_position]];
        return Current;
    }

    /// <summary>Turns shuffle on or off. The song playing keeps playing; what follows it changes.</summary>
    public void SetShuffle(bool shuffle)
    {
        Shuffle = shuffle;
        if (_order.Count == 0)
        {
            return;
        }

        var anchor = _order[_position];
        if (shuffle)
        {
            var rest = Enumerable.Range(0, _list.Count).Where(i => i != anchor);
            _order = [anchor, .. TrueShuffle.Shuffle(rest, _random)];
            _position = 0;
        }
        else
        {
            _order = [.. Enumerable.Range(0, _list.Count)];
            _position = anchor;
        }
    }

    /// <summary>"Add to queue": plays after the current song and anything queued before it.</summary>
    public void AddToQueue(TrackInfo track) => _queued.Enqueue(track);

    /// <summary>
    /// Moves on to the next song and returns it, or null at the end of the
    /// list with repeat off. A song that ended by itself plays again with
    /// "repeat one"; skipping always moves on.
    /// </summary>
    public TrackInfo? MoveNext(bool skipped)
    {
        if (!skipped && Repeat == RepeatMode.One && Current is not null)
        {
            return Current;
        }

        if (_queued.Count > 0)
        {
            Current = _queued.Dequeue();
            _currentIsQueued = true;
            return Current;
        }

        if (_order.Count == 0)
        {
            return null;
        }

        if (_position + 1 < _order.Count)
        {
            _position++;
        }
        else if (Repeat != RepeatMode.Off)
        {
            _position = 0;
        }
        else
        {
            return null;
        }

        _currentIsQueued = false;
        Current = _list[_order[_position]];
        return Current;
    }

    /// <summary>What <see cref="MoveNext"/> would play after the current song ends by itself, without moving; null for nothing.</summary>
    public TrackInfo? PeekNext()
    {
        if (Repeat == RepeatMode.One && Current is not null)
        {
            return Current;
        }

        if (_queued.Count > 0)
        {
            return _queued.Peek();
        }

        if (_order.Count == 0)
        {
            return null;
        }

        if (_position + 1 < _order.Count)
        {
            return _list[_order[_position + 1]];
        }

        return Repeat != RepeatMode.Off ? _list[_order[0]] : null;
    }

    /// <summary>
    /// Goes back one song and returns it: from a queued song to the list song
    /// before it; from the first song to the last with repeat on, otherwise
    /// it stays on the first.
    /// </summary>
    public TrackInfo? MovePrevious()
    {
        if (_order.Count == 0)
        {
            return Current;
        }

        if (_currentIsQueued)
        {
            _currentIsQueued = false;
        }
        else if (_position > 0)
        {
            _position--;
        }
        else if (Repeat != RepeatMode.Off)
        {
            _position = _order.Count - 1;
        }

        Current = _list[_order[_position]];
        return Current;
    }

    /// <summary>After the last song: back to the first, ready to play again.</summary>
    public TrackInfo? ResetToStart()
    {
        if (_order.Count == 0)
        {
            return null;
        }

        _position = 0;
        _currentIsQueued = false;
        Current = _list[_order[0]];
        return Current;
    }
}

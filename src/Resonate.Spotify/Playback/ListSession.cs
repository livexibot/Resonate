using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// A list Resonate started and knows the songs of, and how Spotify plays it:
/// either inside its own Spotify context (the list's own order), or as
/// Resonate's own order of songs, sent to Spotify as a list of song
/// addresses a window at a time. Resonate's truly random shuffle and
/// "repeat all" over a long list live here, instead of in Spotify's own
/// shuffle (which is weighted and can not be told what to play).
/// Not thread-safe: <see cref="PlayerController"/> uses it under its lock.
/// A new plan (shuffle switched, a fallback) is a new instance, so the old
/// one can be put back if Spotify refuses.
/// </summary>
internal sealed class ListSession
{
    /// <summary>
    /// Songs sent to Spotify in one play command. Spotify documents no limit
    /// for "uris", but other apps found that much longer lists are refused
    /// (about 800 answers 413) or stall, so Resonate sends a modest window and
    /// moves on to the next one as the window's last song starts.
    /// </summary>
    public const int WindowSize = PlayerController.MaxUrisPerRequest;

    private readonly Func<int, int>? _nextInt;

    private ListSession(
        IReadOnlyList<TrackInfo> all,
        IReadOnlyList<TrackInfo> playable,
        string? contextUri,
        string? sourceName,
        bool shuffle,
        bool inContext,
        List<TrackInfo> order,
        int index,
        Func<int, int>? nextInt)
    {
        All = all;
        Playable = playable;
        ContextUri = contextUri;
        SourceName = sourceName;
        Shuffle = shuffle;
        InContext = inContext;
        Order = order;
        Index = index;
        _nextInt = nextInt;
        WindowStart = 0;
        WindowEnd = order.Count;
    }

    /// <summary>The list as the page showed it, local entries included.</summary>
    public IReadOnlyList<TrackInfo> All { get; }

    /// <summary>Its songs Spotify can start by their address.</summary>
    public IReadOnlyList<TrackInfo> Playable { get; }

    /// <summary>The playlist or album the list is, when it was shown in its own order.</summary>
    public string? ContextUri { get; }

    public string? SourceName { get; }

    /// <summary>Resonate's shuffle is on (what the shuffle button shows while this list plays).</summary>
    public bool Shuffle { get; }

    /// <summary>Spotify plays <see cref="ContextUri"/> itself, in the list's own order; otherwise it plays windows of <see cref="Order"/>.</summary>
    public bool InContext { get; }

    /// <summary>
    /// The songs in the order they play: <see cref="All"/> inside a context,
    /// otherwise one or more passes over <see cref="Playable"/> (another pass is
    /// added when "repeat all" reaches the end).
    /// </summary>
    public List<TrackInfo> Order { get; }

    /// <summary>Where in <see cref="Order"/> the playing song is.</summary>
    public int Index { get; set; }

    /// <summary>The part of <see cref="Order"/> Spotify has now (from, and up to but not including, the end).</summary>
    public int WindowStart { get; private set; }

    public int WindowEnd { get; private set; }

    /// <summary>
    /// The plan could not start at once, because the playing song can not be
    /// started by its address (a file from the user's computer) or is not in
    /// the list (a queued song): Spotify gets it as the next song starts.
    /// </summary>
    public bool StartsWithNextSong { get; private init; }

    /// <summary>The song that was playing when <see cref="StartsWithNextSong"/> was set.</summary>
    public int WaitingFrom { get; private init; } = -1;

    /// <summary>The user picked a song (rather than playing the list from its start).</summary>
    public bool Picked { get; private init; }

    /// <summary>The song playing now is not in the list (a queued song), so <see cref="Index"/> is the list's last one.</summary>
    public bool PlayingOther { get; set; }

    public TrackInfo? Current => Index >= 0 && Index < Order.Count ? Order[Index] : null;

    /// <summary>A song Spotify can be told to play by its address (not a file on the computer, not a file Resonate plays).</summary>
    public static bool CanStartByUri(TrackInfo track) =>
        track.IsPlayable
        && track.Uri is { } uri
        && !uri.StartsWith("spotify:local:", StringComparison.Ordinal)
        && track.FilePath is null;

    /// <summary>
    /// The plan for playing <paramref name="request"/>, or null when Resonate
    /// does not know the list's songs (nothing it can start, or a lone song
    /// standing for a whole album), so Spotify must play the context itself.
    /// </summary>
    /// <param name="shuffle">Resonate's shuffle is on for this list.</param>
    /// <param name="nextInt">Random numbers for shuffling (tests pass their own).</param>
    public static ListSession? Create(PlayRequest request, bool shuffle, Func<int, int>? nextInt)
    {
        var all = request.Tracks;
        var playable = all.Where(CanStartByUri).ToList();
        if (playable.Count == 0 || (request.ContextUri is not null && playable.Count < 2))
        {
            return null;
        }

        var picked = request.StartTrack;
        if (picked is not null && !CanStartByUri(picked))
        {
            if (picked.IsLocal && request.ContextUri is not null && request.StartIndex >= 0)
            {
                // A file from the computer plays only inside its playlist, by
                // position. Start it there; a shuffled order follows from the next song.
                return new ListSession(all, playable, request.ContextUri, request.SourceName, shuffle, inContext: true, all.ToList(), request.StartIndex, nextInt)
                {
                    Picked = true,
                    StartsWithNextSong = shuffle,
                    WaitingFrom = shuffle ? request.StartIndex : -1,
                };
            }

            picked = NextPlayableFrom(all, request.StartIndex);
        }

        if (shuffle)
        {
            var first = picked is null ? -1 : IndexOfReference(playable, picked);
            var order = TrueShuffle.ShuffleAfter(playable, first, nextInt);
            return new ListSession(all, playable, request.ContextUri, request.SourceName, true, inContext: false, order, 0, nextInt)
            {
                Picked = picked is not null,
            }.WithWindowFrom(0);
        }

        if (request.ContextUri is not null)
        {
            var index = picked is null ? 0 : IndexOfReference(all, picked);
            return new ListSession(all, playable, request.ContextUri, request.SourceName, false, inContext: true, all.ToList(), Math.Max(0, index), nextInt)
            {
                Picked = picked is not null,
            };
        }

        var start = picked is null ? 0 : Math.Max(0, IndexOfReference(playable, picked));
        return new ListSession(all, playable, null, request.SourceName, false, inContext: false, playable.ToList(), start, nextInt)
        {
            Picked = picked is not null,
        }.WithOwnOrderWindow();
    }

    /// <summary>What to send Spotify to start this plan at the current song (the caller adds a position when it restarts one).</summary>
    public StartPlaybackBody StartBody()
    {
        if (InContext)
        {
            PlaybackOffset? offset = null;
            if (Current is { } song && (Picked || Index > 0))
            {
                // Files from the computer can not be named by address, and an
                // address listed twice would start at its first copy; every
                // other song is named by address, which is exact even if the
                // playlist changed since it was loaded.
                offset = CanStartByUri(song) && !IsListedTwice(All, song.Uri)
                    ? new PlaybackOffset { Uri = song.Uri }
                    : new PlaybackOffset { Position = song.Position ?? Index };
            }

            return new StartPlaybackBody { ContextUri = ContextUri, Offset = offset };
        }

        // A position, not an address: a list may hold the same song twice.
        var at = Index - WindowStart;
        return new StartPlaybackBody
        {
            Uris = Order.Skip(WindowStart).Take(WindowEnd - WindowStart).Select(t => t.Uri!).ToList(),
            Offset = at > 0 ? new PlaybackOffset { Position = at } : null,
        };
    }

    /// <summary>
    /// Where the song now playing is in <see cref="Order"/>: by its address,
    /// or by its title when the address is unknown or not in the list (Spotify
    /// sometimes plays a stand-in version of a song under another address),
    /// looking first at the songs Spotify has (from the current one on), then
    /// further. -1 when it is not in the list (a queued song, or music from elsewhere).
    /// </summary>
    public int Locate(string? uri, string? title)
    {
        var byUri = uri is null ? -1 : Search(t => t.Uri == uri);
        return byUri >= 0 || title is null
            ? byUri
            : Search(t => string.Equals(t.Title.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private int Search(Func<TrackInfo, bool> matches)
    {
        var start = Math.Clamp(Index, 0, Order.Count);
        var end = InContext ? Order.Count : WindowEnd;
        var from = InContext ? 0 : WindowStart;
        return Find(start, end) is var a and >= 0 ? a
            : Find(from, start) is var b and >= 0 ? b
            : Find(end, Order.Count) is var c and >= 0 ? c
            : Find(0, from);

        int Find(int begin, int stop)
        {
            for (var i = Math.Max(0, begin); i < Math.Min(stop, Order.Count); i++)
            {
                if (matches(Order[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// Called as a song starts. When it is the last one Spotify has, returns
    /// the next window to send (starting with this song, which the caller
    /// restarts where it is), adding another pass for "repeat all"; null when
    /// Spotify already has what follows, or nothing follows.
    /// </summary>
    public StartPlaybackBody? ContinueAfterWindow(RepeatMode repeat)
    {
        if (InContext || StartsWithNextSong || Index != WindowEnd - 1)
        {
            return null;
        }

        if (Index + 1 >= Order.Count)
        {
            if (repeat != RepeatMode.All)
            {
                return null;
            }

            var spotifyLoops = !Shuffle && WindowEnd - WindowStart == Playable.Count && WindowStart % Playable.Count == 0;
            AddPass();
            if (spotifyLoops)
            {
                // Spotify has the whole list in its own order and repeats it by itself.
                WindowStart += Playable.Count;
                WindowEnd += Playable.Count;
                return null;
            }
        }

        WithWindowFrom(Index);
        return StartBody();
    }

    /// <summary>
    /// The plan after shuffle was switched while this list plays: the rest in
    /// a new random order after the current song, or back to the list's own
    /// order from the current song (inside its context when it has one).
    /// </summary>
    public ListSession WithShuffle(bool shuffle)
    {
        var current = Current;
        if (current is null || PlayingOther || !CanStartByUri(current))
        {
            // Spotify can not restart this song by its address (or it is a
            // queued song): keep it playing and start the new order with the next song.
            return Waiting(shuffle, Index);
        }

        return Restarted(current, shuffle, ContextUri);
    }

    /// <summary>The waiting plan, now that the song at <paramref name="index"/> started.</summary>
    public ListSession StartingAt(int index)
    {
        Index = index;
        return Current is { } current && CanStartByUri(current)
            ? Restarted(current, Shuffle, ContextUri)
            : Waiting(Shuffle, index); // Another file from the computer; wait for the next song again.
    }

    /// <summary>The same list as songs, without its context (Spotify refused the context).</summary>
    public ListSession WithoutContext()
    {
        var current = Current;
        var song = current is not null && CanStartByUri(current)
            ? current
            : NextPlayableFrom(All, Math.Max(0, IndexOfReference(All, current)));
        return Restarted(song ?? Playable[0], Shuffle, contextUri: null);
    }

    private static int IndexOfReference(IReadOnlyList<TrackInfo> list, TrackInfo? track)
    {
        if (track is null)
        {
            return -1;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], track))
            {
                return i;
            }
        }

        // The same song from another copy of the list.
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Uri == track.Uri && list[i].Uri is not null)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The first song Spotify can start at or after <paramref name="index"/>, or the list's first one.</summary>
    private static TrackInfo? NextPlayableFrom(IReadOnlyList<TrackInfo> list, int index)
    {
        for (var i = Math.Max(0, index); i < list.Count; i++)
        {
            if (CanStartByUri(list[i]))
            {
                return list[i];
            }
        }

        return list.FirstOrDefault(CanStartByUri);
    }

    /// <summary>A plan that starts with <paramref name="current"/>, which Spotify can start by its address.</summary>
    private ListSession Restarted(TrackInfo current, bool shuffle, string? contextUri)
    {
        if (shuffle)
        {
            var rest = Playable.ToList();
            var at = IndexOfReference(rest, current);
            if (at >= 0)
            {
                rest.RemoveAt(at);
            }

            var order = new List<TrackInfo>(Playable.Count) { current };
            order.AddRange(TrueShuffle.Shuffle(rest, _nextInt));
            return new ListSession(All, Playable, contextUri, SourceName, true, inContext: false, order, 0, _nextInt)
            {
                Picked = true,
            }.WithWindowFrom(0);
        }

        if (contextUri is not null)
        {
            var inList = IndexOfReference(All, current);
            return new ListSession(All, Playable, contextUri, SourceName, false, inContext: true, All.ToList(), Math.Max(0, inList), _nextInt)
            {
                Picked = true,
            };
        }

        var index = Math.Max(0, IndexOfReference(Playable, current));
        return new ListSession(All, Playable, null, SourceName, false, inContext: false, Playable.ToList(), index, _nextInt)
        {
            Picked = true,
        }.WithOwnOrderWindow();
    }

    /// <summary>The same songs and window as now, with a new order to start when <paramref name="from"/> is no longer playing.</summary>
    private ListSession Waiting(bool shuffle, int from) =>
        new(All, Playable, ContextUri, SourceName, shuffle, InContext, Order, from, _nextInt)
        {
            Picked = true,
            StartsWithNextSong = true,
            WaitingFrom = from,
            WindowStart = WindowStart,
            WindowEnd = WindowEnd,
        };

    /// <summary>The window starts at <paramref name="index"/> and holds as many songs as fit.</summary>
    private ListSession WithWindowFrom(int index)
    {
        WindowStart = index;
        WindowEnd = Math.Min(Order.Count, index + WindowSize);
        return this;
    }

    /// <summary>The whole list when it fits (so "previous" works too), otherwise a window from the current song.</summary>
    private ListSession WithOwnOrderWindow()
    {
        WindowStart = Math.Clamp(Index, 0, Math.Max(0, Order.Count - WindowSize));
        WindowEnd = Math.Min(Order.Count, WindowStart + WindowSize);
        if (Index > WindowStart && Current is { } song && IsListedTwice(Order.Skip(Index).Take(WindowEnd - Index), song.Uri))
        {
            // Spotify refuses (403) to start a list at a song that comes again
            // later in it, so the window starts at this song (no offset) instead.
            WithWindowFrom(Index);
        }

        return this;
    }

    private static bool IsListedTwice(IEnumerable<TrackInfo> songs, string? uri) =>
        uri is not null && songs.Count(t => t.Uri == uri) > 1;

    /// <summary>"Repeat all" reached the end: another pass, in a fresh random order when shuffled.</summary>
    private void AddPass()
    {
        var count = Playable.Count;
        if (Shuffle)
        {
            var pass = TrueShuffle.Shuffle(Playable, _nextInt);
            if (count > 1 && ReferenceEquals(pass[0], Order[^1]))
            {
                // Not the song that just played, twice in a row.
                var swap = 1 + (_nextInt ?? System.Security.Cryptography.RandomNumberGenerator.GetInt32)(count - 1);
                (pass[0], pass[swap]) = (pass[swap], pass[0]);
            }

            Order.AddRange(pass);
        }
        else
        {
            Order.AddRange(Playable);
        }

        // Keep only the pass before this one, so a list that loops all day stays small.
        if (Order.Count > 3 * count && WindowStart >= 2 * count)
        {
            Order.RemoveRange(0, count);
            Index -= count;
            WindowStart -= count;
            WindowEnd -= count;
        }
    }
}

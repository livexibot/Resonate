namespace Resonate.Spotify.Lyrics;

/// <summary>
/// The lyrics pane's one way in: the disk cache first, then the source,
/// whose answer is kept ("none" included). A failed lookup keeps nothing,
/// so "Try again" asks again.
/// </summary>
public sealed class LyricsLibrary
{
    private readonly ILyricsSource _source;
    private readonly LyricsCache? _cache;
    private int _pruned;

    public LyricsLibrary(ILyricsSource source, LyricsCache? cache)
    {
        _source = source;
        _cache = cache;
    }

    /// <summary>
    /// The words of a song, or null when there are none. Reads the disk:
    /// call off the interface thread. Throws when the source cannot be reached.
    /// </summary>
    public async Task<SongLyrics?> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            // Old answers go once a run, the first time lyrics are wanted.
            if (Interlocked.Exchange(ref _pruned, 1) == 0)
            {
                _cache.Prune();
            }

            if (_cache.TryGet(query, out var cached))
            {
                return cached;
            }
        }

        var found = await _source.FindAsync(query, cancellationToken).ConfigureAwait(false);
        _cache?.Store(query, found);
        return found;
    }
}

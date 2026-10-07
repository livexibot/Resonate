using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>
/// Which songs are in Liked Songs, so every list and the player bar can
/// show a heart without asking Spotify song by song. Built from the stored
/// copy of Liked Songs (one request when nothing changed). Liking and
/// unliking are optimistic: the heart changes at once and changes back if
/// Spotify refuses.
/// </summary>
public sealed class LikedSongs
{
    private readonly ISpotifyWebApi _api;
    private readonly LibraryService _library;
    private readonly Lock _gate = new();

    /// <summary>Likes and unlikes made while the list loads, which the list may miss.</summary>
    private readonly Dictionary<string, bool> _changedWhileLoading = new(StringComparer.Ordinal);
    private HashSet<string> _uris = new(StringComparer.Ordinal);
    private int _loads;
    private int _generation;

    public LikedSongs(ISpotifyWebApi api, LibraryService library)
    {
        _api = api;
        _library = library;
    }

    /// <summary>Raised on any thread when a song's heart changed (or, with no song, after the whole list was read or forgotten).</summary>
    public event EventHandler<LikeChange>? Changed;

    /// <summary>The whole list has been read for the account signed in; before that, <see cref="IsLiked"/> only knows songs liked in Resonate.</summary>
    public bool IsLoaded { get; private set; }

    public bool IsLiked(string? uri)
    {
        if (uri is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _uris.Contains(uri);
        }
    }

    /// <summary>Reads Liked Songs (from the stored copy when it is still current).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        int generation;
        lock (_gate)
        {
            generation = _generation;
            _loads++;
        }

        try
        {
            var all = await _library.GetAllLikedSongsAsync(cancellationToken).ConfigureAwait(false);
            var uris = all.Select(t => t.Uri).OfType<string>().ToHashSet(StringComparer.Ordinal);
            lock (_gate)
            {
                if (generation != _generation)
                {
                    // Signed out meanwhile: the list belongs to the account before.
                    return;
                }

                // A heart clicked while the list loaded stays as the user left it.
                foreach (var (uri, liked) in _changedWhileLoading)
                {
                    if (liked)
                    {
                        uris.Add(uri);
                    }
                    else
                    {
                        uris.Remove(uri);
                    }
                }

                _uris = uris;
                IsLoaded = true;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (--_loads == 0)
                {
                    _changedWhileLoading.Clear();
                }
            }
        }

        Changed?.Invoke(this, LikeChange.Everything);
    }

    /// <summary>Forgets the account's liked songs, for example on signing out; <see cref="LoadAsync"/> reads the next account's.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _generation++;
            _uris = new HashSet<string>(StringComparer.Ordinal);
            _changedWhileLoading.Clear();
            IsLoaded = false;
        }

        Changed?.Invoke(this, LikeChange.Everything);
    }

    /// <summary>
    /// Likes or unlikes a song. The heart changes at once; if Spotify
    /// refuses, it changes back and the error is thrown for the caller to show.
    /// </summary>
    public async Task SetLikedAsync(TrackInfo track, bool liked, CancellationToken cancellationToken)
    {
        if (track.Uri is not { } uri || track.FilePath is not null || track.IsLocal)
        {
            return;
        }

        if (!Apply(track, uri, liked))
        {
            // Until the list is read, a liked song can be missing from it
            // (Liked Songs shows every heart filled), so an unlike still goes out.
            if (liked || IsLoaded)
            {
                return;
            }

            Changed?.Invoke(this, new LikeChange(track, liked));
        }

        try
        {
            if (liked)
            {
                await _api.SaveToLibraryAsync([uri], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _api.RemoveFromLibraryAsync([uri], cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            Apply(track, uri, !liked);
            throw;
        }

        await _library.NoteLikeChangedAsync(track, liked, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Changes the stored state; false when it already was so.</summary>
    private bool Apply(TrackInfo track, string uri, bool liked)
    {
        bool changed;
        lock (_gate)
        {
            changed = liked ? _uris.Add(uri) : _uris.Remove(uri);
            if (_loads > 0)
            {
                _changedWhileLoading[uri] = liked;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, new LikeChange(track, liked));
        }

        return changed;
    }
}

/// <summary>A song that was liked or unliked; no song means the whole list was read again (or forgotten).</summary>
public sealed record LikeChange(TrackInfo? Track, bool IsLiked)
{
    public static readonly LikeChange Everything = new(null, false);

    public string? Uri => Track?.Uri;
}

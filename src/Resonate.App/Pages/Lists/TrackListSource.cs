using System.Globalization;
using Resonate.App.Services;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages.Lists;

/// <summary>What the header of a song list shows.</summary>
/// <param name="Kind">A small word above the title, such as "PLAYLIST" or "ALBUM".</param>
/// <param name="PlaceholderName">Picks the colour tile shown until (or instead of) a cover.</param>
/// <param name="Glyph">An icon on the colour tile, for lists without a cover (Liked Songs, Local Files).</param>
public sealed record ListHeader(
    string Kind,
    string Title,
    string? Description,
    string? Details,
    string? ImageUrl,
    string PlaceholderName,
    string? Glyph = null)
{
    /// <summary>The album's artists, shown as links.</summary>
    public IReadOnlyList<ArtistRef> Artists { get; init; } = [];
}

/// <summary>
/// One kind of song list for <see cref="TracksPage"/>: Liked Songs, a
/// playlist, an album, Local Files or a mix. The page does the rest
/// (sorting, filtering, playing, the context menu) the same way for all.
/// </summary>
public abstract class TrackListSource
{
    /// <summary>The navigation key of the list, also used to remember its sort.</summary>
    public abstract string Key { get; }

    /// <summary>The Spotify playlist or album to play inside, when the list is shown in its own order.</summary>
    public virtual string? ContextUri => null;

    /// <summary>A spotify: link for "Open in Spotify", when there is one.</summary>
    public virtual string? SpotifyLink => null;

    /// <summary>The user may reorder and remove songs (their own or a collaborative playlist).</summary>
    public virtual bool CanEdit => false;

    /// <summary>Albums number songs by track number and hide the album column.</summary>
    public virtual bool IsAlbum => false;

    /// <summary>The list knows when each song was added.</summary>
    public virtual bool HasDateAdded => true;

    /// <summary>A note shown when the list has no songs.</summary>
    public virtual string EmptyText => "No songs here yet.";

    /// <summary>What the sort menu calls the list's own order.</summary>
    public virtual string OwnOrderName => "Custom order";

    /// <summary>The header from what is known at once (the sidebar's cache), before anything loads.</summary>
    public abstract ListHeader CachedHeader { get; }

    /// <summary>The full header, when loading it tells more than <see cref="CachedHeader"/>; null to keep it.</summary>
    public virtual Task<ListHeader?> LoadHeaderAsync(CancellationToken cancellationToken) => Task.FromResult<ListHeader?>(null);

    /// <summary>Every song, in the list's own order. Called on a background thread.</summary>
    public abstract Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The first songs, shown while a long list loads for the first time;
    /// null when the list has no quicker first part. Called on a background thread.
    /// </summary>
    public virtual Task<IReadOnlyList<TrackInfo>?> LoadPreviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackInfo>?>(null);

    /// <summary>Moves a song within the list's own order (only when <see cref="CanEdit"/>).</summary>
    public virtual Task MoveAsync(IReadOnlyList<TrackInfo> before, int from, int to, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Removes a song from the list (only when <see cref="CanEdit"/>).</summary>
    public virtual Task RemoveAsync(IReadOnlyList<TrackInfo> before, TrackInfo track, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// For lists that change by themselves (Local Files, as scans find files):
    /// <paramref name="changed"/> is called on any thread, with true when the
    /// songs changed and false when only the header or empty note did.
    /// </summary>
    public virtual void Attach(Action<bool> changed)
    {
    }

    /// <summary>Stops the calls <see cref="Attach"/> asked for.</summary>
    public virtual void Detach()
    {
    }

    /// <summary>
    /// The list for a navigation key: "liked", "local", "onrepeat",
    /// "mix:&lt;number&gt;", "album:&lt;id&gt;", "liked-artist:&lt;id&gt;",
    /// or a playlist ID.
    /// </summary>
    public static TrackListSource For(string key, AppServices services) => key switch
    {
        LikedSongsSource.ListKey => new LikedSongsSource(services),
        LocalFilesSource.ListKey => new LocalFilesSource(services),
        OnRepeatSource.ListKey => new OnRepeatSource(services),
        _ when key.StartsWith(DailyMixSource.Prefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(DailyMixSource.Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) =>
            new DailyMixSource(services, number),
        _ when key.StartsWith(AlbumSource.Prefix, StringComparison.Ordinal) => new AlbumSource(services, key[AlbumSource.Prefix.Length..]),
        _ when key.StartsWith(LikedByArtistSource.Prefix, StringComparison.Ordinal) => new LikedByArtistSource(services, key[LikedByArtistSource.Prefix.Length..]),
        _ => new PlaylistSource(services, key),
    };
}

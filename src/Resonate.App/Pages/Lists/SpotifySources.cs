using System.Globalization;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Pages.Lists;

/// <summary>Liked Songs, newest first.</summary>
public sealed class LikedSongsSource : TrackListSource
{
    public const string ListKey = "liked";

    private readonly AppServices _services;

    public LikedSongsSource(AppServices services) => _services = services;

    public override string Key => ListKey;

    public override string? ContextUri =>
        _services.Library.Snapshot?.User?.Id is { } userId ? $"spotify:user:{userId}:collection" : null;

    public override string EmptyText => "Songs you like appear here. Use the heart next to any song.";

    public override string OwnOrderName => "Recently added";

    public override ListHeader CachedHeader => new("COLLECTION", "Liked Songs", null, _services.Library.Snapshot?.User?.DisplayName, null, "Liked Songs", Glyph: "", AccentCover: true);

    public override Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        LoadAsync(_services.Library, cancellationToken);

    // The stored list shows at once; Spotify is asked only when there is none.
    public override async Task<IReadOnlyList<TrackInfo>?> LoadPreviewAsync(CancellationToken cancellationToken) =>
        _services.Library.GetStoredLikedSongs() ?? (await _services.Library.GetLikedSongsAsync(0, cancellationToken)).Tracks;

    private static async Task<FullTrackList> LoadAsync(LibraryService library, CancellationToken cancellationToken) =>
        new(await library.GetAllLikedSongsAsync(cancellationToken), ItemsHidden: false);
}

/// <summary>A Spotify playlist (in the sidebar, or opened from search or Home).</summary>
public sealed class PlaylistSource : TrackListSource
{
    private readonly AppServices _services;
    private readonly string _id;

    public PlaylistSource(AppServices services, string playlistId)
    {
        _services = services;
        _id = playlistId;
    }

    public override string Key => _id;

    public string PlaylistId => _id;

    public override string? ContextUri => Cached?.Uri ?? $"spotify:playlist:{_id}";

    public override bool CanEdit => _services.Library.CanEdit(_id);

    public override string EmptyText => "This playlist is empty.";

    public override ListHeader CachedHeader => Cached is { } playlist
        ? Header(playlist.Name, playlist.Description, playlist.Owner?.DisplayName ?? playlist.Owner?.Id, ImagePicker.Pick(playlist.Images, 300))
        : Header(string.Empty, null, null, null);

    private SimplifiedPlaylist? Cached => _services.Library.Snapshot?.Playlists.FirstOrDefault(p => p.Id == _id);

    public override async Task<ListHeader?> LoadHeaderAsync(CancellationToken cancellationToken)
    {
        if (Cached is not null)
        {
            // The sidebar's copy is current; asking again would cost a request.
            return null;
        }

        var details = await _services.Library.GetPlaylistAsync(_id, cancellationToken);
        return Header(details.Name, details.Description, details.Owner, details.ImageUrl);
    }

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        await _services.Library.GetAllPlaylistTracksAsync(_id, snapshotId: null, cancellationToken);

    public override async Task<IReadOnlyList<TrackInfo>?> LoadPreviewAsync(CancellationToken cancellationToken)
    {
        // The songs as last stored show at once, even if the playlist changed
        // since (rearranging waits for the current list); Spotify is asked only when there are none.
        if (_services.Library.PeekStoredPlaylistTracks(_id) is { Count: > 0 } stored)
        {
            return stored;
        }

        var page = await _services.Library.GetPlaylistTracksAsync(_id, 0, cancellationToken);
        return page.ItemsHidden ? null : page.Tracks;
    }

    public override Task MoveAsync(IReadOnlyList<TrackInfo> before, int from, int to, CancellationToken cancellationToken) =>
        _services.Library.MovePlaylistTrackAsync(_id, before, from, to, cancellationToken);

    public override Task RemoveAsync(IReadOnlyList<TrackInfo> before, TrackInfo track, CancellationToken cancellationToken) =>
        track.Uri is { } uri ? _services.Library.RemoveFromPlaylistAsync(_id, before, uri, cancellationToken) : Task.CompletedTask;

    private static ListHeader Header(string name, string? description, string? owner, string? imageUrl) =>
        new(
            "PLAYLIST",
            name,
            string.IsNullOrWhiteSpace(description) ? null : System.Net.WebUtility.HtmlDecode(description),
            owner,
            imageUrl,
            name.Length > 0 ? name : "Playlist");
}

/// <summary>An album, in track order.</summary>
public sealed class AlbumSource : TrackListSource
{
    public const string Prefix = "album:";

    private readonly AppServices _services;
    private readonly string _id;
    private Task<(Album Album, IReadOnlyList<TrackInfo> Tracks)>? _loading;

    public AlbumSource(AppServices services, string albumId)
    {
        _services = services;
        _id = albumId;
    }

    public override string Key => Prefix + _id;

    public override string? ContextUri => $"spotify:album:{_id}";

    public override bool IsAlbum => true;

    public override bool HasDateAdded => false;

    public override string OwnOrderName => "Album order";

    public override ListHeader CachedHeader => new("ALBUM", string.Empty, null, null, null, _id);

    public override async Task<ListHeader?> LoadHeaderAsync(CancellationToken cancellationToken)
    {
        var (album, tracks) = await Load(cancellationToken);
        var year = album.ReleaseDate is { Length: >= 4 } date ? date[..4] : null;
        var artists = album.Artists?.Select(a => new ArtistRef(a.Name, a.Id)).ToList() ?? [];
        return new ListHeader(
            (album.AlbumType ?? "album").ToUpperInvariant(),
            album.Name,
            null,
            string.Join(" · ", new[] { year }.OfType<string>()),
            ImagePicker.Pick(album.Images, 300),
            album.Name)
        {
            Artists = artists,
        };
    }

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        new((await Load(cancellationToken)).Tracks, ItemsHidden: false);

    private Task<(Album Album, IReadOnlyList<TrackInfo> Tracks)> Load(CancellationToken cancellationToken) =>
        _loading ??= _services.Library.GetAlbumAsync(_id, cancellationToken);
}

/// <summary>The songs in Liked Songs by one artist (from the artist's page).</summary>
public sealed class LikedByArtistSource : TrackListSource
{
    public const string Prefix = "liked-artist:";

    private readonly AppServices _services;
    private readonly string _artistId;
    private string _artistName = string.Empty;

    public LikedByArtistSource(AppServices services, string artistId)
    {
        _services = services;
        _artistId = artistId;
    }

    public override string Key => Prefix + _artistId;

    public override string EmptyText => "You have not liked any songs by this artist yet.";

    public override string OwnOrderName => "Recently added";

    public override ListHeader CachedHeader => Header();

    public override async Task<ListHeader?> LoadHeaderAsync(CancellationToken cancellationToken)
    {
        await LoadAllAsync(cancellationToken);
        return Header();
    }

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken)
    {
        var all = await _services.Library.GetAllLikedSongsAsync(cancellationToken);
        var mine = all.Where(t => t.ArtistRefs.Any(a => a.Id == _artistId)).ToList();
        if (mine.Count > 0)
        {
            _artistName = mine[0].ArtistRefs.First(a => a.Id == _artistId).Name;
        }

        return new FullTrackList(mine, ItemsHidden: false);
    }

    private ListHeader Header() =>
        new("LIKED SONGS", _artistName.Length > 0 ? _artistName : "Liked songs", null, null, null, "Liked Songs", Glyph: "\uEB52", AccentCover: true);
}

internal static class ListFormat
{
    /// <summary>"50 songs, 3 hr 12 min".</summary>
    public static string CountAndLength(IReadOnlyCollection<TrackInfo> tracks)
    {
        var total = TimeSpan.FromTicks(tracks.Sum(t => t.Duration.Ticks));
        var length = total.TotalHours >= 1
            ? string.Format(CultureInfo.CurrentCulture, "{0} hr {1} min", (int)total.TotalHours, total.Minutes)
            : string.Format(CultureInfo.CurrentCulture, "{0} min {1} sec", total.Minutes, total.Seconds);
        return tracks.Count == 0 ? Format.SongCount(0) : $"{Format.SongCount(tracks.Count)}, {length}";
    }
}

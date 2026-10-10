using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>A song the user has not saved, found for the DJ, and where it came from: a genre, or an artist.</summary>
public sealed record DjFind(TrackInfo Track, string Source, bool FromGenre);

/// <summary>
/// New music for the DJ near the user's taste (the owner asked for the DJ
/// to "mix in artists similar to the user's taste", 10 October 2026).
/// Spotify no longer gives new apps recommendations or related artists, so
/// the DJ looks in two places: songs in the genres of the user's top
/// artists (a search), mostly by artists the user has not saved, and songs
/// the user has not saved from an album of one of their top artists. At
/// most <see cref="MostRequests"/> requests, once a day (the caller keeps
/// the finds); a refusal ends the search with what was found.
/// </summary>
public static class DjDiscovery
{
    public const int MostRequests = 8;

    private const int Genres = 3;
    private const int Albums = 2;

    /// <summary>The finds, in no order; empty when Spotify could not be asked.</summary>
    public static async Task<IReadOnlyList<DjFind>> FindAsync(
        ISpotifyWebApi api,
        IReadOnlyCollection<TrackInfo> liked,
        IReadOnlyList<TopArtist> storedTop,
        Random random,
        CancellationToken cancellationToken)
    {
        var likedUris = new HashSet<string>(liked.Select(t => t.Uri).OfType<string>(), StringComparer.Ordinal);
        var likedArtists = new HashSet<string>(liked.SelectMany(t => t.ArtistRefs.Select(a => a.Name)), StringComparer.OrdinalIgnoreCase);
        var finds = new Dictionary<string, DjFind>(StringComparer.Ordinal);

        void Add(TrackInfo? track, string source, bool fromGenre)
        {
            // From a genre, only artists the user has not saved: new names near their taste.
            if (track is { Uri: { } uri, IsPlayable: true, IsLocal: false }
                && !likedUris.Contains(uri)
                && (!fromGenre || !likedArtists.Contains(track.PrimaryArtist)))
            {
                finds.TryAdd(uri, new DjFind(track, source, fromGenre));
            }
        }

        try
        {
            // The genres of the user's top artists lately, the most common first, picked at random among them.
            var top = await api.GetTopArtistsAsync(TopRange.ShortTerm, 0, 20, cancellationToken).ConfigureAwait(false);
            var artists = top.Items.OfType<Artist>().Where(a => a.Id.Length > 0).ToList();
            var genres = artists.SelectMany(a => a.Genres ?? []).GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key)
                .OrderBy(_ => random.Next()).Take(Genres).ToList();
            foreach (var genre in genres)
            {
                var found = await api.SearchAsync($"genre:\"{genre}\"", SearchTypes.Track, random.Next(0, 10) * 10, 10, cancellationToken).ConfigureAwait(false);
                foreach (var item in found.Tracks?.Items ?? [])
                {
                    Add(TrackInfo.From(item), genre, fromGenre: true);
                }
            }

            // Songs not saved yet from an album of a top artist.
            List<(string Id, string Name)> picks = artists.Count > 0
                ? [.. artists.Select(a => (a.Id, a.Name))]
                : [.. storedTop.Select(a => (a.Id, a.Name))];
            foreach (var (id, name) in picks.OrderBy(_ => random.Next()).Take(Albums))
            {
                var albums = await api.GetArtistAlbumsAsync(id, 0, 10, cancellationToken).ConfigureAwait(false);
                if (albums.Items.OfType<SimplifiedAlbum>().Where(a => a.Id is not null).OrderBy(_ => random.Next()).FirstOrDefault() is not { Id: { } albumId } album)
                {
                    continue;
                }

                var tracks = await api.GetAlbumTracksAsync(albumId, 0, 50, cancellationToken).ConfigureAwait(false);
                foreach (var item in tracks.Items)
                {
                    Add(TrackInfo.From(item, album: album), name, fromGenre: false);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or SpotifyApiException or TaskCanceledException)
        {
            // Refused or offline: the DJ plays from the library with what was found so far.
        }

        return [.. finds.Values];
    }
}

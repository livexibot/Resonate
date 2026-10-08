using System.Text.Json.Serialization;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.WebApi;

/// <summary>
/// Source-generated JSON (no reflection), so the app can be published with
/// Native AOT and trimming.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SpotifyUser))]
[JsonSerializable(typeof(Page<SimplifiedPlaylist>))]
[JsonSerializable(typeof(Page<SavedTrack>))]
[JsonSerializable(typeof(Page<PlaylistEntry>))]
[JsonSerializable(typeof(Playlist))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(DeviceList))]
[JsonSerializable(typeof(PlaybackState))]
[JsonSerializable(typeof(StartPlaybackBody))]
[JsonSerializable(typeof(TransferPlaybackBody))]
[JsonSerializable(typeof(ApiErrorEnvelope))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(OAuthError))]
[JsonSerializable(typeof(LibrarySnapshot))]
[JsonSerializable(typeof(Album))]
[JsonSerializable(typeof(Artist))]
[JsonSerializable(typeof(Page<PlayableItem>))]
[JsonSerializable(typeof(Page<SimplifiedAlbum>))]
[JsonSerializable(typeof(Page<Artist>))]
[JsonSerializable(typeof(PlayerQueue))]
[JsonSerializable(typeof(CursorPage<PlayHistoryItem>))]
[JsonSerializable(typeof(List<bool>))]
[JsonSerializable(typeof(ReorderItemsBody))]
[JsonSerializable(typeof(AddItemsBody))]
[JsonSerializable(typeof(RemoveItemsBody))]
[JsonSerializable(typeof(SnapshotResponse))]
[JsonSerializable(typeof(CreatePlaylistBody))]
[JsonSerializable(typeof(ChangePlaylistDetailsBody))]
[JsonSerializable(typeof(SimplifiedPlaylist))]
[JsonSerializable(typeof(CachedTrackList))]
[JsonSerializable(typeof(HistoryFile))]
[JsonSerializable(typeof(HomeContent))]
internal sealed partial class SpotifyJsonContext : JsonSerializerContext;

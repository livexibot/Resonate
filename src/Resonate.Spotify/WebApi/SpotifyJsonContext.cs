using System.Text.Json.Serialization;
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
internal sealed partial class SpotifyJsonContext : JsonSerializerContext;

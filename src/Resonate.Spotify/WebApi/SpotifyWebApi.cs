using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Resonate.Spotify.Auth;

namespace Resonate.Spotify.WebApi;

/// <summary>
/// A small client for the Spotify Web API. It renews the access token when
/// Spotify rejects it, waits briefly when Spotify asks it to slow down, and
/// never writes tokens anywhere.
/// </summary>
public sealed class SpotifyWebApi : ISpotifyWebApi
{
    /// <summary>Since February 2026, search returns at most 10 results per type and page.</summary>
    public const int MaxSearchLimit = 10;

    /// <summary>Most list endpoints return at most 50 entries per page.</summary>
    public const int MaxPageLimit = 50;

    /// <summary>The library endpoints (save, remove, contains) take at most 40 URIs per request.</summary>
    public const int MaxLibraryUris = 40;

    /// <summary>Adding or removing playlist songs takes at most 100 URIs per request.</summary>
    public const int MaxPlaylistUris = 100;

    /// <summary>Retry a rate-limited request by itself only when the wait is this short.</summary>
    private static readonly TimeSpan MaxAutomaticRetryWait = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly IAccessTokenSource _tokens;
    private readonly Uri _baseUri;
    private readonly TimeProvider _time;

    public SpotifyWebApi(HttpClient http, IAccessTokenSource tokens, Uri? baseUri = null, TimeProvider? time = null)
    {
        _http = http;
        _tokens = tokens;
        _baseUri = baseUri ?? new Uri("https://api.spotify.com/v1/");
        _time = time ?? TimeProvider.System;
    }

    public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        GetAsync("me", SpotifyJsonContext.Default.SpotifyUser, cancellationToken);

    public Task<Page<SimplifiedPlaylist>> GetMyPlaylistsAsync(int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync($"me/playlists?offset={offset}&limit={ClampPage(limit)}", SpotifyJsonContext.Default.PageSimplifiedPlaylist, cancellationToken);

    public Task<Page<SavedTrack>> GetSavedTracksAsync(int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync($"me/tracks?offset={offset}&limit={ClampPage(limit)}", SpotifyJsonContext.Default.PageSavedTrack, cancellationToken);

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken) =>
        GetAsync($"playlists/{Uri.EscapeDataString(playlistId)}", SpotifyJsonContext.Default.Playlist, cancellationToken);

    public Task<Page<PlaylistEntry>> GetPlaylistItemsAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync(
            $"playlists/{Uri.EscapeDataString(playlistId)}/items?offset={offset}&limit={ClampPage(limit)}&additional_types=track,episode",
            SpotifyJsonContext.Default.PagePlaylistEntry,
            cancellationToken);

    public Task<SearchResults> SearchAsync(string query, SearchTypes types, int offset, int limit, CancellationToken cancellationToken)
    {
        if (types == SearchTypes.None)
        {
            throw new ArgumentException("Choose at least one type to search for.", nameof(types));
        }

        var typeNames = new List<string>(4);
        if (types.HasFlag(SearchTypes.Track))
        {
            typeNames.Add("track");
        }

        if (types.HasFlag(SearchTypes.Album))
        {
            typeNames.Add("album");
        }

        if (types.HasFlag(SearchTypes.Artist))
        {
            typeNames.Add("artist");
        }

        if (types.HasFlag(SearchTypes.Playlist))
        {
            typeNames.Add("playlist");
        }

        var clamped = Math.Clamp(limit, 1, MaxSearchLimit);
        return GetAsync(
            $"search?q={Uri.EscapeDataString(query)}&type={string.Join(',', typeNames)}&offset={offset}&limit={clamped}",
            SpotifyJsonContext.Default.SearchResults,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var list = await GetAsync("me/player/devices", SpotifyJsonContext.Default.DeviceList, cancellationToken).ConfigureAwait(false);
        return list.Devices;
    }

    public async Task<PlaybackState?> GetPlaybackStateAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "me/player?additional_types=track,episode", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync(SpotifyJsonContext.Default.PlaybackState, cancellationToken).ConfigureAwait(false);
    }

    public Task StartPlaybackAsync(StartPlaybackBody? body, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            WithDevice("me/player/play", deviceId),
            body is null ? null : JsonContent.Create(body, SpotifyJsonContext.Default.StartPlaybackBody),
            cancellationToken);

    public Task PauseAsync(string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(HttpMethod.Put, WithDevice("me/player/pause", deviceId), null, cancellationToken);

    public Task SkipToNextAsync(string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(HttpMethod.Post, WithDevice("me/player/next", deviceId), null, cancellationToken);

    public Task SkipToPreviousAsync(string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(HttpMethod.Post, WithDevice("me/player/previous", deviceId), null, cancellationToken);

    public Task SeekAsync(TimeSpan position, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            WithDevice($"me/player/seek?position_ms={(long)Math.Max(0, position.TotalMilliseconds)}", deviceId),
            null,
            cancellationToken);

    public Task SetVolumeAsync(int percent, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            WithDevice($"me/player/volume?volume_percent={Math.Clamp(percent, 0, 100)}", deviceId),
            null,
            cancellationToken);

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            "me/player",
            JsonContent.Create(new TransferPlaybackBody { DeviceIds = [deviceId], Play = play }, SpotifyJsonContext.Default.TransferPlaybackBody),
            cancellationToken);

    public Task SetShuffleAsync(bool shuffle, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            WithDevice($"me/player/shuffle?state={(shuffle ? "true" : "false")}", deviceId),
            null,
            cancellationToken);

    public Task SetRepeatAsync(RepeatMode mode, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Put,
            WithDevice($"me/player/repeat?state={RepeatModes.ToSpotify(mode)}", deviceId),
            null,
            cancellationToken);

    public Task AddToQueueAsync(string uri, string? deviceId, CancellationToken cancellationToken) =>
        SendAndForgetAsync(
            HttpMethod.Post,
            WithDevice($"me/player/queue?uri={Uri.EscapeDataString(uri)}", deviceId),
            null,
            cancellationToken);

    public async Task<PlayerQueue> GetQueueAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "me/player/queue", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return new PlayerQueue();
        }

        return await response.Content.ReadFromJsonAsync(SpotifyJsonContext.Default.PlayerQueue, cancellationToken).ConfigureAwait(false)
            ?? new PlayerQueue();
    }

    public Task<CursorPage<PlayHistoryItem>> GetRecentlyPlayedAsync(int limit, DateTimeOffset? after, CancellationToken cancellationToken)
    {
        var path = $"me/player/recently-played?limit={ClampPage(limit)}";
        if (after is { } since)
        {
            path += $"&after={since.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
        }

        return GetAsync(path, SpotifyJsonContext.Default.CursorPagePlayHistoryItem, cancellationToken);
    }

    public Task<Page<Artist>> GetTopArtistsAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync($"me/top/artists?time_range={RangeName(range)}&offset={offset}&limit={ClampPage(limit)}", SpotifyJsonContext.Default.PageArtist, cancellationToken);

    public Task<Page<PlayableItem>> GetTopTracksAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync($"me/top/tracks?time_range={RangeName(range)}&offset={offset}&limit={ClampPage(limit)}", SpotifyJsonContext.Default.PagePlayableItem, cancellationToken);

    public async Task<IReadOnlyList<bool>> CheckLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken)
    {
        if (uris.Count == 0)
        {
            return [];
        }

        return await GetAsync($"me/library/contains?uris={JoinUris(uris, MaxLibraryUris)}", SpotifyJsonContext.Default.ListBoolean, cancellationToken)
            .ConfigureAwait(false);
    }

    // Spotify takes the URIs in the query string here, not in a JSON body.
    public Task SaveToLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) =>
        uris.Count == 0
            ? Task.CompletedTask
            : SendAndForgetAsync(HttpMethod.Put, $"me/library?uris={JoinUris(uris, MaxLibraryUris)}", null, cancellationToken);

    public Task RemoveFromLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) =>
        uris.Count == 0
            ? Task.CompletedTask
            : SendAndForgetAsync(HttpMethod.Delete, $"me/library?uris={JoinUris(uris, MaxLibraryUris)}", null, cancellationToken);

    public Task<string?> ReorderPlaylistItemsAsync(
        string playlistId,
        int rangeStart,
        int insertBefore,
        int rangeLength,
        string? snapshotId,
        CancellationToken cancellationToken) =>
        SendForSnapshotAsync(
            HttpMethod.Put,
            $"playlists/{Uri.EscapeDataString(playlistId)}/items",
            JsonContent.Create(
                new ReorderItemsBody { RangeStart = rangeStart, InsertBefore = insertBefore, RangeLength = rangeLength, SnapshotId = snapshotId },
                SpotifyJsonContext.Default.ReorderItemsBody),
            cancellationToken);

    public Task<string?> AddPlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, int? position, CancellationToken cancellationToken)
    {
        if (uris.Count > MaxPlaylistUris)
        {
            throw new ArgumentException($"At most {MaxPlaylistUris} songs per request.", nameof(uris));
        }

        return SendForSnapshotAsync(
            HttpMethod.Post,
            $"playlists/{Uri.EscapeDataString(playlistId)}/items",
            JsonContent.Create(new AddItemsBody { Uris = [.. uris], Position = position }, SpotifyJsonContext.Default.AddItemsBody),
            cancellationToken);
    }

    public Task<string?> RemovePlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, string? snapshotId, CancellationToken cancellationToken)
    {
        if (uris.Count > MaxPlaylistUris)
        {
            throw new ArgumentException($"At most {MaxPlaylistUris} songs per request.", nameof(uris));
        }

        return SendForSnapshotAsync(
            HttpMethod.Delete,
            $"playlists/{Uri.EscapeDataString(playlistId)}/items",
            JsonContent.Create(
                new RemoveItemsBody { Items = [.. uris.Select(u => new ItemReference { Uri = u })], SnapshotId = snapshotId },
                SpotifyJsonContext.Default.RemoveItemsBody),
            cancellationToken);
    }

    public async Task<SimplifiedPlaylist> CreatePlaylistAsync(string name, string? description, bool isPublic, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "me/playlists",
            JsonContent.Create(new CreatePlaylistBody { Name = name, Description = description, Public = isPublic }, SpotifyJsonContext.Default.CreatePlaylistBody),
            cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(SpotifyJsonContext.Default.SimplifiedPlaylist, cancellationToken).ConfigureAwait(false)
            ?? throw new SpotifyApiException(response.StatusCode, null, "Spotify sent an empty answer.");
    }

    public Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken) =>
        GetAsync($"albums/{Uri.EscapeDataString(albumId)}", SpotifyJsonContext.Default.Album, cancellationToken);

    public Task<Page<PlayableItem>> GetAlbumTracksAsync(string albumId, int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync($"albums/{Uri.EscapeDataString(albumId)}/tracks?offset={offset}&limit={ClampPage(limit)}", SpotifyJsonContext.Default.PagePlayableItem, cancellationToken);

    public Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken) =>
        GetAsync($"artists/{Uri.EscapeDataString(artistId)}", SpotifyJsonContext.Default.Artist, cancellationToken);

    public Task<Page<SimplifiedAlbum>> GetArtistAlbumsAsync(string artistId, int offset, int limit, CancellationToken cancellationToken) =>
        GetAsync(
            $"artists/{Uri.EscapeDataString(artistId)}/albums?include_groups=album,single,compilation&offset={offset}&limit={ClampPage(limit)}",
            SpotifyJsonContext.Default.PageSimplifiedAlbum,
            cancellationToken);

    private static string RangeName(TopRange range) => range switch
    {
        TopRange.ShortTerm => "short_term",
        TopRange.LongTerm => "long_term",
        _ => "medium_term",
    };

    private static string JoinUris(IReadOnlyList<string> uris, int max)
    {
        if (uris.Count > max)
        {
            throw new ArgumentException($"At most {max} URIs per request.", nameof(uris));
        }

        return string.Join(',', uris.Select(Uri.EscapeDataString));
    }

    private async Task<string?> SendForSnapshotAsync(HttpMethod method, string path, HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, content, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        try
        {
            var snapshot = await response.Content.ReadFromJsonAsync(SpotifyJsonContext.Default.SnapshotResponse, cancellationToken).ConfigureAwait(false);
            return snapshot?.SnapshotId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ClampPage(int limit) => Math.Clamp(limit, 1, MaxPageLimit);

    private static string WithDevice(string path, string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return path;
        }

        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}device_id={Uri.EscapeDataString(deviceId)}";
    }

    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        return value ?? throw new SpotifyApiException(response.StatusCode, null, "Spotify sent an empty answer.");
    }

    private async Task SendAndForgetAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        // Buffer the body once so a retry can send it again.
        byte[]? body = null;
        MediaTypeHeaderValue? contentType = null;
        if (content is not null)
        {
            body = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            contentType = content.Headers.ContentType;
            content.Dispose();
        }

        string? rejectedToken = null;
        var rateLimitRetried = false;
        while (true)
        {
            var token = await _tokens.GetAccessTokenAsync(rejectedToken, cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = contentType;
            }
            else if (method == HttpMethod.Put || method == HttpMethod.Post)
            {
                // Spotify expects a Content-Length on bodiless PUT and POST.
                request.Content = new ByteArrayContent([]);
            }

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && rejectedToken is null)
                {
                    rejectedToken = token;
                    continue;
                }

                var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
                var retryAfter = GetRetryAfter(response);

                if (response.StatusCode == HttpStatusCode.TooManyRequests
                    && error?.Reason != "QUOTA_EXCEEDED"
                    && !rateLimitRetried
                    && retryAfter is { } wait
                    && wait <= MaxAutomaticRetryWait)
                {
                    rateLimitRetried = true;
                    await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new SpotifyApiException(
                    response.StatusCode,
                    error?.Reason,
                    error?.Message ?? $"Spotify answered {(int)response.StatusCode}.",
                    retryAfter);
            }
        }
    }

    private static async Task<ApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync(SpotifyJsonContext.Default.ApiErrorEnvelope, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}

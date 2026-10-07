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

using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public class SpotifyWebApiTests
{
    private readonly FakeHttpHandler _handler = new();
    private readonly CountingTokens _tokens = new();

    [Fact]
    public async Task Sends_the_access_token_as_a_bearer_header()
    {
        _handler.Respond(HttpStatusCode.OK, """{"id":"me","display_name":"Me","uri":"spotify:user:me"}""");

        var user = await Api().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Me", user.DisplayName);
        Assert.Equal("Bearer token-1", _handler.Requests[0].Authorization);
        Assert.Equal("https://api.spotify.com/v1/me", _handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Renews_the_token_once_when_Spotify_rejects_it()
    {
        _handler
            .Respond(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"The access token expired"}}""")
            .Respond(HttpStatusCode.OK, """{"id":"me","uri":"spotify:user:me"}""");

        await Api().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Bearer token-1", "Bearer token-2"], _handler.Requests.Select(r => r.Authorization));
        Assert.Equal("token-1", _tokens.LastRejected);
    }

    [Fact]
    public async Task Reads_playlist_entries_in_the_2026_shape()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {
              "id": "p1", "name": "Mine", "uri": "spotify:playlist:p1",
              "owner": { "id": "me", "display_name": "Me" },
              "items": {
                "items": [
                  { "added_at": "2026-01-01T00:00:00Z", "item": {
                      "id": "t1", "name": "Song", "uri": "spotify:track:t1", "type": "track",
                      "duration_ms": 200000, "artists": [ { "name": "A" }, { "name": "B" } ],
                      "album": { "name": "Album", "uri": "spotify:album:a1", "images": [ { "url": "big", "width": 640 }, { "url": "small", "width": 64 } ] } } },
                  { "added_at": "2026-01-01T00:00:00Z", "item": null }
                ],
                "total": 2, "limit": 50, "offset": 0, "next": null
              }
            }
            """);

        var playlist = await Api().GetPlaylistAsync("p1", TestContext.Current.CancellationToken);

        var entry = Assert.Single(playlist.Entries!.Items, e => e?.Playable is not null);
        Assert.Equal("Song", entry!.Playable!.Name);
        Assert.Equal(2, playlist.Entries.Total);
    }

    [Fact]
    public async Task Reads_playlist_entries_in_the_old_shape_too()
    {
        _handler.Respond(HttpStatusCode.OK, """
            { "items": [ { "track": { "name": "Old", "uri": "spotify:track:o", "duration_ms": 1 } } ], "total": 1, "next": "https://next" }
            """);

        var page = await Api().GetPlaylistItemsAsync("p1", 0, 50, TestContext.Current.CancellationToken);

        Assert.Equal("Old", page.Items[0]!.Playable!.Name);
        Assert.True(page.HasMore);
        Assert.Contains("/playlists/p1/items?", _handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_asks_for_no_more_than_ten_results()
    {
        _handler.Respond(HttpStatusCode.OK, """{"tracks":{"items":[],"total":0},"playlists":{"items":[null],"total":1}}""");

        var results = await Api().SearchAsync("daft punk", SearchTypes.Track | SearchTypes.Playlist, 0, 50, TestContext.Current.CancellationToken);

        var uri = _handler.Requests[0].Uri.AbsoluteUri;
        Assert.Contains("q=daft%20punk", uri, StringComparison.Ordinal);
        Assert.Contains("type=track,playlist", uri, StringComparison.Ordinal);
        Assert.Contains("limit=10", uri, StringComparison.Ordinal);
        Assert.Null(results.Playlists!.Items[0]);
    }

    [Fact]
    public async Task Asks_for_no_more_than_ten_of_an_artists_releases()
    {
        _handler.Respond(HttpStatusCode.OK, """{"items":[],"total":0}""");

        await Api().GetArtistAlbumsAsync("abc", 20, 50, TestContext.Current.CancellationToken);

        var uri = _handler.Requests[0].Uri.AbsoluteUri;
        Assert.Contains("artists/abc/albums?", uri, StringComparison.Ordinal);
        Assert.Contains("offset=20&limit=10", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starts_a_song_in_its_playlist_on_the_chosen_device()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        await Api().StartPlaybackAsync(
            new StartPlaybackBody { ContextUri = "spotify:playlist:p1", Offset = new PlaybackOffset { Uri = "spotify:track:t1" } },
            "device 1",
            TestContext.Current.CancellationToken);

        var request = _handler.Requests[0];
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/play?device_id=device%201", request.Uri.AbsoluteUri);
        Assert.Equal("""{"context_uri":"spotify:playlist:p1","offset":{"uri":"spotify:track:t1"}}""", request.Body);
    }

    [Fact]
    public async Task Bodiless_commands_still_send_an_empty_body()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        await Api().SkipToNextAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, _handler.Requests[0].Method);
        Assert.Equal(string.Empty, _handler.Requests[0].Body);
    }

    [Fact]
    public async Task Nothing_playing_is_null()
    {
        _handler.Respond(HttpStatusCode.NoContent);

        Assert.Null(await Api().GetPlaybackStateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Waits_out_a_short_rate_limit_and_retries()
    {
        var time = new FakeTimeProvider();
        _handler
            .Respond(HttpStatusCode.TooManyRequests, null, r => r.Headers.Add("Retry-After", "1"))
            .Respond(HttpStatusCode.OK, """{"devices":[{"id":"d","name":"PC","type":"Computer"}]}""");

        var call = Api(time).GetDevicesAsync(TestContext.Current.CancellationToken);
        while (_handler.Requests.Count < 1)
        {
            await Task.Yield();
        }

        await Task.Delay(50, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));

        var devices = await call;
        Assert.Equal("PC", Assert.Single(devices).Name);
    }

    [Fact]
    public async Task Exhausted_quota_is_reported_without_retrying()
    {
        _handler.Respond(
            HttpStatusCode.TooManyRequests,
            """{"error":{"status":429,"message":"Quota exceeded","reason":"QUOTA_EXCEEDED"}}""",
            r => r.Headers.Add("Retry-After", "1"));

        var ex = await Assert.ThrowsAsync<SpotifyApiException>(
            () => Api().GetDevicesAsync(TestContext.Current.CancellationToken));

        Assert.True(ex.IsQuotaExceeded);
        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(9674, "Your Spotify developer app has used up its request allowance. Spotify allows more in about 2 h 42 min.")]
    [InlineData(3600, "Your Spotify developer app has used up its request allowance. Spotify allows more in about 1 h.")]
    [InlineData(600, "Your Spotify developer app has used up its request allowance. Spotify allows more in about 10 min.")]
    [InlineData(5, "Your Spotify developer app has used up its request allowance for now. Try again later.")]
    public void Exhausted_quota_says_when_Spotify_allows_more(int seconds, string expected) =>
        Assert.Equal(expected, new SpotifyApiException(HttpStatusCode.TooManyRequests, "QUOTA_EXCEEDED", "Quota exceeded", TimeSpan.FromSeconds(seconds)).UserMessage);

    [Fact]
    public async Task Premium_only_errors_carry_their_reason()
    {
        _handler.Respond(
            HttpStatusCode.Forbidden,
            """{"error":{"status":403,"message":"Player command failed: Premium required","reason":"PREMIUM_REQUIRED"}}""");

        var ex = await Assert.ThrowsAsync<SpotifyApiException>(
            () => Api().PauseAsync(null, TestContext.Current.CancellationToken));

        Assert.True(ex.IsPremiumRequired);
        Assert.Contains("Premium", ex.UserMessage, StringComparison.Ordinal);
    }

    private SpotifyWebApi Api(TimeProvider? time = null) => new(new HttpClient(_handler), _tokens, time: time);

    private sealed class CountingTokens : IAccessTokenSource
    {
        private int _issued = 1;

        public string? LastRejected { get; private set; }

        public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
        {
            if (rejectedToken is not null)
            {
                LastRejected = rejectedToken;
                _issued++;
            }

            return Task.FromResult($"token-{_issued}");
        }
    }
}

using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class RequestCounterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-requests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public RequestCounterTests()
    {
        Directory.CreateDirectory(_folder);
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("GET", "me/player?additional_types=track,episode", RequestKind.WhatPlays)]
    [InlineData("PUT", "me/player", RequestKind.PlayerCommands)]
    [InlineData("PUT", "me/player/play?device_id=x", RequestKind.PlayerCommands)]
    [InlineData("POST", "me/player/next", RequestKind.PlayerCommands)]
    [InlineData("POST", "me/player/queue?uri=spotify%3Atrack%3Ab", RequestKind.PlayerCommands)]
    [InlineData("GET", "me/player/queue", RequestKind.Queue)]
    [InlineData("GET", "me/player/devices", RequestKind.Devices)]
    [InlineData("GET", "me/player/recently-played?limit=50", RequestKind.History)]
    [InlineData("GET", "me/playlists?offset=0&limit=50", RequestKind.Library)]
    [InlineData("GET", "me/tracks?offset=0&limit=50", RequestKind.Library)]
    [InlineData("GET", "playlists/abc/items?offset=0", RequestKind.Library)]
    [InlineData("GET", "search?q=a&type=track", RequestKind.Search)]
    [InlineData("GET", "albums/abc", RequestKind.AlbumsAndArtists)]
    [InlineData("GET", "artists/abc/albums", RequestKind.AlbumsAndArtists)]
    [InlineData("GET", "me/top/tracks?time_range=short_term", RequestKind.YourTop)]
    [InlineData("GET", "me", RequestKind.Account)]
    public void Each_request_is_put_under_what_it_was_for(string method, string path, RequestKind expected) =>
        Assert.Equal(expected, RequestCounter.KindOf(new HttpMethod(method), path));

    [Fact]
    public void Counts_today_by_kind_and_in_total_and_keeps_them_on_disk()
    {
        var path = Path.Combine(_folder, "requests.json");
        var counter = new RequestCounter(path, _time);
        counter.Count(HttpMethod.Get, "me/player");
        counter.Count(HttpMethod.Get, "me/player");
        counter.Count(HttpMethod.Get, "search?q=a");
        counter.Save();

        var today = counter.Today(out var total);
        Assert.Equal(3, total);
        Assert.Equal([(RequestKind.WhatPlays, 2L), (RequestKind.Search, 1L)], today);

        // The next day starts at nothing; the total goes on, also after a restart.
        _time.Advance(TimeSpan.FromDays(1));
        var again = new RequestCounter(path, _time);
        again.Count(HttpMethod.Get, "me");
        Assert.Empty(new RequestCounter(null, _time).Today(out _));
        again.Today(out var todayTotal);
        Assert.Equal(1, todayTotal);
        Assert.Equal(4, again.Total);
        Assert.Equal(new DateOnly(2026, 10, 10), again.Since);
    }
}

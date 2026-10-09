using Resonate.Spotify.Library;

namespace Resonate.Spotify.Tests;

public sealed class SongStatsTests
{
    // ReccoBeats' answer for two songs, as it came on 9 October 2026 (some fields left out).
    private const string Answer = """
        {"content":[
          {"id":"b0722e89-94fe-44ea-83f7-f15e5f4a0ef6","href":"https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8","isrc":"GBARL9300135","energy":0.939,"key":8,"loudness":-11.823,"mode":1,"tempo":113.309,"valence":0.914},
          {"id":"636463ea-c943-44d5-b600-1fa6f3ad8057","href":"https://open.spotify.com/track/3n3Ppam7vgaVa1iaRUc9Lp","energy":0.918,"key":1,"loudness":-4.36,"mode":0,"tempo":148.114}
        ]}
        """;

    [Fact]
    public void Reads_ReccoBeats_answer_by_Spotify_ID()
    {
        var found = ReccoBeatsClient.Parse(Answer).ToDictionary(f => f.Id, f => f.Stats);

        Assert.Equal(2, found.Count);
        var first = found["4PTG3Z6ehGkBFwjybzWkR8"];
        Assert.Equal("113", first.TempoText);
        Assert.Equal("G♯", first.KeyText);
        Assert.Equal(-11.823, first.Loudness, 3);
        Assert.Equal("C♯m", found["3n3Ppam7vgaVa1iaRUc9Lp"].KeyText);
    }

    [Theory]
    [InlineData("4PTG3Z6ehGkBFwjybzWkR8", true)]
    [InlineData("local:file", false)]
    [InlineData("4PTG3Z6ehGkBFwjybzWkR", false)]
    [InlineData("4PTG3Z6ehGkBFwjybzWkR8/", false)]
    public void Only_Spotify_track_IDs_are_sent(string id, bool sent) =>
        Assert.Equal(sent, ReccoBeatsClient.IsSpotifyId(id));

    [Fact]
    public void An_unknown_song_is_asked_about_again_after_a_while()
    {
        var now = DateTimeOffset.Parse("2026-10-09T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var cache = new SongStatsCache(path: null);
        cache.Store("4PTG3Z6ehGkBFwjybzWkR8", null, now);
        cache.Store("3n3Ppam7vgaVa1iaRUc9Lp", new SongStats(148, 1, 0, -4.4, 0.9), now);

        Assert.True(cache.TryGet("4PTG3Z6ehGkBFwjybzWkR8", now.AddDays(1), out var unknown));
        Assert.Null(unknown);
        Assert.False(cache.TryGet("4PTG3Z6ehGkBFwjybzWkR8", now + SongStatsCache.RetryUnknownAfter, out _));
        Assert.True(cache.TryGet("3n3Ppam7vgaVa1iaRUc9Lp", now.AddYears(1), out var known));
        Assert.Equal("148", known!.TempoText);
    }
}

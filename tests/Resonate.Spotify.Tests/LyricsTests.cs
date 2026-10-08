using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Lyrics;

namespace Resonate.Spotify.Tests;

public sealed class LyricsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-lyrics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Song (Remastered 2011)", "Song")]
    [InlineData("Song - Live at Wembley", "Song")]
    [InlineData("Song - 2009 Remaster", "Song")]
    [InlineData("Song - 2011 Version", "Song")]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song feat. Someone", "Song")]
    [InlineData("Song ft. Someone", "Song")]
    [InlineData("Song (Part One)", "Song (Part One)")]
    [InlineData("Hyphen - Ated", "Hyphen - Ated")]
    [InlineData("Left Behind", "Left Behind")]
    [InlineData("(Remastered)", "(Remastered)")]
    [InlineData("Feat\u200Bure", "Feature")]
    [InlineData("\u0130\u0130 feat. Someone", "\u0130\u0130")]
    [InlineData("\u1E9E caf\u00E9 feat. Someone", "\u1E9E caf\u00E9")]
    public void Titles_lose_what_a_lyrics_library_leaves_out(string title, string expected) =>
        Assert.Equal(expected, LyricsText.CleanTitle(title));

    [Theory]
    [InlineData("TOOL;Tool", "TOOL")]
    [InlineData("Artist feat. Guest", "Artist")]
    [InlineData("Artist (feat. Guest)", "Artist")]
    [InlineData("Artist featuring Other", "Artist")]
    [InlineData("First, Second", "First")]
    [InlineData("Beyonc\u00E9", "Beyonc\u00E9")]
    public void Artists_keep_their_first_name(string artist, string expected) =>
        Assert.Equal(expected, LyricsText.CleanArtist(artist));

    [Fact]
    public void Matching_is_loose_about_case_accents_and_punctuation()
    {
        Assert.True(LyricsText.LooseMatch("Beyonc\u00E9", "beyonce"));
        Assert.True(LyricsText.LooseMatch("Rock & Roll", "rock and roll"));
        Assert.True(LyricsText.LooseMatch("Don't Stop", "dont stop"));
        Assert.True(LyricsText.LooseMatch("Song (Live)", "Song"));
        Assert.True(LyricsText.LooseMatch("Tyler, The Creator", "Tyler"));
        Assert.False(LyricsText.LooseMatch("Something", "Else"));
        Assert.False(LyricsText.LooseMatch(string.Empty, "Else"));
    }

    [Fact]
    public void LRC_lines_parse_with_several_stamps_and_sort()
    {
        var lines = LyricsText.ParseLrc("[ar:Someone]\r\n[00:12.50]First\n[00:05]Early\n[01:00.1][02:00.123]Twice\n\nNo stamp\n[00:20.]Broken\n[00:30.00]\n");

        Assert.Equal(
            [5_000, 12_500, 30_000, 60_100, 120_123],
            lines.Select(l => (int)l.At!.Value.TotalMilliseconds));
        Assert.Equal(["Early", "First", string.Empty, "Twice", "Twice"], lines.Select(l => l.Text));
    }

    [Fact]
    public void The_active_line_is_the_last_one_started()
    {
        var lyrics = new SongLyrics(LyricsText.ParseLrc("[00:05]a\n[00:10]b\n[00:15]c"), synced: true);

        Assert.Equal(-1, lyrics.ActiveLine(TimeSpan.Zero));
        Assert.Equal(0, lyrics.ActiveLine(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, lyrics.ActiveLine(TimeSpan.FromSeconds(12)));
        Assert.Equal(2, lyrics.ActiveLine(TimeSpan.FromSeconds(99)));

        var plain = new SongLyrics([new LyricLine(null, "a")], synced: true);
        Assert.False(plain.IsSynced);
        Assert.Equal(-1, plain.ActiveLine(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void The_closest_length_wins_and_synced_breaks_ties()
    {
        var query = new LyricsQuery("Artist", "Song", string.Empty, TimeSpan.FromSeconds(200));

        var picked = LrcLibClient.Pick(
            [Record("Song", "Artist", 260, synced: true), Record("Song", "Artist", 201, synced: false), Record("Song", "Artist", 202, synced: true), Record("Other", "Artist", 200, synced: true)],
            query);

        Assert.Equal(202, picked!.Duration);
        Assert.Null(LrcLibClient.Pick([Record("Song", "Artist", 400, synced: true)], query));
    }

    [Fact]
    public void Records_turn_into_lyrics()
    {
        var instrumental = Record("Song", "Artist", 100, synced: false);
        instrumental.Instrumental = true;

        Assert.True(instrumental.ToLyrics()!.IsInstrumental);
        Assert.True(Record("Song", "Artist", 100, synced: true).ToLyrics()!.IsSynced);
        var plain = Record("Song", "Artist", 100, synced: false).ToLyrics()!;
        Assert.False(plain.IsSynced);
        Assert.Single(plain.Lines);
        Assert.Null(new LrcLibRecord().ToLyrics());
    }

    [Fact]
    public async Task Asks_for_the_exact_song_first_and_names_itself()
    {
        var handler = new LrcLibServer().Answer(HttpStatusCode.OK, """{"trackName":"Song","artistName":"Artist","duration":201.0,"instrumental":false,"syncedLyrics":"[00:01.00] Hello\n[00:03.00] World"}""");
        using var http = new HttpClient(handler);
        var client = new LrcLibClient(http, LrcLibClient.UserAgentFor("1.2.3"));

        var lyrics = await client.FindAsync(new LyricsQuery("Artist, Guest", "Song (Remastered 2011)", "Album & More", TimeSpan.FromSeconds(200.6)), CancellationToken.None);

        Assert.True(lyrics!.IsSynced);
        Assert.Equal(["Hello", "World"], lyrics.Lines.Select(l => l.Text));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://lrclib.net/api/get?artist_name=Artist&track_name=Song&album_name=Album%20%26%20More&duration=201", request.Uri.AbsoluteUri);
        Assert.Equal("Resonate/1.2.3 (https://github.com/livexibot/Resonate)", request.UserAgent);
    }

    [Fact]
    public async Task Searches_when_the_exact_lookup_finds_nothing()
    {
        var handler = new LrcLibServer()
            .Answer(HttpStatusCode.NotFound, """{"code":404}""")
            .Answer(HttpStatusCode.OK, """[{"trackName":"Song","artistName":"Someone else","duration":180,"plainLyrics":"x"},{"trackName":"Song (Live)","artistName":"Artist","duration":181,"plainLyrics":"Line one\nLine two"}]""");
        using var http = new HttpClient(handler);
        var client = new LrcLibClient(http);

        var lyrics = await client.FindAsync(new LyricsQuery("Artist", "Song", string.Empty, TimeSpan.FromSeconds(180)), CancellationToken.None);

        Assert.False(lyrics!.IsSynced);
        Assert.Equal(["Line one", "Line two"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal("https://lrclib.net/api/search?artist_name=Artist&track_name=Song", handler.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Nothing_found_is_null_and_a_server_error_throws()
    {
        var handler = new LrcLibServer().Answer(HttpStatusCode.NotFound).Answer(HttpStatusCode.OK, "[]").Answer(HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);
        var client = new LrcLibClient(http);
        var query = new LyricsQuery("Artist", "Song", string.Empty, TimeSpan.Zero);

        Assert.Null(await client.FindAsync(query, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.FindAsync(query, CancellationToken.None));
    }

    [Fact]
    public void A_song_without_a_title_or_artist_is_not_looked_up()
    {
        Assert.Null(LyricsQuery.For("Song", " ", "Album", TimeSpan.Zero));
        Assert.Null(LyricsQuery.For(null, "Artist", "Album", TimeSpan.Zero));
        Assert.Equal(new LyricsQuery("Artist", "Song", string.Empty, TimeSpan.Zero), LyricsQuery.For(" Song ", "Artist", null, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task Answers_are_kept_for_30_days_none_included_but_failures_are_not()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-08T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var source = new CountingSource();
        var library = new LyricsLibrary(source, new LyricsCache(_folder, time));
        var found = new LyricsQuery("Artist", "Song", "Album", TimeSpan.FromSeconds(200));
        var none = new LyricsQuery("Artist", "Nothing", "Album", TimeSpan.FromSeconds(200));
        var failing = new LyricsQuery("Artist", "Offline", "Album", TimeSpan.FromSeconds(200));

        var first = await library.GetAsync(found, CancellationToken.None);
        var again = await library.GetAsync(found, CancellationToken.None);
        Assert.Null(await library.GetAsync(none, CancellationToken.None));
        Assert.Null(await library.GetAsync(none, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => library.GetAsync(failing, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => library.GetAsync(failing, CancellationToken.None));

        Assert.Equal(2, source.Calls[found.Title] + source.Calls[none.Title]);
        Assert.Equal(2, source.Calls[failing.Title]);
        Assert.True(again!.IsSynced);
        Assert.Equal(first!.Lines, again.Lines);

        time.Advance(LyricsCache.Lifetime);
        await library.GetAsync(found, CancellationToken.None);
        Assert.Equal(2, source.Calls[found.Title]);
    }

    [Fact]
    public void Old_answers_are_pruned()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new LyricsCache(_folder, time);
        var query = new LyricsQuery("Artist", "Song", string.Empty, TimeSpan.Zero);
        cache.Store(query, SongLyrics.Instrumental);
        Assert.True(cache.TryGet(query, out var kept));
        Assert.True(kept!.IsInstrumental);

        File.SetLastWriteTimeUtc(cache.PathFor(query), DateTime.UtcNow - LyricsCache.Lifetime - TimeSpan.FromDays(1));
        cache.Prune();

        Assert.False(File.Exists(cache.PathFor(query)));
    }

    private static LrcLibRecord Record(string track, string artist, double duration, bool synced) => new()
    {
        TrackName = track,
        ArtistName = artist,
        Duration = duration,
        SyncedLyrics = synced ? "[00:01.00] a" : null,
        PlainLyrics = "a",
    };

    private sealed class CountingSource : ILyricsSource
    {
        public Dictionary<string, int> Calls { get; } = [];

        public Task<SongLyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            Calls[query.Title] = Calls.GetValueOrDefault(query.Title) + 1;
            return query.Title switch
            {
                "Song" => Task.FromResult<SongLyrics?>(new SongLyrics(LyricsText.ParseLrc("[00:01]a\n[00:02]b"), synced: true)),
                "Offline" => Task.FromException<SongLyrics?>(new HttpRequestException("offline")),
                _ => Task.FromResult<SongLyrics?>(null),
            };
        }
    }

    /// <summary>Answers in order and keeps what was asked, User-Agent included.</summary>
    private sealed class LrcLibServer : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string? Json)> _answers = new();

        public List<(Uri Uri, string? UserAgent)> Requests { get; } = [];

        public LrcLibServer Answer(HttpStatusCode status, string? json = null)
        {
            _answers.Enqueue((status, json));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.TryGetValues("User-Agent", out var agent) ? string.Join(' ', agent) : null));
            var (status, json) = _answers.Dequeue();
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}

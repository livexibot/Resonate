using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Lyrics;

namespace Resonate.Spotify.Tests;

/// <summary>Line Finder: a half-remembered line finds the song, the line and when it is sung.</summary>
public sealed class LineFinderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-lines-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string IndexFile => Path.Combine(_folder, "line-finder.jsonl");

    [Theory]
    [InlineData("Don't Stop Believin'", "dont stop believin")]
    [InlineData("Café, déjà vu!", "cafe deja vu")]
    [InlineData("  Hello\tWORLD  ", "hello world")]
    [InlineData("(Oh-oh) — yeah…", "oh oh yeah")]
    [InlineData("東京の夜", "東京の夜")]
    [InlineData("", "")]
    public void Words_are_folded_so_any_way_of_typing_them_matches(string text, string expected) =>
        Assert.Equal(expected, LineText.Fold(text));

    [Theory]
    [InlineData("names", "name")]
    [InlineData("walked", "walk")]
    [InlineData("running", "runn")]
    [InlineData("kiss", null)]
    [InlineData("bus", null)]
    [InlineData("sing", null)]
    [InlineData("cafés", null)]
    public void Common_endings_come_off_for_the_stem(string word, string? expected) =>
        Assert.Equal(expected, LineText.Stem(word));

    [Fact]
    public void A_half_remembered_line_finds_the_closest_lines_first()
    {
        var index = Index(
            Song(1, "Static Letters", "Every signal fading out", "We wrote our names in the static", "Like it would never wash away"),
            Song(2, "Paper Weather", "Folded maps and borrowed time", "You wrote our names on the window", "Before the rain came in"),
            Song(3, "Hollow Stars", "Under hollow stars we lay", "I wrote your name in the dust", "And the wind forgot it by the morning"),
            Song(4, "Blue Machines", "City in a pocket of light", "Names we wrote in neon blue", "Running through the wires"),
            Song(5, "Elsewhere", "Nothing here is like the rest", "Only rivers and the sea"));

        var found = index.Search("wrote our names", 10, TestContext.Current.CancellationToken);

        Assert.Equal(["Static Letters", "Paper Weather", "Hollow Stars", "Blue Machines"], found.Select(f => f.Track.Title));
        var best = found[0];
        Assert.Equal("We wrote our names in the static", best.Line);
        Assert.Equal("Every signal fading out", best.Before);
        Assert.Equal("Like it would never wash away", best.After);
        Assert.Equal(TimeSpan.FromSeconds(12), best.At);
        Assert.Equal(["wrote", "our", "names"], best.Marks.Select(m => best.Line.Substring(m.Start, m.Length)));

        var stem = found[2];
        Assert.Equal(["wrote", "name"], stem.Marks.Select(m => stem.Line.Substring(m.Start, m.Length)));
    }

    [Fact]
    public void Words_typed_without_their_accents_or_apostrophes_still_match()
    {
        var index = Index(Song(1, "Cafe", "Meet me at the café", "Don't you wait for me"));

        var found = Assert.Single(index.Search("dont you wait", 10, TestContext.Current.CancellationToken));

        Assert.Equal("Don't you wait for me", found.Line);
        Assert.Equal(["Don't", "you", "wait"], found.Marks.Select(m => found.Line.Substring(m.Start, m.Length)));
        Assert.Equal("Meet me at the café", Assert.Single(index.Search("meet me at the cafe", 10, TestContext.Current.CancellationToken)).Line);
    }

    [Fact]
    public void A_line_typed_across_a_line_break_starts_at_its_first_half()
    {
        var index = Index(Song(1, "Signal", "Every signal fading out", "We wrote our names in the static", "Like it would never wash away"));

        var found = Assert.Single(index.Search("signal fading out we wrote", 10, TestContext.Current.CancellationToken));

        Assert.Equal("Every signal fading out", found.Line);
        Assert.Equal(TimeSpan.FromSeconds(6), found.At);
    }

    [Fact]
    public void A_chorus_starts_where_it_is_first_sung()
    {
        var index = Index(Song(1, "Chorus", "Hold on tonight", "The night is young", "Hold on tonight", "We run"));

        var found = Assert.Single(index.Search("hold on tonight", 10, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(6), found.At);
        Assert.Null(found.Before);
    }

    [Fact]
    public void A_line_that_holds_too_little_of_what_was_typed_is_left_out()
    {
        var index = Index(
            Song(1, "Umbrella", "Under my umbrella", "Rain keeps falling"),
            Song(2, "Sunny", "Bright and sunny days", "Nothing but the light"));

        Assert.Equal("Under my umbrella", Assert.Single(index.Search("umbrella", 10, TestContext.Current.CancellationToken)).Line);
        Assert.Empty(index.Search("xylophone", 10, TestContext.Current.CancellationToken));
        Assert.Empty(index.Search("umbrella xylophone", 10, TestContext.Current.CancellationToken));
        Assert.Empty(index.Search("", 10, TestContext.Current.CancellationToken));
        Assert.Empty(index.Search("!!", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_line_of_little_words_is_found_too()
    {
        var index = Index(Song(1, "Be", "Whisper words of wisdom", "Let it be, let it be"), Song(2, "Other", "It is what it is"));

        var found = Assert.Single(index.Search("let it be", 10, TestContext.Current.CancellationToken));

        Assert.Equal("Let it be, let it be", found.Line);
    }

    [Fact]
    public void Words_in_scripts_without_spaces_are_found_inside_the_line()
    {
        var index = Index(Song(1, "Yoru", "東京の夜に", "君を待つ"));

        var found = Assert.Single(index.Search("東京", 10, TestContext.Current.CancellationToken));

        Assert.Equal("東京の夜に", found.Line);
        Assert.Equal([new LineMark(0, 2)], found.Marks);
    }

    [Fact]
    public void The_same_song_on_an_album_and_a_single_shows_once()
    {
        var index = Index(
            Song(1, "Runaway", "We run away tonight", "Into the light"),
            Song(2, "Runaway", "We run away tonight", "Into the light"));

        Assert.Single(index.Search("run away tonight", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Lyrics_without_timing_are_found_without_a_time()
    {
        var index = new LineIndex(path: null, _time);
        index.Add(Track(1, "Plain"), new SongLyrics([new LyricLine(null, "Words without a clock"), new LyricLine(null, "")], synced: false));

        var found = Assert.Single(index.Search("without a clock", 10, TestContext.Current.CancellationToken));

        Assert.Null(found.At);
        Assert.Null(found.After);
        Assert.Equal("spotify:track:1", found.Track.Uri);
        Assert.Equal("1", found.Track.Id);
        Assert.True(found.Track.IsPlayable);
    }

    [Fact]
    public void Empty_lines_are_left_out_so_the_lines_around_one_are_sung_ones()
    {
        var index = new LineIndex(path: null, _time);
        index.Add(Track(1, "Gaps"), Synced("First words", "", "♪", "Second words", "", "Third words"));

        var found = Assert.Single(index.Search("second words", 10, TestContext.Current.CancellationToken));

        Assert.Equal("First words", found.Before);
        Assert.Equal("Third words", found.After);
        Assert.Equal(TimeSpan.FromSeconds(24), found.At);
    }

    [Fact]
    public void The_index_is_kept_on_disk_and_read_back()
    {
        var index = new LineIndex(IndexFile, _time);
        index.Add(Track(1, "Kept"), Synced("Words kept on disk", "Read back later"));
        index.Add(Track(2, "None"), null);
        index.Add(Track(3, "Instrumental"), SongLyrics.Instrumental);

        var again = new LineIndex(IndexFile, _time);

        Assert.True(again.Knows("spotify:track:1"));
        Assert.True(again.Knows("spotify:track:2"));
        Assert.True(again.Knows("spotify:track:3"));
        Assert.False(again.Knows("spotify:track:4"));
        Assert.Equal(1, again.Count);
        var found = Assert.Single(again.Search("read back", 10, TestContext.Current.CancellationToken));
        Assert.Equal("Kept", found.Track.Title);
        Assert.Equal(TimeSpan.FromSeconds(12), found.At);
    }

    [Fact]
    public void A_song_without_words_is_looked_up_again_after_a_month_but_words_are_kept_for_good()
    {
        var index = new LineIndex(IndexFile, _time);
        index.Add(Track(1, "Words"), Synced("Some words"));
        index.Add(Track(2, "None"), null);

        _time.Advance(LineIndex.RecheckAfter + TimeSpan.FromHours(1));

        Assert.True(index.Knows("spotify:track:1"));
        Assert.False(index.Knows("spotify:track:2"));
    }

    [Fact]
    public void Half_a_line_left_by_a_crash_is_skipped()
    {
        var index = new LineIndex(IndexFile, _time);
        index.Add(Track(1, "Whole"), Synced("A whole line of words"));
        File.AppendAllText(IndexFile, "{\"uri\":\"spotify:track:2\",\"answer\":1,\"li");

        var again = new LineIndex(IndexFile, _time);

        Assert.True(again.Knows("spotify:track:1"));
        Assert.False(again.Knows("spotify:track:2"));
        Assert.Single(again.Search("whole line", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_file_full_of_replaced_answers_is_written_afresh()
    {
        var index = new LineIndex(IndexFile, _time);
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 80; i++)
            {
                index.Add(Track(i, "Song " + i), null);
            }
        }

        index.Add(Track(1000, "Words"), Synced("Still here after the rewrite"));
        Assert.Equal(241, File.ReadAllLines(IndexFile).Length);

        var again = new LineIndex(IndexFile, _time);
        again.Load();

        Assert.Equal(81, File.ReadAllLines(IndexFile).Length);
        Assert.True(again.Knows("spotify:track:79"));
        Assert.Single(new LineIndex(IndexFile, _time).Search("after the rewrite", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Clearing_forgets_every_song_and_deletes_the_file()
    {
        var index = new LineIndex(IndexFile, _time);
        index.Add(Track(1, "Gone"), Synced("Soon forgotten"));

        index.Clear();

        Assert.False(File.Exists(IndexFile));
        Assert.False(index.Knows("spotify:track:1"));
        Assert.Empty(index.Search("soon forgotten", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Only_songs_Spotify_can_start_by_address_are_covered_each_once()
    {
        var songs = new[]
        {
            Track(1, "One"),
            Track(1, "One again"),
            Track(2, "Two"),
            new TrackInfo("spotify:local:Me:Demo:Song:120", "Local", "Me", "Demo", null, TimeSpan.FromMinutes(2), null, null, false, false) { IsLocal = true },
            new TrackInfo("spotify:episode:1", "Episode", "Show", "Show", null, TimeSpan.FromMinutes(30), null, null, false, true),
            new TrackInfo("spotify:track:3", " ", "Band", "Album", null, TimeSpan.FromMinutes(3), null, null, false, true),
        };

        Assert.Equal(["One", "Two"], LineIndexBuilder.Covered(songs).Select(s => s.Title));
    }

    [Fact]
    public void Lookups_pause_longer_while_they_keep_failing()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), LineIndexBuilder.WaitAfter(1));
        Assert.Equal(TimeSpan.FromMinutes(1), LineIndexBuilder.WaitAfter(2));
        Assert.Equal(TimeSpan.FromMinutes(15), LineIndexBuilder.WaitAfter(9));
    }

    [Fact]
    public async Task The_builder_looks_up_each_song_it_does_not_know_once_and_counts_them()
    {
        var index = new LineIndex(IndexFile, _time);
        index.Add(Track(1, "Known"), Synced("Already known"));
        var source = new FakeSource();
        var cache = new LyricsCache(Path.Combine(_folder, "lyrics"), _time);
        cache.Store(LyricsQuery.For("Kept", "Band", "Album", TimeSpan.FromMinutes(3))!, Synced("Kept by the lyrics pane"));
        var builder = new LineIndexBuilder(index, source, cache, _time) { Pace = TimeSpan.Zero };
        var reports = new List<LineIndexProgress>();
        builder.ProgressChanged += (_, progress) =>
        {
            lock (reports)
            {
                reports.Add(progress);
            }
        };
        using var stop = new CancellationTokenSource();

        var run = builder.RunAsync(_ => Task.FromResult<IReadOnlyList<TrackInfo>>([Track(1, "Known"), Track(2, "Kept"), Track(3, "New"), Track(4, "Nothing")]), stop.Token);
        await WaitUntil(() => builder.Progress == new LineIndexProgress(4, 4));

        Assert.Equal(["New", "Nothing"], source.Asked);
        Assert.Equal(new LineIndexProgress(1, 4), reports[0]);
        Assert.False(builder.Progress.IsBuilding);
        Assert.Equal("Kept", Assert.Single(index.Search("kept by the lyrics pane", 10, TestContext.Current.CancellationToken)).Track.Title);
        Assert.Equal("New", Assert.Single(index.Search("brand new words", 10, TestContext.Current.CancellationToken)).Track.Title);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task A_failed_lookup_is_tried_again_on_a_later_pass()
    {
        var index = new LineIndex(path: null, _time);
        var source = new FakeSource { FailOnce = "Flaky" };
        var builder = new LineIndexBuilder(index, source, cache: null, _time) { Pace = TimeSpan.Zero };
        using var stop = new CancellationTokenSource();

        var run = builder.RunAsync(_ => Task.FromResult<IReadOnlyList<TrackInfo>>([Track(1, "Flaky"), Track(2, "New")]), stop.Token);
        await AdvanceUntil(() => builder.Progress == new LineIndexProgress(1, 2), LineIndexBuilder.WaitAfter(1));
        Assert.True(builder.Progress.IsBuilding);
        Assert.Equal(["Flaky", "New"], source.Asked);

        await AdvanceUntil(() => builder.Progress == new LineIndexProgress(2, 2), LineIndexBuilder.RescanAtMost);

        Assert.Equal(["Flaky", "New", "Flaky"], source.Asked);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task A_change_to_the_library_brings_in_its_new_songs()
    {
        var index = new LineIndex(path: null, _time);
        var source = new FakeSource();
        var builder = new LineIndexBuilder(index, source, cache: null, _time) { Pace = TimeSpan.Zero };
        List<TrackInfo> library = [Track(1, "New")];
        using var stop = new CancellationTokenSource();

        var run = builder.RunAsync(_ => Task.FromResult<IReadOnlyList<TrackInfo>>([.. library]), stop.Token);
        await WaitUntil(() => builder.Progress == new LineIndexProgress(1, 1));
        library.Add(Track(2, "Liked later"));
        builder.Refresh();
        await AdvanceUntil(() => builder.Progress == new LineIndexProgress(2, 2), TimeSpan.FromMinutes(1));

        Assert.Equal(["New", "Liked later"], source.Asked);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    /// <summary>Moves the clock on a step at a time until <paramref name="condition"/> holds (a pause may not have started yet).</summary>
    private async Task AdvanceUntil(Func<bool> condition, TimeSpan step)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            _time.Advance(step);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private LineIndex Index(params (TrackInfo Track, SongLyrics Lyrics)[] songs)
    {
        var index = new LineIndex(path: null, _time);
        foreach (var (track, lyrics) in songs)
        {
            index.Add(track, lyrics);
        }

        return index;
    }

    private static (TrackInfo, SongLyrics) Song(int number, string title, params string[] lines) => (Track(number, title), Synced(lines));

    /// <summary>Lines sung six seconds apart, from 0:06.</summary>
    private static SongLyrics Synced(params string[] lines) =>
        new([.. lines.Select((text, i) => new LyricLine(TimeSpan.FromSeconds(6 * (i + 1)), text))], synced: true);

    private static TrackInfo Track(int number, string title) =>
        new($"spotify:track:{number}", title, "Band", "Album", "spotify:album:a", TimeSpan.FromMinutes(3), "small", "large", IsExplicit: false, IsPlayable: true);

    /// <summary>Answers from made-up words: "Nothing" has none, the rest have "Brand new words".</summary>
    private sealed class FakeSource : ILyricsSource
    {
        private readonly Lock _gate = new();

        public List<string> Asked { get; } = [];

        /// <summary>A title whose first lookup fails as if offline.</summary>
        public string? FailOnce { get; set; }

        public Task<SongLyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Asked.Add(query.Title);
                if (query.Title == FailOnce)
                {
                    FailOnce = null;
                    return Task.FromException<SongLyrics?>(new HttpRequestException("Offline."));
                }
            }

            return Task.FromResult(query.Title == "Nothing" ? null : Synced("Brand new words"));
        }
    }
}

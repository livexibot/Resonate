using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public class LocalPlayQueueTests
{
    private static readonly TrackInfo[] Songs = [Song("a"), Song("b"), Song("c"), Song("d")];

    // Fisher–Yates with every pick 0 moves each song one place forward: [a, b, c, d] becomes [b, c, d, a].
    private static readonly Func<int, int> RotateLeft = _ => 0;

    [Fact]
    public void Plays_the_list_in_order_from_the_song_picked()
    {
        var queue = new LocalPlayQueue();

        Assert.Equal("b", queue.Load(Songs, 1, shuffle: false)?.Title);
        Assert.Equal(["c", "d"], Titles(queue.Upcoming));
        Assert.Equal("c", queue.MoveNext(skipped: true)?.Title);
        Assert.Equal("d", queue.MoveNext(skipped: false)?.Title);
        Assert.Null(queue.MoveNext(skipped: false));
    }

    [Fact]
    public void Shuffle_play_starts_with_the_song_picked_then_the_rest_in_random_order()
    {
        var queue = new LocalPlayQueue(RotateLeft);

        Assert.Equal("c", queue.Load(Songs, 2, shuffle: true)?.Title);

        // The rest ([a, b, d]) shuffled.
        Assert.Equal(["b", "d", "a"], Titles(queue.Upcoming));
    }

    [Fact]
    public void Turning_shuffle_on_keeps_the_song_playing_and_off_restores_the_order()
    {
        var queue = new LocalPlayQueue(RotateLeft);
        queue.Load(Songs, 1, shuffle: false);

        queue.SetShuffle(true);
        Assert.Equal("b", queue.Current?.Title);
        Assert.Equal(["c", "d", "a"], Titles(queue.Upcoming));

        queue.MoveNext(skipped: true);
        queue.SetShuffle(false);
        Assert.Equal("c", queue.Current?.Title);
        Assert.Equal(["d"], Titles(queue.Upcoming));
    }

    [Fact]
    public void Added_songs_play_next_in_the_order_added_and_survive_a_new_list()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs, 0, shuffle: false);
        queue.AddToQueue(Song("x"));
        queue.AddToQueue(Song("y"));

        Assert.Equal(["x", "y", "b", "c", "d"], Titles(queue.Upcoming));

        queue.Load(Songs, 2, shuffle: false);
        Assert.Equal(["x", "y", "d"], Titles(queue.Upcoming));
        Assert.Equal("x", queue.MoveNext(skipped: true)?.Title);
        Assert.Equal("y", queue.MoveNext(skipped: true)?.Title);
        Assert.Equal("d", queue.MoveNext(skipped: true)?.Title);
    }

    [Fact]
    public void Repeat_all_wraps_to_the_start()
    {
        var queue = new LocalPlayQueue { Repeat = RepeatMode.All };
        queue.Load(Songs, 3, shuffle: false);

        Assert.Equal(["a", "b", "c"], Titles(queue.Upcoming));
        Assert.Equal("a", queue.PeekNext()?.Title);
        Assert.Equal("a", queue.MoveNext(skipped: false)?.Title);
    }

    [Fact]
    public void Repeat_one_replays_a_song_that_ends_but_a_skip_moves_on()
    {
        var queue = new LocalPlayQueue { Repeat = RepeatMode.One };
        queue.Load(Songs, 1, shuffle: false);

        Assert.Equal("b", queue.PeekNext()?.Title);
        Assert.Equal("b", queue.MoveNext(skipped: false)?.Title);
        Assert.Equal("c", queue.MoveNext(skipped: true)?.Title);
    }

    [Fact]
    public void Previous_goes_back_and_stays_on_the_first_song()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs, 1, shuffle: false);

        Assert.Equal("a", queue.MovePrevious()?.Title);
        Assert.Equal("a", queue.MovePrevious()?.Title);

        queue.Repeat = RepeatMode.All;
        Assert.Equal("d", queue.MovePrevious()?.Title);
    }

    [Fact]
    public void Previous_from_a_queued_song_returns_to_the_list_song_before_it()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs, 1, shuffle: false);
        queue.AddToQueue(Song("x"));

        Assert.Equal("x", queue.MoveNext(skipped: true)?.Title);
        Assert.Equal("b", queue.MovePrevious()?.Title);
        Assert.Equal("c", queue.MoveNext(skipped: true)?.Title);
    }

    [Fact]
    public void After_the_end_it_resets_to_the_first_song()
    {
        var queue = new LocalPlayQueue();
        queue.Load(Songs, 3, shuffle: false);

        Assert.Null(queue.MoveNext(skipped: false));
        Assert.Equal("a", queue.ResetToStart()?.Title);
        Assert.Equal(["b", "c", "d"], Titles(queue.Upcoming));
    }

    [Fact]
    public void An_empty_list_has_nothing_to_play()
    {
        var queue = new LocalPlayQueue();

        Assert.Null(queue.Load([], 0, shuffle: false));
        Assert.Null(queue.MoveNext(skipped: true));
        Assert.Null(queue.PeekNext());
        Assert.Empty(queue.Upcoming);
    }

    internal static TrackInfo Song(string name, string album = "Album", TimeSpan? duration = null) =>
        new LocalFile
        {
            Path = $"/music/{album}/{name}.mp3",
            Title = name,
            Artist = "Artist",
            Album = album,
            DurationMs = (long)(duration ?? TimeSpan.FromMinutes(3)).TotalMilliseconds,
            AddedAt = DateTimeOffset.UnixEpoch,
        }.ToTrackInfo();

    private static string[] Titles(IEnumerable<TrackInfo> tracks) => [.. tracks.Select(t => t.Title)];
}

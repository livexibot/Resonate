using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class QueuePreviewTests
{
    [Fact]
    public void Stops_at_the_first_song_Spotify_repeats_to_fill_its_queue()
    {
        var queue = new PlayerQueue
        {
            CurrentlyPlaying = Item(1),
            Queue = [Item(2), Item(3), null, Item(4), Item(2), Item(3), Item(4)],
        };

        Assert.Equal(["Song 2", "Song 3", "Song 4"], QueuePreview.Upcoming(queue).Select(t => t.Title));
    }

    [Fact]
    public void A_song_started_on_its_own_has_nothing_after_it()
    {
        // Spotify pads "next up" with copies of the song that plays.
        var queue = new PlayerQueue { CurrentlyPlaying = Item(1), Queue = [Item(1), Item(1), Item(1)] };

        Assert.Empty(QueuePreview.Upcoming(queue));
    }

    private static PlayableItem Item(int i) =>
        new() { Name = $"Song {i}", Uri = $"spotify:track:{i}", DurationMs = 200_000 };
}

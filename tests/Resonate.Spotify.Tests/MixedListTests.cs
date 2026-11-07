using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>The order of a list that mixes Spotify's songs with files, kept by Resonate.</summary>
public sealed class MixedListTests
{
    [Fact]
    public void A_queued_song_follows_the_current_one_after_the_list_starts_again()
    {
        var mix = new MixedList(List(), 0, shuffle: false, RepeatMode.Off, "Mine", nextInt: null);
        mix.StartStretchAt(0);
        while (mix.NextStretch() is not null)
        {
        }

        mix.Restart();
        mix.Add(Spotify(7), playNext: false);

        Assert.Equal(["File 0", "Song 7", "Song 1", "Song 2", "File 3", "Song 4"], Titles(mix.Order));
    }

    [Fact]
    public void Shuffle_after_repeat_all_starts_the_list_again_plays_every_song_once()
    {
        var mix = new MixedList(List(), 0, shuffle: false, RepeatMode.All, "Mine", max => max - 1);
        mix.StartStretchAt(0);
        for (var i = 0; i < 4; i++)
        {
            mix.NextStretch();
        }

        Assert.Equal(0, mix.Index);
        mix.SetShuffle(true);

        Assert.Equal(Titles(List()).Order(), Titles(mix.Order).Order());
    }

    [Fact]
    public void A_queued_song_follows_the_current_one_after_going_back_a_stretch()
    {
        var mix = new MixedList(List(), 4, shuffle: false, RepeatMode.Off, "Mine", nextInt: null);
        mix.StartStretchAt(4);
        mix.PreviousStretch();
        Assert.Equal("File 3", mix.Current.Title);

        mix.Add(Spotify(7), playNext: false);

        Assert.Equal(["File 0", "Song 1", "Song 2", "File 3", "Song 7", "Song 4"], Titles(mix.Order));
    }

    [Fact]
    public void Every_song_is_its_own_entry_so_a_stretch_starts_on_the_right_one()
    {
        var song = Spotify(1);
        var mix = new MixedList([song, Spotify(2), song, File(3)], 2, shuffle: false, RepeatMode.Off, "Mine", nextInt: null);

        var stretch = mix.StartStretchAt(2);
        Assert.Equal(2, stretch.StartIndex);
        Assert.NotSame(stretch.Tracks[0], stretch.StartTrack);

        mix.Add(mix.Order[0], playNext: true);
        Assert.Equal(mix.Order.Count, mix.Order.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    private static List<TrackInfo> List() => [File(0), Spotify(1), Spotify(2), File(3), Spotify(4)];

    private static TrackInfo Spotify(int i) =>
        new($"spotify:track:{i}", $"Song {i}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true);

    private static TrackInfo File(int i) =>
        new($"spotify:local:Me:Demos:File+{i}:120", $"File {i}", "Me", "Demos", null, TimeSpan.FromSeconds(120), null, null, IsExplicit: false, IsPlayable: false)
        {
            IsLocal = true,
            FilePath = $"/music/File{i}.mp3",
        };

    private static List<string> Titles(IEnumerable<TrackInfo> tracks) => [.. tracks.Select(t => t.Title)];
}

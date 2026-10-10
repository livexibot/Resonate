using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests;

public sealed class DjMixTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);

    private static TrackInfo Song(int n, string artist, double daysAgo) =>
        new($"spotify:track:{n}", $"Song {n}", artist, "Album", null, TimeSpan.FromMinutes(3), null, null, false, true)
        {
            Id = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AddedAt = Now.AddDays(-daysAgo),
            ArtistRefs = [new ArtistRef(artist, null)],
        };

    private static PlayRecord Play(TrackInfo song, double daysAgo, int hour = 20) => new()
    {
        Uri = song.Uri!,
        Title = song.Title,
        Artists = [.. song.ArtistRefs],
        Album = song.Album,
        DurationMs = 180_000,
        PlayedAt = new DateTimeOffset(Now.AddDays(-daysAgo).Date, TimeSpan.Zero).AddHours(hour),
    };

    private static (List<TrackInfo> Liked, List<PlayRecord> Plays) Library()
    {
        var liked = new List<TrackInfo>();
        for (var i = 0; i < 12; i++)
        {
            liked.Add(Song(i, "Old Band", 500 + i));
        }

        for (var i = 12; i < 20; i++)
        {
            liked.Add(Song(i, "New Band", 5 + i));
        }

        for (var i = 20; i < 40; i++)
        {
            liked.Add(Song(i, $"Artist {i}", 100 + i));
        }

        for (var i = 40; i < 46; i++)
        {
            liked.Add(Song(i, $"Newcomer {i}", 10));
        }

        var plays = new List<PlayRecord>();
        foreach (var song in liked.Skip(12).Take(6))
        {
            plays.Add(Play(song, 1));
            plays.Add(Play(song, 2));
            plays.Add(Play(song, 3));
        }

        return (liked, plays);
    }

    [Fact]
    public void A_run_starts_with_favourites_and_never_plays_a_song_twice()
    {
        var (liked, plays) = Library();
        var sets = DjMix.Build(liked, plays, Now, new Random(1), sets: 6);

        Assert.Equal(DjSetKind.Favourites, sets[0].Kind);
        Assert.StartsWith("Hey, it's your DJ.", sets[0].Intro, StringComparison.Ordinal);
        Assert.All(sets[0].Tracks, t => Assert.Contains(plays, p => p.Uri == t.Uri));
        var all = DjMix.Flatten(sets);
        Assert.Equal(all.Count, all.Select(t => t.Uri).Distinct().Count());
        Assert.All(sets, s => Assert.InRange(s.Tracks.Count, DjMix.SmallestSet, DjMix.DefaultSetSize));
        Assert.Equal(sets.Count, sets.Count(s => s.Title.Length > 0 && s.Intro.Length > 0));
    }

    [Fact]
    public void Each_kind_picks_the_songs_it_is_about()
    {
        var (liked, plays) = Library();
        var sets = DjMix.Build(liked, plays, Now, new Random(2), sets: 12);

        var throwbacks = sets.First(s => s.Kind == DjSetKind.Throwbacks);
        Assert.All(throwbacks.Tracks, t => Assert.True(Now - t.AddedAt!.Value >= TimeSpan.FromDays(365)));

        var fresh = sets.First(s => s.Kind == DjSetKind.FreshFinds);
        Assert.All(fresh.Tracks, t => Assert.True(Now - t.AddedAt!.Value <= TimeSpan.FromDays(45)));

        var spotlight = sets.First(s => s.Kind == DjSetKind.Spotlight);
        Assert.StartsWith("More from ", spotlight.Title, StringComparison.Ordinal);
        Assert.Single(spotlight.Tracks.Select(t => t.Artists).Distinct());
    }

    [Fact]
    public void Switching_up_starts_with_the_kind_asked_for_and_skips_what_played()
    {
        var (liked, plays) = Library();
        var played = new HashSet<string>(liked.Take(5).Select(t => t.Uri!), StringComparer.Ordinal);
        var sets = DjMix.Build(liked, plays, Now, new Random(3), sets: 4, first: DjSetKind.Throwbacks, skip: played);

        Assert.Equal(DjSetKind.Throwbacks, sets[0].Kind);
        Assert.DoesNotContain(DjMix.Flatten(sets), t => played.Contains(t.Uri!));
    }

    [Fact]
    public void A_small_library_still_makes_a_run_and_an_empty_one_makes_none()
    {
        var few = new List<TrackInfo> { Song(1, "A", 10), Song(2, "B", 20), Song(3, "C", 30), Song(4, "D", 40) };
        var sets = DjMix.Build(few, [], Now, new Random(4), sets: 5);
        Assert.NotEmpty(sets);
        Assert.Empty(DjMix.Build([], [], Now, new Random(4)));
    }

    [Fact]
    public void A_song_in_the_run_belongs_to_its_set()
    {
        var (liked, plays) = Library();
        var sets = DjMix.Build(liked, plays, Now, new Random(5), sets: 3);
        Assert.Equal(0, DjMix.SetAt(sets, 0));
        Assert.Equal(1, DjMix.SetAt(sets, sets[0].Tracks.Count));
        Assert.Equal(-1, DjMix.SetAt(sets, 999));
        Assert.Equal("evening", DjMix.Period(20));
        Assert.Equal("late-night", DjMix.Period(2));
    }
}

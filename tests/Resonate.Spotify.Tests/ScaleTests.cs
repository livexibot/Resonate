using System.Diagnostics;
using System.Globalization;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Tests;

/// <summary>
/// Song lists the size of a heavy listener's library. The time limits are far
/// above what the work takes (a few milliseconds); they are there to catch work
/// that grows with the square of the list, not to time anything exactly.
/// </summary>
public sealed class ScaleTests : IDisposable
{
    private const int Count = 10_000;

    private static readonly string[] Words = ["Álpha", "alpha", "Beta", "bêta", "ｇamma", "gamma", "Delta", "élan", "Zed", "zed", "Ørsted", "a"];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-scale-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static List<TrackInfo> Library()
    {
        var random = new Random(7);
        string Pick() => $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]}";
        return Enumerable.Range(0, Count).Select(i =>
        {
            var artist = Pick();
            return new TrackInfo($"spotify:track:{i}", Pick(), artist, Pick(), null, TimeSpan.FromSeconds(random.Next(60, 400)), null, null, false, true)
            {
                Position = i,
                ArtistRefs = [new ArtistRef(artist, artist)],
                AddedAt = DateTimeOffset.UnixEpoch.AddDays(random.Next(5000)),
                DiscNumber = random.Next(3) == 0 ? null : random.Next(1, 3),
                TrackNumber = random.Next(1, 15),
            };
        }).ToList();
    }

    /// <summary>The order as the sorter worked it out before: every comparison made, then the first difference taken.</summary>
    private static List<TrackInfo> ReferenceSort(List<TrackInfo> tracks, TrackSortField field)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        int Text(string a, string b) => compare.Compare(a, b, options);
        static int Nullable(int? a, int? b) => (a, b) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => a.Value.CompareTo(b.Value),
        };

        var sorted = tracks.ToList();
        sorted.Sort((a, b) =>
        {
            var result = field switch
            {
                TrackSortField.Title => Text(a.Title, b.Title),
                TrackSortField.Artist => new[] { Text(a.PrimaryArtist, b.PrimaryArtist), Text(a.Album, b.Album), Nullable(a.DiscNumber, b.DiscNumber), Nullable(a.TrackNumber, b.TrackNumber) }.FirstOrDefault(r => r != 0),
                _ => new[] { Text(a.Album, b.Album), Nullable(a.DiscNumber, b.DiscNumber), Nullable(a.TrackNumber, b.TrackNumber) }.FirstOrDefault(r => r != 0),
            };
            return result != 0 ? result : a.Position!.Value.CompareTo(b.Position!.Value);
        });
        return sorted;
    }

    [Theory]
    [InlineData(TrackSortField.Title)]
    [InlineData(TrackSortField.Artist)]
    [InlineData(TrackSortField.Album)]
    public void Sorting_by_text_keeps_the_same_order_as_before(TrackSortField field)
    {
        var tracks = Library();

        var sorted = TrackSorter.Apply(tracks, new TrackSort(field, Descending: false));

        Assert.Equal(ReferenceSort(tracks, field).Select(t => t.Uri), sorted.Select(t => t.Uri));
    }

    [Fact]
    public void Ten_thousand_songs_sort_and_filter_quickly()
    {
        var tracks = Library();
        TrackSorter.Apply(tracks.Take(100), new TrackSort(TrackSortField.Artist, false));

        var clock = Stopwatch.StartNew();
        foreach (var field in Enum.GetValues<TrackSortField>())
        {
            TrackSorter.Apply(tracks, new TrackSort(field, Descending: true));
        }

        var matches = tracks.Count(t => TrackSorter.Matches(t, "alpha zed"));

        Assert.InRange(matches, 1, Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Sorting and filtering took {clock.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void A_list_read_once_is_kept_in_memory()
    {
        var store = new TrackListStore(_folder);
        store.Save(new CachedTrackList { Key = "liked", Tracks = Library().Take(10).ToList() });
        var path = Directory.GetFiles(_folder).Single();
        var first = store.Load("liked");

        File.Delete(path);

        Assert.Same(first, store.Load("liked"));
    }

    [Fact]
    public void Only_the_lists_used_last_stay_in_memory()
    {
        var store = new TrackListStore(_folder);
        foreach (var key in new[] { "liked", "playlist-a", "playlist-b" })
        {
            store.Save(new CachedTrackList { Key = key, Tracks = Library().Take(3).ToList() });
        }

        foreach (var file in Directory.GetFiles(_folder))
        {
            File.Delete(file);
        }

        Assert.Null(store.Load("liked"));
        Assert.NotNull(store.Load("playlist-b"));
    }

    [Fact]
    public void Clearing_the_store_forgets_the_lists_in_memory()
    {
        var store = new TrackListStore(_folder);
        store.Save(new CachedTrackList { Key = "liked", Tracks = Library().Take(3).ToList() });

        store.Clear();

        Assert.Null(store.Load("liked"));
    }
}

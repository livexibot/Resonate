using Resonate.Spotify.Library;

namespace Resonate.Spotify.Tests;

/// <summary>Which of Spotify's pictures a cover drawn at a given size uses.</summary>
public sealed class CoverPickTests
{
    private const string Small = "https://i.scdn.co/image/small";
    private const string Large = "https://i.scdn.co/image/large";
    private const string Full = "https://i.scdn.co/image/full";

    [Theory]
    [InlineData(40, Small)]
    [InlineData(64, Small)]
    [InlineData(65, Large)]
    [InlineData(100, Large)]
    [InlineData(300, Large)]
    [InlineData(301, Full)]
    [InlineData(700, Full)]
    public void The_smallest_picture_that_is_not_stretched_is_used(int pixels, string expected) =>
        Assert.Equal(expected, ImagePicker.ForSize(pixels, Small, Large, Full));

    [Fact]
    public void A_missing_picture_falls_back_to_the_nearest_one()
    {
        Assert.Equal(Large, ImagePicker.ForSize(40, null, Large, Full));
        Assert.Equal(Full, ImagePicker.ForSize(100, Small, null, Full));
        Assert.Equal(Small, ImagePicker.ForSize(100, Small, null, null));
        Assert.Equal(Large, ImagePicker.ForSize(500, Small, Large, null));
        Assert.Null(ImagePicker.ForSize(100, null, null, null));
    }

    [Fact]
    public void A_song_picks_from_its_own_pictures()
    {
        var song = new TrackInfo("spotify:track:1", "Song", "Artist", "Album", null, TimeSpan.FromMinutes(3), Small, Large, false, true)
        {
            FullImageUrl = Full,
        };

        // A row's 40 px cover at 125 % display scaling, then at 200 % Cover size.
        Assert.Equal(Small, song.ImageFor(50));
        Assert.Equal(Large, song.ImageFor(100));
        Assert.Equal(Full, song.ImageFor(350));
    }
}

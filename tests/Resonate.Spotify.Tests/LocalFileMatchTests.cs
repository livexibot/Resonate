using Resonate.Spotify.LocalFiles;

namespace Resonate.Spotify.Tests;

public sealed class LocalFileMatchTests
{
    private static LocalFile File(string path, string title, string? artist, string? album, int seconds, string? albumArtist = null) => new()
    {
        Path = path,
        Title = title,
        Artist = artist,
        AlbumArtist = albumArtist,
        Album = album,
        DurationMs = seconds * 1000L,
    };

    [Fact]
    public void Finds_the_same_song_by_title_and_artist_ignoring_case_and_spaces()
    {
        var match = new LocalFileMatch([
            File(@"C:\Music\a.mp3", "Night  Drive", "The Band", "Roads", 200),
            File(@"C:\Music\b.mp3", "Other", "The Band", "Roads", 180),
        ]);

        Assert.Equal(@"C:\Music\a.mp3", match.Find("night drive", "the band", "Roads", TimeSpan.FromSeconds(201))?.Path);
    }

    [Fact]
    public void Takes_the_file_that_also_matches_album_and_length_when_titles_repeat()
    {
        var match = new LocalFileMatch([
            File(@"C:\Music\live.mp3", "Song", "Artist", "Live", 320),
            File(@"C:\Music\studio.mp3", "Song", "Artist", "Studio", 210),
        ]);

        Assert.Equal(@"C:\Music\studio.mp3", match.Find("Song", "Artist", "Studio", TimeSpan.FromSeconds(211))?.Path);
        Assert.Equal(@"C:\Music\live.mp3", match.Find("Song", "Artist", "Live", TimeSpan.FromSeconds(318))?.Path);
    }

    [Fact]
    public void Matches_one_artist_of_several_or_the_album_artist()
    {
        var match = new LocalFileMatch([
            File(@"C:\Music\a.flac", "Duet", "Singer A, Singer B", "Pairs", 240),
            File(@"C:\Music\b.flac", "Solo", null, "Alone", 150, albumArtist: "Singer C"),
        ]);

        Assert.NotNull(match.Find("Duet", "Singer B", "Pairs", TimeSpan.FromSeconds(240)));
        Assert.NotNull(match.Find("Solo", "Singer C", "Alone", TimeSpan.FromSeconds(150)));
    }

    [Fact]
    public void Takes_album_and_length_when_the_artist_differs()
    {
        var match = new LocalFileMatch([File(@"C:\Music\a.mp3", "Track 1", "Unknown", "Demo Tape", 95)]);

        Assert.NotNull(match.Find("Track 1", "Someone", "Demo Tape", TimeSpan.FromSeconds(96)));
        Assert.Null(match.Find("Track 1", "Someone", "Demo Tape", TimeSpan.FromSeconds(120)));
        Assert.Null(match.Find("Track 1", "Someone", "Other Album", TimeSpan.FromSeconds(95)));
    }

    [Fact]
    public void With_no_artist_anywhere_the_length_decides()
    {
        var match = new LocalFileMatch([File(@"C:\Music\voice memo.m4a", "voice memo", null, null, 61)]);

        Assert.NotNull(match.Find("voice memo", string.Empty, string.Empty, TimeSpan.FromSeconds(60)));
        Assert.Null(match.Find("voice memo", string.Empty, string.Empty, TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public void Finds_nothing_for_another_title()
    {
        var match = new LocalFileMatch([File(@"C:\Music\a.mp3", "Song", "Artist", "Album", 200)]);

        Assert.Null(match.Find("Another song", "Artist", "Album", TimeSpan.FromSeconds(200)));
        Assert.Null(match.Find(string.Empty, "Artist", "Album", TimeSpan.FromSeconds(200)));
    }
}

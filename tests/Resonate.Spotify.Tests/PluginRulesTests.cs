using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests;

public sealed class PluginRulesTests
{
    [Theory]
    [InlineData(22, 7, 23, true)]
    [InlineData(22, 7, 3, true)]
    [InlineData(22, 7, 7, false)]
    [InlineData(22, 7, 12, false)]
    [InlineData(22, 7, 22, true)]
    [InlineData(9, 17, 12, true)]
    [InlineData(9, 17, 17, false)]
    [InlineData(9, 17, 8, false)]
    [InlineData(5, 5, 5, false)]
    public void Quiet_hours_may_run_past_midnight(int from, int to, int hour, bool quiet) =>
        Assert.Equal(quiet, QuietHours.IsQuiet(from, to, hour));

    [Theory]
    [InlineData(0.8, 30, 0.3)]
    [InlineData(0.2, 30, 0.2)]
    [InlineData(1.0, 150, 1.0)]
    public void Quiet_hours_cap_the_volume_and_never_raise_it(double volume, int percent, double expected) =>
        Assert.Equal(expected, QuietHours.Cap(volume, percent), 6);

    [Fact]
    public void Now_playing_text_fills_in_the_song_on_one_line()
    {
        Assert.Equal("Band - Song", NowPlayingText.Format("{artist} - {title}", "Song", "Band", "Record"));
        Assert.Equal("Song (Record)", NowPlayingText.Format("{TITLE} ({album})", "Song", "Band", "Record"));
        Assert.Equal("Band - Song", NowPlayingText.Format(null, "Song", "Band", null));
        Assert.Equal("A B - Song", NowPlayingText.Format("{artist} - {title}", "Song", "A\nB", null));
        Assert.Equal(string.Empty, NowPlayingText.Format("{artist} - {title}", null, "Band", null));
    }

    [Fact]
    public void History_is_a_spreadsheet_newest_first_with_safe_cells()
    {
        var plays = new[]
        {
            new PlayRecord { Title = "Old, \"quoted\"", Artists = [new ArtistRef("Band", null)], Album = "Record", DurationMs = 61_500, PlayedAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Uri = "spotify:track:1" },
            new PlayRecord { Title = "=SUM(A1)", Artists = [new ArtistRef("A", null), new ArtistRef("B", null)], Album = "X", DurationMs = 2_000, PlayedAt = DateTimeOffset.Parse("2026-10-02T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Uri = "spotify:track:2" },
        };

        var lines = HistoryCsv.Write(plays).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Played at,Title,Artists,Album,Length (seconds),Spotify link", lines[0]);
        Assert.Contains("'=SUM(A1)", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"A, B\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"Old, \"\"quoted\"\"\"", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",61,spotify:track:1", lines[2], StringComparison.Ordinal);
        Assert.Equal(3, lines.Length);
    }
}

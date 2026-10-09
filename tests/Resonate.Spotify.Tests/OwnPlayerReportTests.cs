using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

public sealed class OwnPlayerReportTests
{
    private const string Playing = """
        {"type":"state","now":{"paused":false,"position":61500,"shuffle":true,"repeat":1,
        "context":"spotify:playlist:p","disallows":["skipping_prev"],
        "track":{"uri":"spotify:track:t","id":"t","name":"Song","duration":200000,
        "artists":[{"name":"Band","uri":"spotify:artist:a"},{"name":"Guest","uri":"spotify:artist:g"}],
        "album":{"name":"Record","uri":"spotify:album:r","images":[{"url":"https://i/640","width":640,"height":640},{"url":"https://i/64","width":64,"height":64}]}}}}
        """;

    [Fact]
    public void What_the_own_player_says_reads_as_Spotifys_answer_for_its_device()
    {
        var state = OwnPlayerReport.Parse(Playing, "dev-1", "Resonate");

        Assert.NotNull(state);
        Assert.Equal("dev-1", state.Device?.Id);
        Assert.Equal("Resonate", state.Device?.Name);
        Assert.True(state.Device?.IsActive);
        Assert.Null(state.Device?.VolumePercent);
        Assert.True(state.IsPlaying);
        Assert.Equal(61500, state.ProgressMs);
        Assert.True(state.ShuffleState);
        Assert.Equal(RepeatMode.All, state.Repeat);
        Assert.Equal("spotify:playlist:p", state.Context?.Uri);
        Assert.True(state.Actions?.Disallowed("skipping_prev"));
        Assert.False(state.Actions?.Disallowed("toggling_shuffle"));

        var track = TrackInfo.From(state.Item);
        Assert.NotNull(track);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Band, Guest", track.Artists);
        Assert.Equal("Record", track.Album);
        Assert.Equal("spotify:track:t", track.Uri);
        Assert.Equal(TimeSpan.FromSeconds(200), track.Duration);
    }

    [Theory]
    [InlineData("""{"type":"state"}""")]
    [InlineData("""{"type":"state","now":null}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void A_report_with_nothing_playing_here_reads_as_nothing(string json) =>
        Assert.Null(OwnPlayerReport.Parse(json, "dev-1", "Resonate"));

    [Fact]
    public void A_paused_song_with_track_repeat_reads_as_such()
    {
        var state = OwnPlayerReport.Parse("""{"type":"state","now":{"paused":true,"position":0,"repeat":2,"track":{"uri":"spotify:track:t","name":"Song","duration":1000}}}""", "dev-1", "Resonate");

        Assert.NotNull(state);
        Assert.False(state.IsPlaying);
        Assert.Equal(RepeatMode.One, state.Repeat);
        Assert.Null(state.Context);
    }
}

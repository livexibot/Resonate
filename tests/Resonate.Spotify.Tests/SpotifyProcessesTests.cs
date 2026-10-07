using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests;

public class SpotifyProcessesTests
{
    [Theory]
    [InlineData("\"C:\\Users\\me\\AppData\\Roaming\\Spotify\\Spotify.exe\" --autostart --minimized", SpotifyProcessKind.Main)]
    [InlineData("\"C:\\Spotify\\Spotify.exe\" --type=renderer --log-severity=disable --lang=en-US --field-trial-handle=1234", SpotifyProcessKind.Interface)]
    [InlineData("\"C:\\Spotify\\Spotify.exe\" --type=gpu-process --gpu-preferences=UAAAAAAAAADgAAA", SpotifyProcessKind.Graphics)]
    [InlineData("\"C:\\Spotify\\Spotify.exe\" --type=utility --utility-sub-type=audio.mojom.AudioService --lang=en-US", SpotifyProcessKind.Audio)]
    [InlineData("\"C:\\Spotify\\Spotify.exe\" --type=utility --utility-sub-type=network.mojom.NetworkService", SpotifyProcessKind.Other)]
    [InlineData("\"C:\\Spotify\\Spotify.exe\" --type=crashpad-handler --database=x", SpotifyProcessKind.Other)]
    [InlineData("", SpotifyProcessKind.Other)]
    [InlineData(null, SpotifyProcessKind.Other)]
    public void Each_process_is_recognised_by_its_command_line(string? commandLine, SpotifyProcessKind expected) =>
        Assert.Equal(expected, SpotifyProcesses.Classify(commandLine));

    [Fact]
    public void A_look_alike_argument_is_not_the_process_type() =>
        Assert.Equal(SpotifyProcessKind.Main, SpotifyProcesses.Classify("Spotify.exe --typeface=renderer"));

    [Theory]
    [InlineData(SpotifyProcessKind.Interface, true)]
    [InlineData(SpotifyProcessKind.Graphics, true)]
    [InlineData(SpotifyProcessKind.Main, false)]
    [InlineData(SpotifyProcessKind.Audio, false)]
    [InlineData(SpotifyProcessKind.Other, false)]
    public void Only_the_processes_that_draw_the_window_are_slowed(SpotifyProcessKind kind, bool slowed) =>
        Assert.Equal(slowed, SpotifyProcesses.MaySaveResources(kind));
}

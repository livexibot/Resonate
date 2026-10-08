using System.Net;
using Microsoft.UI.Xaml;
using Resonate.App.Demo;
using Resonate.App.Services;
using Resonate.Spotify.Lyrics;

namespace Resonate.App;

/// <summary>
/// Lyrics, a built-in plugin: the player bar's lyrics button opens the
/// words of the song in the pane on the right, which the queue and Settings
/// share (opening one closes the others; the lyrics keep the queue's width).
/// While the plugin is off the button is hidden and nothing is looked up.
/// Words come from LRCLIB (lrclib.net), only while the pane is open, and
/// are kept on disk for 30 days; the demo makes its own.
/// </summary>
public sealed partial class MainWindow
{
    // Made the first time the pane opens.
    private LyricsLibrary? _lyricsLibrary;

    /// <summary>Opens the lyrics pane, or closes it (the player bar's lyrics button).</summary>
    public void ToggleLyrics() => ShowLyrics(!LyricsPane.IsOpen);

    partial void SetUpLyrics()
    {
        PlayerBar.AttachLyrics();
        PlayerBar.LyricsRequested += (_, _) => ToggleLyrics();
        LyricsPane.CloseRequested += (_, _) => ShowLyrics(false);
        ShownChanged += (_, _) => LyricsPane.SetWindowShown(IsShown);
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.Lyrics)
            {
                ApplyLyricsPlugin();
            }
        };
        ApplyLyricsPlugin();
    }

    private bool LyricsOn => _services.BuiltIns.IsOn(BuiltInPlugins.Lyrics);

    private void ApplyLyricsPlugin()
    {
        if (!LyricsOn)
        {
            ShowLyrics(false);
            LyricsPane.Clear();
        }

        PlayerBar.ShowLyricsButton(LyricsOn, LyricsPane.IsOpen);
    }

    private void ShowLyrics(bool open)
    {
        if (open == LyricsPane.IsOpen || (open && !LyricsOn))
        {
            return;
        }

        // One pane at a time on the right, so the page keeps its room.
        if (open)
        {
            ShowQueue(false);
            ShowSettings(false);
        }

        LyricsPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            LyricsPane.Open(
                _services.Player,
                _lyricsLibrary ??= CreateLyricsLibrary(),
                _services.IsDemo ? "Made-up words for the demo" : "Lyrics from LRCLIB",
                IsShown);
        }
        else
        {
            LyricsPane.Close();
        }

        PlaceRightPane();
        PlayerBar.ShowLyricsButton(LyricsOn, LyricsPane.IsOpen);
    }

    private LyricsLibrary CreateLyricsLibrary()
    {
        // The demo (and CI) never reaches the network.
        if (_services.IsDemo)
        {
            return new LyricsLibrary(new DemoLyrics(), cache: null);
        }

        // Its own client, made only once lyrics are wanted, with short timeouts: the pane says so and offers to try again.
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        return new LyricsLibrary(
            new LrcLibClient(http, LrcLibClient.UserAgentFor(AppInfo.Version)),
            new LyricsCache(Path.Combine(AppPaths.CacheFolder, "lyrics")));
    }
}

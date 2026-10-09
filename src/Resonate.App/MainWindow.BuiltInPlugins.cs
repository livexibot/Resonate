namespace Resonate.App;

/// <summary>
/// The built-in plugins that live in the window (see
/// <see cref="Services.BuiltInPlugins"/>). Each one sets itself up in its
/// own <c>MainWindow.&lt;Name&gt;.cs</c>, watching
/// <c>_services.BuiltIns.Changed</c> so it starts and stops at once; while
/// it is off it shows nothing and runs nothing.
/// </summary>
public sealed partial class MainWindow
{
    private void SetUpBuiltInPlugins()
    {
        SetUpLyrics();
        SetUpHomeStage();
        SetUpAwayScreen();
        SetUpRediscover();
        SetUpUpNext();
        SetUpArtistOrbit();
        SetUpSmartPlaylists();
        SetUpWindowShapes();
        SetUpSummonBar();
        SetUpSignalPath();
        SetUpPauseOnLock();
        SetUpPauseOnUnplug();
    }

    partial void SetUpLyrics();

    partial void SetUpHomeStage();

    partial void SetUpAwayScreen();

    partial void SetUpRediscover();

    partial void SetUpUpNext();

    partial void SetUpArtistOrbit();

    partial void SetUpSmartPlaylists();

    partial void SetUpWindowShapes();

    partial void SetUpSummonBar();

    partial void SetUpSignalPath();

    partial void SetUpPauseOnLock();

    partial void SetUpPauseOnUnplug();
}

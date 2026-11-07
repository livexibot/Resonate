using Resonate.App.Services;

namespace Resonate.App;

/// <summary>
/// Line Finder in the window (a built-in plugin, off until turned on in
/// Settings, Plugins): while it is on, its index fills in the background
/// (see <see cref="LineFinderService"/>) and Search offers Lyrics.
/// </summary>
public sealed partial class MainWindow
{
    partial void SetUpLineFinder()
    {
        FollowPlugin(BuiltInPlugins.LineFinder, () => ApplyLineFinder(atLaunch: false));
        ApplyLineFinder(atLaunch: true);
    }

    private void ApplyLineFinder(bool atLaunch)
    {
        if (_services.BuiltIns.IsOn(BuiltInPlugins.LineFinder))
        {
            _services.LineFinder.Start(atLaunch);
        }
        else
        {
            // Never made while the plugin was off: nothing to stop.
            _services.StopLineFinder();
        }
    }
}

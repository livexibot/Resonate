using Resonate.App.Services;

namespace Resonate.App;

/// <summary>
/// Up next, a built-in plugin: the queue pane can edit what plays next
/// (<see cref="Controls.QueuePanel"/>), and songs' menus offer "Play next"
/// (<see cref="Pages.Lists.UpNextActions"/>). Off, the queue only shows.
/// </summary>
public sealed partial class MainWindow
{
    partial void SetUpUpNext()
    {
        QueuePane.SetUpNext(_services.BuiltIns.IsOn(BuiltInPlugins.UpNext));
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.UpNext)
            {
                QueuePane.SetUpNext(_services.BuiltIns.IsOn(id));
            }
        };
    }
}

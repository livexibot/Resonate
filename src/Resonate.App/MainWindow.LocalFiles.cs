using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Resonate.App.ViewModels;

namespace Resonate.App;

/// <summary>Local Files in the window: its place in the sidebar, the media keys, and the first look through the folders.</summary>
public sealed partial class MainWindow
{
    private NavItem? _localFilesNav;

    /// <summary>Called once, from the constructor.</summary>
    private void SetUpLocalFiles()
    {
        _localFilesNav = NavItems.FirstOrDefault(n => n.Key == LocalFilesKey);
        ShowLocalFilesInSidebar();
        _services.LocalFiles.SidebarChanged += (_, _) => ShowLocalFilesInSidebar();
        CompositionTarget.Rendering += StartLocalFilesAfterFirstFrame;
    }

    /// <summary>Under Liked Songs, unless the user switched it off in Settings.</summary>
    private void ShowLocalFilesInSidebar()
    {
        if (_localFilesNav is not { } item)
        {
            return;
        }

        var shown = NavItems.Contains(item);
        _syncingSelection = true;
        try
        {
            if (_services.LocalFiles.ShowInSidebar && !shown)
            {
                var liked = NavItems.ToList().FindIndex(n => n.Key == LikedSongsKey);
                NavItems.Insert(liked + 1, item);
                NavList.SelectedItem = NavItems.FirstOrDefault(n => n.Key == _currentPage);
            }
            else if (!_services.LocalFiles.ShowInSidebar && shown)
            {
                NavItems.Remove(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Nothing about local files runs before the first frame; then the index loads and the folders are scanned in the background.</summary>
    private void StartLocalFilesAfterFirstFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= StartLocalFilesAfterFirstFrame;
        if (StartupOptions.Current is { StartupBenchmarkFile: not null } or { UpdateCheckFeed: not null })
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _services.LocalFiles.Controls?.AttachWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            _services.LocalFiles.Start();
        });
    }
}

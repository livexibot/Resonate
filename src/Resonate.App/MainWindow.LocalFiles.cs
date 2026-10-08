using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Resonate.App;

/// <summary>Local Files in the window: its place in the sidebar, the media keys, and the first look through the folders.</summary>
public sealed partial class MainWindow
{
    /// <summary>Called once, from the constructor.</summary>
    private void SetUpLocalFiles()
    {
        ShowSidebarLinks();
        _services.LocalFiles.SidebarChanged += (_, _) => ShowSidebarLinks();
        CompositionTarget.Rendering += StartLocalFilesAfterFirstFrame;
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

using Microsoft.UI.Dispatching;
using Resonate.Windows;

namespace Resonate.App;

/// <summary>
/// Lighter while out of sight: a few seconds after the window is minimised
/// or hidden (the mini player, the tray), .NET tidies its memory and the
/// pages Resonate is not using go back to Windows, so Task Manager shows
/// far less. They come back as soon as they are needed.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan TrimAfter = TimeSpan.FromSeconds(4);

    private DispatcherQueueTimer? _trimTimer;

    private void SetUpMemoryTrim()
    {
        if (_services.IsDemo)
        {
            return;
        }

        _trimTimer = DispatcherQueue.CreateTimer();
        _trimTimer.Interval = TrimAfter;
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += (_, _) =>
        {
            if (!IsShown)
            {
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
                OwnMemory.Trim();
            }
        };
        ShownChanged += (_, _) =>
        {
            _trimTimer.Stop();
            if (!IsShown)
            {
                _trimTimer.Start();
            }
        };
    }
}

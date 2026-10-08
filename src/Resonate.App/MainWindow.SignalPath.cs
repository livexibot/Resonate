using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Services;

namespace Resonate.App;

/// <summary>
/// Signal path, a built-in plugin: the pill in the player bar that says
/// whether what plays reaches the output losslessly. While it is off,
/// nothing of it runs: no file watch, no Core Audio notifications.
/// </summary>
public sealed partial class MainWindow
{
    private SignalPathMonitor? _signalPath;
    private bool _signalPathReady;

    partial void SetUpSignalPath()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.SignalPath)
            {
                ApplySignalPath();
            }
        };
        Closed += (_, _) => StopSignalPath();

        // Nothing of it before the first frame, so it never costs start-up time.
        CompositionTarget.Rendering += StartSignalPathAfterFirstFrame;
    }

    private void StartSignalPathAfterFirstFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= StartSignalPathAfterFirstFrame;
        if (StartupOptions.Current is { StartupBenchmarkFile: not null } or { UpdateCheckFeed: not null })
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _signalPathReady = true;
            ApplySignalPath();
        });
    }

    private void ApplySignalPath()
    {
        var on = _signalPathReady && _services.BuiltIns.IsOn(BuiltInPlugins.SignalPath);
        if (on && _signalPath is null)
        {
            _signalPath = new SignalPathMonitor(_services);
            PlayerBar.AttachSignalPath(_signalPath);
        }
        else if (!on)
        {
            StopSignalPath();
        }
    }

    private void StopSignalPath()
    {
        if (_signalPath is { } monitor)
        {
            PlayerBar.AttachSignalPath(null);
            monitor.Dispose();
            _signalPath = null;
        }
    }
}

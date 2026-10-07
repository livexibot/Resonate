using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Resonate.App;

public static class Program
{
    /// <summary>When the process started, for measuring the time to the first frame.</summary>
    public static long StartTimestamp { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        StartTimestamp = Stopwatch.GetTimestamp();

        // Velopack's install, update and uninstall hooks run here and exit;
        // on a normal start this returns at once.
        VelopackApp.Build().Run();

        StartupOptions.Current = StartupOptions.Parse(args);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            // Mirrors the Main that the XAML compiler would generate.
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}

/// <summary>Command-line switches, mostly for automated checks in CI.</summary>
public sealed record StartupOptions
{
    public static StartupOptions Current { get; set; } = new();

    /// <summary>Show sample data instead of a Spotify account (for screenshots and trying the look).</summary>
    public bool Demo { get; init; }

    /// <summary>Write the time to the first frame to this file, then quit.</summary>
    public string? StartupBenchmarkFile { get; init; }

    /// <summary>Save screenshots of the main pages to this folder, then quit.</summary>
    public string? ScreenshotFolder { get; init; }

    /// <summary>Run the speed and memory test with a large demo library, write its results to this folder, then quit.</summary>
    public string? PerformanceFolder { get; init; }

    /// <summary>
    /// Look for an update in this Velopack feed folder instead of GitHub,
    /// download it, write the outcome to <see cref="UpdateCheckResultFile"/>,
    /// then quit. CI uses it to prove an installed copy can update.
    /// </summary>
    public string? UpdateCheckFeed { get; init; }

    public string? UpdateCheckResultFile { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var options = new StartupOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--demo":
                    options = options with { Demo = true };
                    break;
                case "--startup-benchmark" when next is not null:
                    options = options with { StartupBenchmarkFile = next };
                    i++;
                    break;
                case "--screenshots" when next is not null:
                    options = options with { ScreenshotFolder = next, Demo = true };
                    i++;
                    break;
                case "--perf" when next is not null:
                    options = options with { PerformanceFolder = next, Demo = true };
                    i++;
                    break;
                case "--update-check" when next is not null && i + 2 < args.Count:
                    options = options with { UpdateCheckFeed = next, UpdateCheckResultFile = args[i + 2], Demo = true };
                    i += 2;
                    break;
            }
        }

        return options;
    }
}

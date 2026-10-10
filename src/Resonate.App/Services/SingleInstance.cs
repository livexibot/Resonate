using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;

namespace Resonate.App.Services;

/// <summary>
/// One Resonate at a time (the owner's request, 10 October 2026): a second
/// start asks the open one to come to the front and quits. Counted per
/// settings file, so a local build run with "--data" can still sit beside
/// the installed copy, and CI's checks, which quit by themselves, are left
/// out (<see cref="AppliesTo"/>). Plain named kernel objects: a mutex held
/// while Resonate runs, an event a second start sets, and one the open
/// copy sets back once it has heard.
/// </summary>
internal static partial class SingleInstance
{
    /// <summary>How long a second start waits for the open copy to answer.</summary>
    private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(3);

    /// <summary>How long it then waits for a copy that did not answer (one that is closing, as an update restarts Resonate) to end.</summary>
    private static readonly TimeSpan EndWait = TimeSpan.FromSeconds(10);

    // Held for as long as this process runs; Windows lets go of it when the process ends.
    private static Mutex? _running;
    private static EventWaitHandle? _asked;
    private static EventWaitHandle? _answered;
    private static Target? _window;
    private static int _pending;

    /// <summary>Runs that check something and quit by themselves never count.</summary>
    public static bool AppliesTo(StartupOptions options) =>
        options.StartupBenchmarkFile is null
        && options.ScreenshotFolder is null
        && options.PerformanceFolder is null
        && options.UpdateCheckFeed is null
        && options.PluginCheckFeed is null
        && options.WebPlayerCheckResultFile is null;

    /// <summary>
    /// True when this is the one Resonate for its settings and it should
    /// start; false once the open copy has been asked to come to the front
    /// (or did not answer and did not end), and this one should quit.
    /// </summary>
    public static bool Claim(string settingsFile)
    {
        var key = Key(settingsFile);
        _running = new Mutex(initiallyOwned: false, $@"Local\{key}");
        if (TryTake(TimeSpan.Zero))
        {
            Listen(key);
            return true;
        }

        // Another copy runs: let it take the foreground, which only a program the user just started may hand on, and ask it.
        AllowSetForegroundWindow(AsfwAny);
        using (var asked = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-show"))
        using (var answered = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-shown"))
        {
            asked.Set();
            if (answered.WaitOne(AnswerWait))
            {
                return false;
            }
        }

        // No answer: it is closing (an update restarting Resonate) or stuck. Start once it has ended.
        if (TryTake(EndWait))
        {
            Listen(key);
            return true;
        }

        return false;
    }

    /// <summary>
    /// What a second start does to this copy: <paramref name="bringToFront"/>
    /// runs on <paramref name="queue"/>'s thread, also when one asked before
    /// the window was there.
    /// </summary>
    public static void OnAsked(DispatcherQueue queue, Action bringToFront)
    {
        Volatile.Write(ref _window, new Target(queue, bringToFront));
        Deliver();
    }

    /// <summary>Brings the window forward if a second start asked and the window is there; whichever of the two comes last does it.</summary>
    private static void Deliver()
    {
        if (Volatile.Read(ref _window) is { } window && Interlocked.Exchange(ref _pending, 0) == 1)
        {
            window.Queue.TryEnqueue(() => window.Bring());
        }
    }

    private static bool TryTake(TimeSpan wait)
    {
        try
        {
            return _running!.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The last copy ended without letting go (a crash): it is ours now.
            return true;
        }
    }

    /// <summary>Hears second starts on a thread of its own and answers each at once; the window is brought forward on its own thread.</summary>
    private static void Listen(string key)
    {
        _asked = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-show");
        _answered = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-shown");
        var thread = new Thread(() =>
        {
            while (true)
            {
                _asked.WaitOne();
                _answered.Set();

                // Still starting, the window comes forward once it is there.
                Interlocked.Exchange(ref _pending, 1);
                Deliver();
            }
        })
        {
            IsBackground = true,
            Name = "Resonate single instance",
        };
        thread.Start();
    }

    /// <summary>A name for the settings file that fits a kernel object's name.</summary>
    private static string Key(string settingsFile)
    {
        var path = Path.GetFullPath(settingsFile).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "Resonate-" + Convert.ToHexString(hash, 0, 12);
    }

    /// <summary>The window's thread and what brings it forward.</summary>
    private sealed record Target(DispatcherQueue Queue, Action Bring);

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}

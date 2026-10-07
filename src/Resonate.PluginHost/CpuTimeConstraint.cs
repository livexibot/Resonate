using System.Diagnostics;
using System.Runtime.InteropServices;
using Jint;

namespace Resonate.PluginHost;

/// <summary>
/// Stops a call into a plugin once it has kept the processor busy for longer
/// than the limit. Time spent waiting for the processor does not count: the
/// helper runs at below-normal priority, so on a busy computer a plugin can
/// wait seconds without doing anything wrong. A much longer limit on the
/// clock still applies, in case the processor time can not be read.
/// </summary>
internal sealed partial class CpuTimeConstraint : Constraint
{
    // Reading the clock for every statement would slow plugins down; a loop runs this many between looks.
    private const int StatementsBetweenChecks = 1000;

    private readonly TimeSpan _limit;
    private readonly TimeSpan _ceiling;
    private long _started;
    private TimeSpan? _startedCpu;
    private int _countdown;

    public CpuTimeConstraint(TimeSpan limit, TimeSpan ceiling)
    {
        _limit = limit;
        _ceiling = ceiling;
    }

    public override void Reset()
    {
        _started = Stopwatch.GetTimestamp();
        _startedCpu = ThreadCpuTime();
        _countdown = StatementsBetweenChecks;
    }

    public override void Check()
    {
        if (--_countdown > 0)
        {
            return;
        }

        _countdown = StatementsBetweenChecks;
        var elapsed = Stopwatch.GetElapsedTime(_started);
        if (elapsed < _limit)
        {
            // It can not have used more processor time than this.
            return;
        }

        var used = _startedCpu is { } start && ThreadCpuTime() is { } now ? now - start : elapsed;
        if (used >= _limit || elapsed >= _ceiling)
        {
            throw new TimeoutException();
        }
    }

    /// <summary>The processor time this thread has used, or null where it can not be read.</summary>
    internal static TimeSpan? ThreadCpuTime()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user)
                    ? TimeSpan.FromTicks(kernel + user)
                    : null;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // CLOCK_THREAD_CPUTIME_ID
                var clock = OperatingSystem.IsLinux() ? 3 : 16;
                return clock_gettime(clock, out var time) == 0
                    ? TimeSpan.FromTicks((time.Seconds * TimeSpan.TicksPerSecond) + (time.Nanoseconds / 100))
                    : null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Fall back to the clock.
        }

        return null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    // The four times are FILETIMEs: 100-nanosecond units, like TimeSpan ticks.
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("libc", EntryPoint = "clock_gettime")]
    private static partial int clock_gettime(int clock, out TimeSpec time);

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }
}

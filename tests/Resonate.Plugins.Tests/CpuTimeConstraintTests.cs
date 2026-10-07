using System.Diagnostics;
using Resonate.PluginHost;

namespace Resonate.Plugins.Tests;

/// <summary>
/// The time limit on a plugin's calls counts processor time, so a plugin that
/// only waits for a busy computer is not stopped.
/// </summary>
public sealed class CpuTimeConstraintTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void Reads_this_threads_processor_time()
    {
        Assert.NotNull(CpuTimeConstraint.ThreadCpuTime());
    }

    [Fact]
    public void Waiting_does_not_count()
    {
        var constraint = new CpuTimeConstraint(Limit, TimeSpan.FromSeconds(30));
        constraint.Reset();
        Thread.Sleep(Limit * 2);

        for (var i = 0; i < 5000; i++)
        {
            constraint.Check();
        }
    }

    [Fact]
    public void Busy_work_is_stopped()
    {
        var constraint = new CpuTimeConstraint(Limit, TimeSpan.FromSeconds(30));
        constraint.Reset();
        var started = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() =>
        {
            while (started.Elapsed < TimeSpan.FromSeconds(10))
            {
                constraint.Check();
            }
        });
    }

    [Fact]
    public void The_clock_limit_still_applies()
    {
        var constraint = new CpuTimeConstraint(Limit, ceiling: Limit);
        constraint.Reset();
        Thread.Sleep(Limit * 2);

        Assert.Throws<TimeoutException>(() =>
        {
            for (var i = 0; i < 5000; i++)
            {
                constraint.Check();
            }
        });
    }
}

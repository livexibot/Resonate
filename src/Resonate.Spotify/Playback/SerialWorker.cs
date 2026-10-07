using System.Threading.Channels;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Runs commands one after another on a background thread, in the order they
/// were given, so the interface thread never waits for Spotify.
/// </summary>
internal sealed class SerialWorker : IDisposable
{
    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _stopping = new();

    public SerialWorker() => _ = Task.Run(RunAsync);

    /// <summary>Queues <paramref name="work"/>. The returned task completes when it has run; it never faults.</summary>
    public Task Enqueue(Func<CancellationToken, Task> work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new WorkItem(work, done)))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();

        // Cancel without disposing: a command that is still running may hold the token.
        _stopping.Cancel();
    }

    private async Task RunAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await item.Work(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Commands report their own errors; a stray one must not stop the queue.
            }
            finally
            {
                item.Done.TrySetResult();
            }
        }
    }

    private sealed record WorkItem(Func<CancellationToken, Task> Work, TaskCompletionSource Done);
}

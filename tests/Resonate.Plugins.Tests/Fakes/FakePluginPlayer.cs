using Resonate.Plugins.Protocol;

namespace Resonate.Plugins.Tests.Fakes;

/// <summary>A player that records what plugins asked it to do.</summary>
internal sealed class FakePluginPlayer : IPluginPlayer
{
    private readonly List<string> _calls = [];

    public event EventHandler? StateChanged;

    public NowPlaying? Current { get; private set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public void Show(NowPlaying? state)
    {
        Current = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task PlayAsync() => Record("play");

    public Task PauseAsync() => Record("pause");

    public Task NextAsync() => Record("next");

    public Task PreviousAsync() => Record("previous");

    public Task SeekAsync(TimeSpan position) => Record("seek " + position.TotalSeconds);

    public Task SetVolumeAsync(double volume) => Record("volume " + volume);

    private Task Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }

        return Task.CompletedTask;
    }
}

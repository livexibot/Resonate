using Resonate.App.Services;
using Resonate.Windows;

namespace Resonate.App;

/// <summary>
/// Pause on unplug, a built-in plugin: when the default sound output changes
/// while music plays (headphones or a Bluetooth speaker disconnecting, so
/// Windows moves the sound elsewhere), the music pauses instead of carrying
/// on out loud. Core Audio's notifications (<see cref="DefaultAudioOutput.Watch"/>)
/// say something changed; the output's name, read off the interface thread,
/// says whether it is another output. While off, nothing is watched.
/// </summary>
public sealed partial class MainWindow
{
    private IDisposable? _outputWatch;
    private string? _outputName;
    private string? _unpluggedFrom;
    private int _outputCheckQueued;

    partial void SetUpPauseOnUnplug()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.PauseOnUnplug)
            {
                TurnPauseOnUnplug(_services.BuiltIns.IsOn(id));
            }
        };
        Closed += (_, _) => TurnPauseOnUnplug(false);
        TurnPauseOnUnplug(_services.BuiltIns.IsOn(BuiltInPlugins.PauseOnUnplug));
    }

    private void TurnPauseOnUnplug(bool on)
    {
        if (on == (_outputWatch is not null))
        {
            return;
        }

        if (!on)
        {
            _outputWatch?.Dispose();
            _outputWatch = null;
            _outputName = null;
            return;
        }

        _outputWatch = DefaultAudioOutput.Watch(OnOutputChanged);
        if (_outputWatch is not null)
        {
            _ = CheckOutputAsync(pauseWhenChanged: false);
        }
    }

    /// <summary>On a Windows audio thread, often several times for one change: checked once.</summary>
    private void OnOutputChanged()
    {
        if (Interlocked.Exchange(ref _outputCheckQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                // Windows sends a burst of notifications as an output goes; let it settle.
                await Task.Delay(300);
                Interlocked.Exchange(ref _outputCheckQueued, 0);
                await CheckOutputAsync(pauseWhenChanged: true);
            });
        }
    }

    private async Task CheckOutputAsync(bool pauseWhenChanged)
    {
        var output = await Task.Run(DefaultAudioOutput.TryRead);
        if (_outputWatch is null || output is null)
        {
            return;
        }

        var previous = _outputName;
        var changed = previous is not null && output.Name != previous;
        _outputName = output.Name;
        if (!pauseWhenChanged || !changed)
        {
            return;
        }

        if (_services.Player.State.IsPlaying)
        {
            _unpluggedFrom = previous;
            await _services.Player.PauseAsync();
        }
        else if (output.Name == _unpluggedFrom && _services.Settings.PauseOnUnplugResume)
        {
            // The same headphones or speaker came back.
            _unpluggedFrom = null;
            await _services.Player.PlayAsync();
        }
    }
}

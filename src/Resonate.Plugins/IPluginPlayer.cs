using Resonate.Plugins.Protocol;

namespace Resonate.Plugins;

/// <summary>The player as plugins reach it. The app adapts its own player to this.</summary>
public interface IPluginPlayer
{
    /// <summary>Raised on any thread after <see cref="Current"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>What is playing, or null when nothing is.</summary>
    NowPlaying? Current { get; }

    Task PlayAsync();

    Task PauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    Task SeekAsync(TimeSpan position);

    /// <summary>From 0 to 1.</summary>
    Task SetVolumeAsync(double volume);
}

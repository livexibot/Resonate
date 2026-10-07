using Resonate.Plugins;
using Resonate.Plugins.Protocol;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>The player as plugins see it: what plays, and the same controls as the player bar.</summary>
internal sealed class PluginPlayer : IPluginPlayer, IDisposable
{
    private readonly PlayerController _player;

    public PluginPlayer(PlayerController player)
    {
        _player = player;
        player.StateChanged += OnStateChanged;
    }

    public event EventHandler? StateChanged;

    public NowPlaying? Current => From(_player.State, DateTimeOffset.UtcNow);

    public static NowPlaying? From(PlayerState state, DateTimeOffset now) => state.HasTrack
        ? new NowPlaying
        {
            Title = state.Title,
            Artists = state.Artists,
            Album = state.Album,
            Uri = state.TrackUri,
            ContextUri = state.ContextUri,
            IsPlaying = state.IsPlaying,
            Position = state.PositionAt(now).TotalSeconds,
            Duration = state.Duration.TotalSeconds,
            Volume = state.Volume,
            CanSeek = state.CanSeek,
        }
        : null;

    public Task PlayAsync() => _player.PlayAsync();

    public Task PauseAsync() => _player.PauseAsync();

    public Task NextAsync() => _player.NextAsync();

    public Task PreviousAsync() => _player.PreviousAsync();

    public Task SeekAsync(TimeSpan position) => _player.SeekAsync(position);

    public Task SetVolumeAsync(double volume) => _player.SetVolumeAsync(volume);

    public void Dispose() => _player.StateChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);
}

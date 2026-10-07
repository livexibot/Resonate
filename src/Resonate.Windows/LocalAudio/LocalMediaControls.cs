using System.Runtime.InteropServices;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Windows.Media;
using Windows.Storage.Streams;

namespace Resonate.Windows.LocalAudio;

/// <summary>
/// Windows' media controls (media keys, the volume flyout, the lock screen)
/// for the local files player, attached to Resonate's main window. They are
/// switched on only while local files play, so the rest of the time the
/// media keys reach the Spotify app as before.
/// </summary>
public sealed partial class LocalMediaControls : ILocalSystemControls
{
    private readonly Lock _gate = new();
    private SystemMediaTransportControls? _controls;

    // The window's thread: the controls are changed there, as for any window.
    private SynchronizationContext? _windowThread;
    private string? _shownTrack;
    private byte[]? _shownArtwork;
    private bool _disposed;

    public event EventHandler<LocalControlButton>? ButtonPressed;

    public event EventHandler<TimeSpan>? SeekRequested;

    public event EventHandler<bool>? ShuffleRequested;

    public event EventHandler<RepeatMode>? RepeatRequested;

    /// <summary>Connects to the main window's media controls (call once, on the interface thread, after the window exists).</summary>
    public void AttachWindow(nint window)
    {
        lock (_gate)
        {
            if (_controls is not null || _disposed)
            {
                return;
            }
        }

        SystemMediaTransportControls controls;
        try
        {
            controls = SystemMediaTransportControlsInterop.GetForWindow(window);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // No media keys for local files; the buttons in Resonate still work.
            return;
        }

        controls.IsEnabled = false;
        controls.IsPlayEnabled = true;
        controls.IsPauseEnabled = true;
        controls.IsStopEnabled = true;
        controls.IsNextEnabled = true;
        controls.IsPreviousEnabled = true;
        controls.ButtonPressed += OnButtonPressed;
        controls.PlaybackPositionChangeRequested += OnPositionRequested;
        controls.ShuffleEnabledChangeRequested += OnShuffleRequested;
        controls.AutoRepeatModeChangeRequested += OnRepeatRequested;
        lock (_gate)
        {
            _controls = controls;
            _windowThread = SynchronizationContext.Current;
        }
    }

    public Task ShowAsync(PlayerState? state)
    {
        SystemMediaTransportControls? controls;
        SynchronizationContext? windowThread;
        lock (_gate)
        {
            controls = _disposed ? null : _controls;
            windowThread = _windowThread;
        }

        if (controls is null)
        {
            return Task.CompletedTask;
        }

        if (windowThread is null || windowThread == SynchronizationContext.Current)
        {
            Show(controls, state);
        }
        else
        {
            // Posted in order, never waited for: the player does not wait on the window.
            windowThread.Post(_ => Show(controls, state), null);
        }

        return Task.CompletedTask;
    }

    private void Show(SystemMediaTransportControls controls, PlayerState? state)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            ShowState(controls, state);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The media controls are a convenience; the music plays on without them.
        }
    }

    private void ShowState(SystemMediaTransportControls controls, PlayerState? state)
    {
        if (state is null || !state.HasTrack)
        {
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            controls.IsEnabled = false;
            return;
        }

        controls.IsEnabled = true;
        controls.PlaybackStatus = state.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        controls.ShuffleEnabled = state.Shuffle;
        controls.AutoRepeatMode = state.Repeat switch
        {
            RepeatMode.One => MediaPlaybackAutoRepeatMode.Track,
            RepeatMode.All => MediaPlaybackAutoRepeatMode.List,
            _ => MediaPlaybackAutoRepeatMode.None,
        };

        if (state.TrackUri != _shownTrack || !ReferenceEquals(state.ArtworkBytes, _shownArtwork))
        {
            _shownTrack = state.TrackUri;
            _shownArtwork = state.ArtworkBytes;
            var display = controls.DisplayUpdater;
            display.ClearAll();
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = state.Title ?? string.Empty;
            display.MusicProperties.Artist = state.Artists ?? string.Empty;
            display.MusicProperties.AlbumTitle = state.Album ?? string.Empty;
            if (state.ArtworkBytes is { Length: > 0 } artwork)
            {
                display.Thumbnail = RandomAccessStreamReference.CreateFromStream(new MemoryStream(artwork, writable: false).AsRandomAccessStream());
            }

            display.Update();
        }

        var duration = state.Duration > TimeSpan.Zero ? state.Duration : TimeSpan.Zero;
        var position = state.PositionAt(DateTimeOffset.UtcNow);
        controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            EndTime = duration,
            MaxSeekTime = duration,
            Position = duration > TimeSpan.Zero && position > duration ? duration : position,
        });
    }

    public void Dispose()
    {
        SystemMediaTransportControls? controls;
        lock (_gate)
        {
            _disposed = true;
            controls = _controls;
            _controls = null;
        }

        if (controls is null)
        {
            return;
        }

        controls.ButtonPressed -= OnButtonPressed;
        controls.PlaybackPositionChangeRequested -= OnPositionRequested;
        controls.ShuffleEnabledChangeRequested -= OnShuffleRequested;
        controls.AutoRepeatModeChangeRequested -= OnRepeatRequested;
        try
        {
            controls.IsEnabled = false;
        }
        catch (COMException)
        {
            // The window is already gone.
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        LocalControlButton? button = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => LocalControlButton.Play,
            SystemMediaTransportControlsButton.Pause => LocalControlButton.Pause,
            SystemMediaTransportControlsButton.Stop => LocalControlButton.Stop,
            SystemMediaTransportControlsButton.Next => LocalControlButton.Next,
            SystemMediaTransportControlsButton.Previous => LocalControlButton.Previous,
            _ => null,
        };
        if (button is { } pressed)
        {
            ButtonPressed?.Invoke(this, pressed);
        }
    }

    private void OnPositionRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args) =>
        SeekRequested?.Invoke(this, args.RequestedPlaybackPosition);

    private void OnShuffleRequested(SystemMediaTransportControls sender, ShuffleEnabledChangeRequestedEventArgs args) =>
        ShuffleRequested?.Invoke(this, args.RequestedShuffleEnabled);

    private void OnRepeatRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args) =>
        RepeatRequested?.Invoke(this, args.RequestedAutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.Track => RepeatMode.One,
            MediaPlaybackAutoRepeatMode.List => RepeatMode.All,
            _ => RepeatMode.Off,
        });
}

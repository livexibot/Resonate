using Resonate.Spotify.Playback;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Resonate.Windows;

/// <summary>
/// Controls the Spotify app through the Windows system media transport
/// controls (the same session the media keys and the volume flyout use).
/// Commands and reports never leave the computer.
/// </summary>
public sealed partial class SmtcMediaChannel : ILocalMediaChannel
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Lock _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private LocalMediaSnapshot _current = LocalMediaSnapshot.None;
    private (string? Title, string? Artist, string? Album) _artworkKey;
    private byte[]? _artwork;
    private bool _artworkRead;
    private volatile bool _listening;
    private bool _disposed;

    public event EventHandler<LocalMediaSnapshot>? Changed;

    public LocalMediaSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>True when the session belongs to Spotify (installer or Microsoft Store version).</summary>
    public static bool IsSpotify(string sourceAppUserModelId) =>
        sourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listening || _disposed)
        {
            return;
        }

        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
        _listening = true;
        _manager.SessionsChanged -= OnSessionsChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        await AttachAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Stop()
    {
        _listening = false;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        Detach();
        Publish(LocalMediaSnapshot.None);
    }

    public Task<bool> PlayAsync(CancellationToken cancellationToken) =>
        SendAsync(s => s.TryPlayAsync().AsTask(cancellationToken));

    public Task<bool> PauseAsync(CancellationToken cancellationToken) =>
        SendAsync(s => s.TryPauseAsync().AsTask(cancellationToken));

    public Task<bool> NextAsync(CancellationToken cancellationToken) =>
        SendAsync(s => s.TrySkipNextAsync().AsTask(cancellationToken));

    public Task<bool> PreviousAsync(CancellationToken cancellationToken) =>
        SendAsync(s => s.TrySkipPreviousAsync().AsTask(cancellationToken));

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken) =>
        SendAsync(s =>
        {
            // The session's timeline may not start at zero.
            var start = s.GetTimelineProperties().StartTime;
            return s.TryChangePlaybackPositionAsync((start + position).Ticks).AsTask(cancellationToken);
        });

    public void Dispose()
    {
        _disposed = true;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        Detach();
        _refreshLock.Dispose();
    }

    private async Task<bool> SendAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        var session = Volatile.Read(ref _session);
        if (session is null)
        {
            return false;
        }

        try
        {
            return await command(session).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The session went away (Spotify closed) between the check and the call.
            return false;
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _ = AttachAsync(CancellationToken.None);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = RefreshAsync(sender);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ = RefreshAsync(sender);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ = RefreshAsync(sender);

    private async Task AttachAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_listening || _manager is null)
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? spotify = null;
        try
        {
            spotify = _manager.GetSessions().FirstOrDefault(s => IsSpotify(s.SourceAppUserModelId));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Sessions are changing; the next SessionsChanged event tries again.
        }

        var current = Volatile.Read(ref _session);
        if (spotify?.SourceAppUserModelId == current?.SourceAppUserModelId && (spotify is null) == (current is null))
        {
            return;
        }

        Detach();
        if (spotify is null || !_listening)
        {
            Publish(LocalMediaSnapshot.None);
            return;
        }

        spotify.MediaPropertiesChanged += OnMediaPropertiesChanged;
        spotify.PlaybackInfoChanged += OnPlaybackInfoChanged;
        spotify.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        Volatile.Write(ref _session, spotify);
        await RefreshAsync(spotify, cancellationToken).ConfigureAwait(false);
    }

    private void Detach()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
        {
            return;
        }

        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private async Task RefreshAsync(GlobalSystemMediaTransportControlsSession session, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (!_listening || !ReferenceEquals(session, Volatile.Read(ref _session)))
            {
                return;
            }

            var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var controls = playback?.Controls;

            var key = (media?.Title, media?.Artist, media?.AlbumTitle);
            if (key != _artworkKey)
            {
                _artworkKey = key;
                _artwork = null;
                _artworkRead = false;
            }

            // A media session may name the new song first and add its cover in
            // a later update, so keep looking until a cover has been read.
            if (!_artworkRead && media?.Thumbnail is { } thumbnail)
            {
                // Publish the new song at once; the cover follows when read.
                PublishFrom(session, Build(media, playback, timeline, controls, artwork: null));
                _artwork = await ReadThumbnailAsync(thumbnail, cancellationToken).ConfigureAwait(false);
                _artworkRead = _artwork is not null;
            }

            PublishFrom(session, Build(media, playback, timeline, controls, _artwork));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            // Spotify is changing songs or closing; the next event brings a fresh report.
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static LocalMediaSnapshot Build(
        GlobalSystemMediaTransportControlsSessionMediaProperties? media,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback,
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline,
        GlobalSystemMediaTransportControlsSessionPlaybackControls? controls,
        byte[]? artwork)
    {
        var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;
        var position = timeline is null ? TimeSpan.Zero : timeline.Position - timeline.StartTime;

        // Without a timeline the sample time is meaningless (it can be year 1601).
        var sampledAt = timeline?.LastUpdatedTime ?? default;
        if (duration <= TimeSpan.Zero || sampledAt.Year < 2000)
        {
            sampledAt = DateTimeOffset.UtcNow;
        }

        if (duration <= TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        return new LocalMediaSnapshot
        {
            HasSession = true,
            Title = NullIfEmpty(media?.Title),
            Artist = NullIfEmpty(media?.Artist),
            Album = NullIfEmpty(media?.AlbumTitle),
            Artwork = artwork,
            IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Position = position < TimeSpan.Zero ? TimeSpan.Zero : position,
            Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
            PositionUpdatedAt = sampledAt,
            CanSeek = controls?.IsPlaybackPositionEnabled == true,
            CanSkipNext = controls?.IsNextEnabled == true,
            CanSkipPrevious = controls?.IsPreviousEnabled == true,
        };
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail, CancellationToken cancellationToken)
    {
        using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (stream.Size is 0 or > 4 * 1024 * 1024)
        {
            return null;
        }

        await using var source = stream.AsStreamForRead();
        using var buffer = new MemoryStream((int)stream.Size);
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Publishes a report read from <paramref name="session"/>, unless listening stopped or the session changed meanwhile.</summary>
    private void PublishFrom(GlobalSystemMediaTransportControlsSession session, LocalMediaSnapshot snapshot)
    {
        if (_listening && ReferenceEquals(session, Volatile.Read(ref _session)))
        {
            Publish(snapshot);
        }
    }

    private void Publish(LocalMediaSnapshot snapshot)
    {
        lock (_gate)
        {
            if (snapshot == _current)
            {
                return;
            }

            _current = snapshot;
        }

        Changed?.Invoke(this, snapshot);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

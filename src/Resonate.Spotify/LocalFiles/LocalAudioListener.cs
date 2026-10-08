using Resonate.Spotify.Playback;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Lends a sink (the visualiser) the local files player's sound, but only
/// while it is wanted and the local files player is the one the player bar
/// shows; otherwise the player's tap is taken down. Spotify's songs never
/// reach the sink: the Spotify app plays them in its own process, and only
/// the Home stage may hear that sound, through
/// <see cref="Audio.SpotifySoundListener"/>.
/// </summary>
public sealed class LocalAudioListener : IDisposable
{
    private readonly PlayerRouter _player;
    private readonly ILocalAudioSink _sink;
    private readonly Lock _gate = new();
    private bool _wanted;
    private bool _listening;
    private bool _disposed;

    public LocalAudioListener(PlayerRouter player, ILocalAudioSink sink)
    {
        _player = player;
        _sink = sink;
        player.StateChanged += OnStateChanged;
    }

    /// <summary>Something shows the sound (a visualiser is on screen). May be set from any thread.</summary>
    public bool Wanted
    {
        get
        {
            lock (_gate)
            {
                return _wanted;
            }
        }

        set
        {
            lock (_gate)
            {
                _wanted = value;
                Update();
            }
        }
    }

    /// <summary>The sink is handed to the local files player: it is wanted, and local files are in the player bar.</summary>
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _listening;
            }
        }
    }

    /// <summary>A local file is the player bar's song and it plays, so the sink hears music; false for Spotify's songs.</summary>
    public bool IsLive => _player.ActiveSource == PlaybackSource.LocalFiles && _player.State.IsPlaying;

    public void Dispose()
    {
        _player.StateChanged -= OnStateChanged;
        lock (_gate)
        {
            _disposed = true;
            Update();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            Update();
        }
    }

    // Under _gate, so the player gets the wishes in the order they were made.
    // StateChanged comes often; only a change reaches the player.
    private void Update()
    {
        var listen = !_disposed && _wanted && _player.ActiveSource == PlaybackSource.LocalFiles;
        if (listen == _listening)
        {
            return;
        }

        _listening = listen;
        _player.Local.SetAudioSink(listen ? _sink : null);
    }
}

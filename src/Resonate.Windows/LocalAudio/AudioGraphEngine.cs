using System.Runtime.InteropServices;
using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Storage;

namespace Resonate.Windows.LocalAudio;

/// <summary>
/// Plays the user's own music files through Windows' AudioGraph (Media
/// Foundation decodes them: MP3, AAC, ALAC, FLAC, WAV and WMA; Ogg and Opus
/// need Microsoft's Web Media Extensions). Never used for Spotify audio.
///
/// The graph: the open file (its outgoing gain is the equalizer's preamp)
/// → a long-lived "EQ bus" with two four-band equalizers and a limiter →
/// the sound device (its outgoing gain is the volume). The graph is made on
/// the first song played, never at start-up. Pause stops the file, so play
/// resumes at once; after a minute paused the whole graph stops, so Windows
/// does not keep an audio stream open. Shortly before a song ends the next
/// one is opened and connected, stopped, and started the moment the first
/// ends, so albums play without a gap.
/// </summary>
public sealed partial class AudioGraphEngine : ILocalAudioEngine
{
    /// <summary>How long before the end of a song the next one is opened.</summary>
    internal static readonly TimeSpan PrepareAhead = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan IdleStop = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>How close to its end a song that stopped moving counts as ended.</summary>
    private static readonly TimeSpan EndSlack = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ticks after which a song that played and then stood still, even after
    /// being started again, counts as ended wherever it is: Windows only
    /// estimates the length of some files (MP3s without a length header), so
    /// the end can come before the length says.
    /// </summary>
    private const int GiveUpTicks = 5;

    // Windows' equalizer effect works from 22 to 48 kHz; a device set higher gets a 48 kHz graph.
    private const uint MaxGraphRate = 48_000;

    private const string NoDeviceMessage = "Resonate could not open the sound device. Check that speakers or headphones are connected, then press play.";
    private const string LostDeviceMessage = "Resonate lost the sound device, so the music stopped. Press play to go on.";

    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    // Opening files and rebuilding the graph go one at a time; the rest is quick and under the lock.
    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly Lock _gate = new();
    private readonly ITimer _idleTimer;
    private readonly ITimer _ticker;

    private AudioGraph? _graph;
    private AudioDeviceOutputNode? _output;
    private AudioSubmixNode? _bus;
    private EqualizerEffectDefinition? _lowBands;
    private EqualizerEffectDefinition? _highBands;
    private LimiterEffectDefinition? _limiter;
    private bool _graphRunning;

    private Track? _current;
    private Track? _next;
    private Track? _gaplessNext;
    private string? _nextPath;
    private string? _failedNextPath;
    private bool _preparing;
    private bool _looping;
    private bool _playing;
    private double _volume = 1;
    private LocalEqualizer _equalizer = LocalEqualizer.From(null);
    private bool _disposed;

    public AudioGraphEngine()
    {
        _idleTimer = TimeProvider.System.CreateTimer(_ => StopWhenIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ticker = TimeProvider.System.CreateTimer(_ => OnTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<LocalTrackEnded>? TrackEnded;

    public event EventHandler<string>? Failed;

    public TimeSpan Position => PositionOf(Volatile.Read(ref _current));

    public TimeSpan Duration => Volatile.Read(ref _current) is { } current ? DurationOf(current) : TimeSpan.Zero;

    public async Task<TimeSpan> OpenAsync(string path, TimeSpan position, bool play, CancellationToken cancellationToken)
    {
        await _ops.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var (graph, bus) = await EnsureGraphAsync().ConfigureAwait(false);

            // "Next" usually asks for the song that is already open and waiting.
            Track? track;
            lock (_gate)
            {
                track = _next is { } next && string.Equals(next.Path, path, PathComparison) ? next : null;
                if (track is not null)
                {
                    _next = null;
                    Volatile.Write(ref _gaplessNext, null);
                }
            }

            if (track is null)
            {
                try
                {
                    track = await CreateTrackAsync(graph, bus, path, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // The file takes the place of what was open even when it can not
                    // play: the song before must not go on where nothing can pause it.
                    lock (_gate)
                    {
                        if (!_disposed)
                        {
                            CloseCurrent();
                        }
                    }

                    throw;
                }
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    track.Dispose();
                    throw new ObjectDisposedException(nameof(AudioGraphEngine));
                }

                _current?.Dispose();
                Volatile.Write(ref _current, track);
                ConfigureCurrent(track);
                if (position > TimeSpan.Zero)
                {
                    Seek(track, position);
                }

                if (!play)
                {
                    StopPlaying();
                }
                else if (!StartPlaying())
                {
                    throw DeviceProblem(null);
                }

                return DurationOf(track);
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task PlayAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        var started = true;
        try
        {
            lock (_gate)
            {
                if (_current is not null)
                {
                    started = StartPlaying();
                }
            }
        }
        finally
        {
            _ops.Release();
        }

        if (!started)
        {
            Failed?.Invoke(this, NoDeviceMessage);
        }
    }

    public async Task PauseAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                StopPlaying();
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_current is { } current)
                {
                    Seek(current, position);
                }
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    public void SetNext(string? path)
    {
        lock (_gate)
        {
            _nextPath = path;
            if (_next is { } prepared && !string.Equals(prepared.Path, path, PathComparison))
            {
                Volatile.Write(ref _gaplessNext, null);
                _next = null;
                prepared.Dispose();
            }
        }

        // When the song is nearly over already, open the next one now.
        _ = Task.Run(PrepareNextIfDueAsync);
    }

    public void SetLooping(bool looping)
    {
        lock (_gate)
        {
            _looping = looping;
            if (_current is { } current)
            {
                SetLoop(current, looping);
            }
        }
    }

    public void SetVolume(double volume)
    {
        lock (_gate)
        {
            _volume = volume;
            ApplyVolume();
        }
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
        lock (_gate)
        {
            _equalizer = LocalEqualizer.From(settings);
            ApplyEqualizer();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _idleTimer.Dispose();
            _ticker.Dispose();
            TearDownGraph();
        }
    }

    // ---- The graph ----

    /// <summary>Makes the graph on first use (call while holding <see cref="_ops"/>).</summary>
    private async Task<(AudioGraph Graph, AudioSubmixNode Bus)> EnsureGraphAsync()
    {
        lock (_gate)
        {
            if (_graph is not null && _bus is not null)
            {
                return (_graph, _bus);
            }
        }

        var graph = await CreateGraphAsync(null).ConfigureAwait(false);
        try
        {
            if (graph.EncodingProperties.SampleRate > MaxGraphRate)
            {
                var format = AudioEncodingProperties.CreatePcm(MaxGraphRate, graph.EncodingProperties.ChannelCount, 32);
                format.Subtype = MediaEncodingSubtypes.Float;
                graph.Dispose();
                graph = await CreateGraphAsync(format).ConfigureAwait(false);
            }

            var output = await graph.CreateDeviceOutputNodeAsync();
            if (output.Status != AudioDeviceNodeCreationStatus.Success)
            {
                throw DeviceProblem(output.ExtendedError);
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _graph = graph;
                _output = output.DeviceOutputNode;
                _bus = graph.CreateSubmixNode();
                _bus.AddOutgoingConnection(_output);

                _lowBands = new EqualizerEffectDefinition(graph);
                _highBands = new EqualizerEffectDefinition(graph);
                _limiter = new LimiterEffectDefinition(graph) { Loudness = 1000, Release = 10 };
                _bus.EffectDefinitions.Add(_lowBands);
                _bus.EffectDefinitions.Add(_highBands);
                _bus.EffectDefinitions.Add(_limiter);

                graph.UnrecoverableErrorOccurred += OnGraphError;
                ApplyVolume();
                ApplyEqualizer();
                return (graph, _bus);
            }
        }
        catch
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_graph, graph))
                {
                    graph.Dispose();
                }
            }

            throw;
        }
    }

    private static async Task<AudioGraph> CreateGraphAsync(AudioEncodingProperties? format)
    {
        var settings = new AudioGraphSettings(AudioRenderCategory.Media)
        {
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.SystemDefault,

            // The default (1024) sets aside buffers for fast playback, which Resonate never uses.
            MaxPlaybackSpeedFactor = 1,
        };
        if (format is not null)
        {
            settings.EncodingProperties = format;
        }

        CreateAudioGraphResult result;
        try
        {
            result = await AudioGraph.CreateAsync(settings);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw DeviceProblem(ex);
        }

        return result.Status == AudioGraphCreationStatus.Success ? result.Graph : throw DeviceProblem(result.ExtendedError);
    }

    /// <summary>Opens <paramref name="path"/> as a stopped file node connected to the EQ bus.</summary>
    private async Task<Track> CreateTrackAsync(AudioGraph graph, AudioSubmixNode bus, string path, CancellationToken cancellationToken)
    {
        // Resonate plays the user's own files, never anything of the Spotify app's.
        if (LocalLibrary.IsInSpotifyFolder(path))
        {
            throw new LocalAudioException($"Resonate does not play “{Name(path)}”: it is in the Spotify app's own folder.");
        }

        StorageFile file;
        try
        {
            file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            throw new LocalAudioException($"“{Name(path)}” is no longer there. It may have been moved or deleted.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new LocalAudioException($"Windows does not let Resonate read “{Name(path)}”.", ex);
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            throw new LocalAudioException($"Resonate could not open “{Name(path)}”.", ex);
        }

        var source = MediaSource.CreateFromStorageFile(file);
        var creating = graph.CreateMediaSourceAudioInputNodeAsync(source).AsTask();
        CreateMediaSourceAudioInputNodeResult result;
        try
        {
            result = await creating.WaitAsync(OpenTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // The node may still arrive; let it go when it does.
            _ = creating.ContinueWith(
                t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        t.Result.Node?.Dispose();
                    }

                    source.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new LocalAudioException($"Opening “{Name(path)}” took too long.", ex);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            source.Dispose();
            throw new LocalAudioException($"Resonate could not play “{Name(path)}”.", ex);
        }

        if (result.Status != MediaSourceAudioInputNodeCreationStatus.Success || result.Node is null)
        {
            source.Dispose();
            throw CanNotPlay(path, result.Status, result.ExtendedError);
        }

        var node = result.Node;
        var track = new Track(path, source, node);
        try
        {
            node.Stop();
            node.AddOutgoingConnection(bus);
            node.MediaSourceCompleted += (_, _) => OnSourceCompleted(track);
            lock (_gate)
            {
                node.OutgoingGain = _equalizer.PreampGain;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            track.Dispose();
            throw new LocalAudioException($"Resonate could not play “{Name(path)}”.", ex);
        }

        return track;
    }

    // ---- Playing (all under the lock) ----

    private void ConfigureCurrent(Track track)
    {
        SetLoop(track, _looping);
        Try(() => track.Node.OutgoingGain = _equalizer.PreampGain);
    }

    /// <returns>False when the sound device would not start; the graph is then gone, and the next song makes a new one.</returns>
    private bool StartPlaying()
    {
        if (_graph is not { } graph || _current is not { } current)
        {
            return true;
        }

        _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            if (!_graphRunning)
            {
                graph.Start();
                _graphRunning = true;

                // Settings made while the graph was stopped may not have reached the equalizer.
                ApplyEqualizer();
            }

            current.Node.Start();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            TearDownGraph();
            return false;
        }

        _playing = true;
        _ticker.Change(TickInterval, TickInterval);
        return true;
    }

    private void StopPlaying()
    {
        if (_current is { } current)
        {
            Try(current.Node.Stop);
        }

        _playing = false;
        _ticker.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _idleTimer.Change(IdleStop, Timeout.InfiniteTimeSpan);
    }

    private static void Seek(Track track, TimeSpan position)
    {
        var duration = DurationOf(track);
        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;

        // Just short of the end, so the song still ends by itself.
        if (duration > TimeSpan.Zero && target > duration - TimeSpan.FromMilliseconds(100))
        {
            target = duration > TimeSpan.FromMilliseconds(100) ? duration - TimeSpan.FromMilliseconds(100) : TimeSpan.Zero;
        }

        Try(() => track.Node.Seek(target));
    }

    private static void SetLoop(Track track, bool looping) =>
        Try(() => track.Node.LoopCount = looping ? null : 0);

    private void ApplyVolume()
    {
        if (_output is { } output)
        {
            Try(() => output.OutgoingGain = LocalVolume.ToGain(_volume));
        }
    }

    private void ApplyEqualizer()
    {
        if (_graph is not { } graph || _bus is not { } bus || _lowBands is not { } low || _highBands is not { } high || _limiter is not { } limiter)
        {
            return;
        }

        var equalizer = _equalizer;
        try
        {
            // One batch, so the bands, the preamp and the limiter change together.
            using (graph.CreateBatchUpdater())
            {
                SetBands(low, equalizer.Bands, 0);
                SetBands(high, equalizer.Bands, 4);
                if (equalizer.IsActive)
                {
                    bus.EnableEffectsByDefinition(low);
                    bus.EnableEffectsByDefinition(high);
                }
                else
                {
                    bus.DisableEffectsByDefinition(low);
                    bus.DisableEffectsByDefinition(high);
                }

                if (equalizer.NeedsLimiter)
                {
                    bus.EnableEffectsByDefinition(limiter);
                }
                else
                {
                    bus.DisableEffectsByDefinition(limiter);
                }

                foreach (var track in new[] { _current, _next })
                {
                    if (track is not null)
                    {
                        track.Node.OutgoingGain = equalizer.PreampGain;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            // The music keeps playing, without the change.
        }
    }

    private static void SetBands(EqualizerEffectDefinition definition, IReadOnlyList<EqualizerEffectBand> bands, int first)
    {
        var effectBands = definition.Bands;
        for (var i = 0; i < effectBands.Count && first + i < bands.Count; i++)
        {
            var band = bands[first + i];
            var effectBand = effectBands[i];
            effectBand.FrequencyCenter = band.FrequencyHz;
            effectBand.Bandwidth = band.Bandwidth;
            effectBand.Gain = band.Gain;
        }
    }

    // ---- Events from the audio thread ----

    /// <summary>On the audio thread: the song ended. Starts the prepared next one at once, then hands over.</summary>
    private void OnSourceCompleted(Track finished)
    {
        Track? started = null;
        try
        {
            if (ReferenceEquals(Volatile.Read(ref _current), finished) && Volatile.Read(ref _gaplessNext) is { } next)
            {
                next.Node.Start();
                started = next;
            }
        }
        catch (Exception)
        {
            // OnCompleted starts it instead. Nothing may stop the hand-over below.
        }

        _ = Task.Run(() => OnCompleted(finished, started));
    }

    /// <summary>
    /// Once a second while playing: prepares the next song when it is due,
    /// and makes sure a song that stopped moving does not stop the music.
    /// </summary>
    private void OnTick()
    {
        CheckStalled();
        _ = PrepareNextIfDueAsync();
    }

    /// <summary>
    /// A song that sat still at its end for two ticks has ended, even when
    /// Windows did not say so; a song that should play but sits still
    /// elsewhere is started again (a start Windows dropped).
    /// </summary>
    private void CheckStalled()
    {
        Track? ended = null;
        lock (_gate)
        {
            if (_disposed || !_playing || _current is not { } current)
            {
                return;
            }

            var position = PositionOf(current);
            current.StuckTicks = position == current.LastTick ? current.StuckTicks + 1 : 0;
            current.LastTick = position;
            if (current.StuckTicks < 2)
            {
                return;
            }

            var duration = DurationOf(current);
            if ((duration > TimeSpan.Zero && position >= duration - EndSlack) || (current.StuckTicks >= GiveUpTicks && position > TimeSpan.Zero))
            {
                current.StuckTicks = 0;
                ended = current;
            }
            else
            {
                Try(current.Node.Start);
            }
        }

        // Ignored when Windows' own report came first (OnCompleted checks the song is still open).
        if (ended is not null)
        {
            OnSourceCompleted(ended);
        }
    }

    private void OnCompleted(Track finished, Track? started)
    {
        string? nextPath = null;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_current, finished))
            {
                // Something newer replaced the song; a next one started for it must not play.
                if (started is not null && !ReferenceEquals(started, _current))
                {
                    Try(started.Node.Stop);
                    DropNext(started);
                }

                return;
            }

            // The prepared song did not start inside Windows' callback: start it here.
            if (started is null && _playing && _next is { } prepared && string.Equals(prepared.Path, _nextPath, PathComparison))
            {
                try
                {
                    prepared.Node.Start();
                    started = prepared;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or ObjectDisposedException)
                {
                    // The player opens it the usual way.
                }
            }

            finished.Dispose();
            if (started is not null && ReferenceEquals(started, _next))
            {
                _next = null;
                Volatile.Write(ref _gaplessNext, null);
                Volatile.Write(ref _current, started);
                ConfigureCurrent(started);
                nextPath = started.Path;
            }
            else
            {
                Volatile.Write(ref _current, null);
                if (started is not null)
                {
                    Try(started.Node.Stop);
                    DropNext(started);
                }

                StopPlaying();
            }
        }

        TrackEnded?.Invoke(this, new LocalTrackEnded(finished.Path, nextPath));
    }

    private async Task PrepareNextIfDueAsync()
    {
        string path;
        AudioGraph graph;
        AudioSubmixNode bus;
        lock (_gate)
        {
            if (_disposed || !_playing || _preparing || _looping || _next is not null
                || _nextPath is not { } nextPath || string.Equals(nextPath, _failedNextPath, PathComparison)
                || _current is not { } current || _graph is null || _bus is null)
            {
                return;
            }

            var duration = DurationOf(current);
            if (duration <= TimeSpan.Zero || duration - PositionOf(current) > PrepareAhead)
            {
                return;
            }

            path = nextPath;
            graph = _graph;
            bus = _bus;
            _preparing = true;
        }

        Track? track = null;
        try
        {
            track = await CreateTrackAsync(graph, bus, path, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The player opens it when it is due, and says what is wrong.
            lock (_gate)
            {
                _failedNextPath = path;
            }
        }
        finally
        {
            lock (_gate)
            {
                _preparing = false;
                if (track is not null)
                {
                    if (_disposed || _next is not null || !ReferenceEquals(graph, _graph) || !string.Equals(_nextPath, path, PathComparison))
                    {
                        track.Dispose();
                    }
                    else
                    {
                        _next = track;
                        Volatile.Write(ref _gaplessNext, track);
                    }
                }
            }
        }
    }

    private void OnGraphError(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args) =>
        _ = Task.Run(() => RecoverAsync(sender));

    /// <summary>The sound device went away or changed: a new graph on the device there is now, from where the song was.</summary>
    private async Task RecoverAsync(AudioGraph failed)
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        var recovered = true;
        try
        {
            string? path;
            TimeSpan position;
            bool playing;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_graph, failed))
                {
                    return;
                }

                path = _current?.Path;
                position = PositionOf(_current);
                playing = _playing;
                TearDownGraph();
            }

            if (path is null)
            {
                // Nothing was open: the next song makes a new graph.
                return;
            }

            try
            {
                var (graph, bus) = await EnsureGraphAsync().ConfigureAwait(false);
                var track = await CreateTrackAsync(graph, bus, path, CancellationToken.None).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        track.Dispose();
                        return;
                    }

                    Volatile.Write(ref _current, track);
                    ConfigureCurrent(track);
                    Seek(track, position);
                    if (!playing)
                    {
                        StopPlaying();
                    }
                    else if (!StartPlaying())
                    {
                        recovered = false;
                    }
                }
            }
            catch (Exception ex) when (ex is LocalAudioException or COMException or InvalidOperationException or ObjectDisposedException)
            {
                recovered = false;
                lock (_gate)
                {
                    TearDownGraph();
                }
            }
        }
        finally
        {
            _ops.Release();
        }

        if (!recovered)
        {
            Failed?.Invoke(this, LostDeviceMessage);
        }
    }

    private void StopWhenIdle()
    {
        lock (_gate)
        {
            if (_disposed || _playing || !_graphRunning || _graph is null)
            {
                return;
            }

            // A paused graph still holds the sound device open; let it go.
            if (_next is { } next)
            {
                DropNext(next);
            }

            Try(_graph.Stop);
            _graphRunning = false;
        }
    }

    /// <summary>Stops and closes the open song and the one prepared to follow it. Call under the lock.</summary>
    private void CloseCurrent()
    {
        _nextPath = null;
        if (_next is { } next)
        {
            DropNext(next);
        }

        StopPlaying();
        _current?.Dispose();
        Volatile.Write(ref _current, null);
    }

    /// <summary>Call under the lock.</summary>
    private void DropNext(Track track)
    {
        if (ReferenceEquals(_next, track))
        {
            _next = null;
            Volatile.Write(ref _gaplessNext, null);
        }

        track.Dispose();
    }

    /// <summary>Call under the lock.</summary>
    private void TearDownGraph()
    {
        Volatile.Write(ref _gaplessNext, null);
        _next?.Dispose();
        _next = null;
        _current?.Dispose();
        Volatile.Write(ref _current, null);
        _playing = false;
        _graphRunning = false;
        _failedNextPath = null;

        if (_graph is { } graph)
        {
            graph.UnrecoverableErrorOccurred -= OnGraphError;
            Try(graph.Stop);
            Try(() => _bus?.Dispose());
            Try(() => _output?.Dispose());
            Try(graph.Dispose);
        }

        _graph = null;
        _output = null;
        _bus = null;
        _lowBands = null;
        _highBands = null;
        _limiter = null;
    }

    // ---- Helpers ----

    private static TimeSpan PositionOf(Track? track)
    {
        if (track is null)
        {
            return TimeSpan.Zero;
        }

        try
        {
            return track.Node.Position;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return TimeSpan.Zero;
        }
    }

    private static TimeSpan DurationOf(Track track)
    {
        try
        {
            return track.Node.Duration;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return TimeSpan.Zero;
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            // A node that is going away; nothing to do.
        }
    }

    private static string Name(string path) => Path.GetFileName(path);

    private static LocalAudioException DeviceProblem(Exception? error) =>
        error is null
            ? new LocalAudioException(NoDeviceMessage) { IsDeviceProblem = true }
            : new LocalAudioException(NoDeviceMessage, error) { IsDeviceProblem = true };

    private static LocalAudioException CanNotPlay(string path, MediaSourceAudioInputNodeCreationStatus status, Exception? error)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var message = status switch
        {
            MediaSourceAudioInputNodeCreationStatus.FormatNotSupported when extension is ".ogg" or ".opus" =>
                $"Windows can not play “{Name(path)}” yet. Install Microsoft's free Web Media Extensions from the Microsoft Store to play Ogg and Opus files.",
            MediaSourceAudioInputNodeCreationStatus.FormatNotSupported =>
                $"Windows can not play “{Name(path)}”: its format is not supported.",
            _ => $"Resonate could not play “{Name(path)}”.",
        };
        return error is null ? new LocalAudioException(message) : new LocalAudioException(message, error);
    }

    /// <summary>An open file: its node in the graph and the media source behind it.</summary>
    private sealed class Track(string path, MediaSource source, MediaSourceAudioInputNode node) : IDisposable
    {
        public string Path { get; } = path;

        public MediaSourceAudioInputNode Node { get; } = node;

        /// <summary>The position at the last tick, and how many ticks in a row it stayed there (under the engine's lock).</summary>
        public TimeSpan LastTick { get; set; } = TimeSpan.MinValue;

        public int StuckTicks { get; set; }

        public void Dispose()
        {
            Try(Node.Dispose);
            Try(source.Dispose);
        }
    }
}

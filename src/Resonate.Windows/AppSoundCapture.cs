using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Playback;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Hears what one program plays, and the programs it started, through
/// Windows' process loopback: for the Home stage's visualizer only. Windows
/// copies the program's sound as it mixes it, so the program plays exactly
/// as before (Lossless included); nothing else on the PC is heard, and the
/// copy goes straight to the visualizer's analyser and is dropped. Each
/// capture runs on a thread of its own: 16-bit stereo at 48 kHz, read when
/// Windows signals a new packet (about every 10 ms).
/// </summary>
public sealed partial class AppSoundCapture : IAppSoundCapture, IDisposable
{
    private const int Channels = 2;
    private const int SampleRate = 48_000;
    private const int BitsPerSample = 16;

    /// <summary>The shared buffer Windows keeps for the capture: room for a busy moment, not a recording.</summary>
    private const long BufferDuration = 1_000_000; // 100 ms in 100-ns units

    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>With no packet for this long the sink hears an empty write, so it can tell silence from a capture that stopped.</summary>
    private static readonly TimeSpan QuietStep = TimeSpan.FromMilliseconds(100);

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private readonly Lock _gate = new();
    private Session? _session;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

    public event EventHandler? Failed;

    public void Start(int processId, ISoundSink sink)
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = new Session(this, processId, sink);
            _session.Begin();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The Spotify app's main process (the one without a <c>--type</c>), whose
    /// tree includes its audio service; null when Spotify does not run. A few
    /// milliseconds: call off the interface thread.
    /// </summary>
    public static int? FindSpotify()
    {
        var processes = Process.GetProcessesByName("Spotify");
        try
        {
            foreach (var process in processes)
            {
                if (SpotifyProcesses.Classify(SpotifyBackground.CommandLine((uint)process.Id)) == SpotifyProcessKind.Main)
                {
                    return process.Id;
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void OnSessionFailed(Session session)
    {
        lock (_gate)
        {
            if (_session != session)
            {
                return;
            }

            _session = null;
        }

        Failed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// One capture of one program, from activation to release, on its own
    /// thread. Disposing asks the thread to finish; the thread releases
    /// Windows' objects and the event itself, once Windows no longer holds it.
    /// </summary>
    private sealed unsafe class Session(AppSoundCapture owner, int processId, ISoundSink sink) : IDisposable
    {
        private readonly AutoResetEvent _packets = new(false);
        private volatile bool _stopping;

        public void Begin()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Resonate visualizer sound" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Dispose()
        {
            _stopping = true;
            try
            {
                _packets.Set();
            }
            catch (ObjectDisposedException)
            {
                // The thread already finished.
            }
        }

        private void Run()
        {
            nint client = 0;
            nint capture = 0;
            var started = false;
            var failed = true;
            try
            {
                if (_stopping)
                {
                    // Replaced before the thread began: nothing to ask Windows for.
                    return;
                }

                client = Activate(processId);
                if (client == 0 || _stopping)
                {
                    return;
                }

                var format = new WaveFormat
                {
                    FormatTag = ProcessLoopbackIds.FormatPcm,
                    Channels = Channels,
                    SamplesPerSecond = SampleRate,
                    AverageBytesPerSecond = SampleRate * Channels * (BitsPerSample / 8),
                    BlockAlign = Channels * (BitsPerSample / 8),
                    BitsPerSample = BitsPerSample,
                    ExtraSize = 0,
                };
                var initialize = (delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormat*, Guid*, int>)Slot(client, ProcessLoopbackIds.InitializeSlot);
                var flags = ProcessLoopbackIds.StreamFlagsLoopback | ProcessLoopbackIds.StreamFlagsEventCallback
                    | ProcessLoopbackIds.StreamFlagsAutoConvertPcm | ProcessLoopbackIds.StreamFlagsSrcDefaultQuality;
                if (initialize(client, ProcessLoopbackIds.ShareModeShared, flags, BufferDuration, 0, &format, null) < 0)
                {
                    return;
                }

                var setEvent = (delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(client, ProcessLoopbackIds.SetEventHandleSlot);
                if (setEvent(client, _packets.SafeWaitHandle.DangerousGetHandle()) < 0)
                {
                    return;
                }

                var iid = ProcessLoopbackIds.IAudioCaptureClient;
                var getService = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(client, ProcessLoopbackIds.GetServiceSlot);
                if (getService(client, &iid, &capture) < 0 || capture == 0)
                {
                    return;
                }

                var start = (delegate* unmanaged[Stdcall]<nint, int>)Slot(client, ProcessLoopbackIds.StartSlot);
                if (_stopping || start(client) < 0)
                {
                    return;
                }

                started = true;
                failed = !Pump(capture);
            }
            catch (Exception)
            {
                // A thread of its own: nothing may escape it. The visualizer sways on its own instead.
                failed = true;
            }
            finally
            {
                if (started)
                {
                    ((delegate* unmanaged[Stdcall]<nint, int>)Slot(client, ProcessLoopbackIds.StopSlot))(client);
                }

                if (capture != 0)
                {
                    Marshal.Release(capture);
                }

                if (client != 0)
                {
                    Marshal.Release(client);
                }

                // Only once Windows no longer holds the event (the client is released).
                _packets.Dispose();
                if (failed && !_stopping)
                {
                    owner.OnSessionFailed(this);
                }
            }
        }

        /// <summary>Reads packets until stopped. False when Windows reported an error (the program went away, the audio service restarted).</summary>
        private bool Pump(nint capture)
        {
            var getNext = (delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(capture, ProcessLoopbackIds.GetNextPacketSizeSlot);
            var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)Slot(capture, ProcessLoopbackIds.GetBufferSlot);
            var release = (delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(capture, ProcessLoopbackIds.ReleaseBufferSlot);
            var samples = new float[SampleRate / 10 * Channels];
            var lastWrite = Stopwatch.GetTimestamp();
            while (!_stopping)
            {
                _packets.WaitOne(QuietStep);
                var wrote = false;
                while (!_stopping)
                {
                    uint next;
                    if (getNext(capture, &next) < 0)
                    {
                        return false;
                    }

                    if (next == 0)
                    {
                        break;
                    }

                    byte* data;
                    uint frames;
                    uint bufferFlags;
                    var result = getBuffer(capture, &data, &frames, &bufferFlags, null, null);
                    if (result < 0)
                    {
                        return false;
                    }

                    if (result == ProcessLoopbackIds.BufferEmpty)
                    {
                        break;
                    }

                    var count = (int)frames * Channels;
                    if (samples.Length < count)
                    {
                        samples = new float[count];
                    }

                    var block = samples.AsSpan(0, count);
                    if ((bufferFlags & ProcessLoopbackIds.BufferFlagsSilent) != 0 || data == null)
                    {
                        block.Clear();
                    }
                    else
                    {
                        var pcm = new ReadOnlySpan<short>(data, count);
                        for (var i = 0; i < count; i++)
                        {
                            block[i] = pcm[i] * (1f / 32768);
                        }
                    }

                    if (release(capture, frames) < 0)
                    {
                        return false;
                    }

                    if (count > 0 && !_stopping)
                    {
                        sink.Write(block, Channels, SampleRate);
                        wrote = true;
                    }
                }

                var now = Stopwatch.GetTimestamp();
                if (wrote)
                {
                    lastWrite = now;
                }
                else if (!_stopping && Stopwatch.GetElapsedTime(lastWrite, now) >= QuietStep)
                {
                    sink.Write([], Channels, SampleRate);
                    lastWrite = now;
                }
            }

            return true;
        }

        /// <summary>An AddRef'd IAudioClient that hears <paramref name="target"/>'s process tree, or 0 when Windows refused.</summary>
        private static nint Activate(int target)
        {
            // Native memory: if Windows answers after the wait gave up, it still reads valid parameters.
            var parameters = (ProcessLoopbackParams*)NativeMemory.Alloc((nuint)sizeof(ProcessLoopbackParams));
            var variant = (PropVariant*)NativeMemory.AllocZeroed((nuint)sizeof(PropVariant));
            *parameters = new ProcessLoopbackParams
            {
                ActivationType = ProcessLoopbackIds.ActivationTypeProcessLoopback,
                TargetProcessId = (uint)target,
                LoopbackMode = ProcessLoopbackIds.LoopbackModeIncludeTree,
            };
            variant->Type = PropVariant.Blob;
            variant->Value = sizeof(ProcessLoopbackParams);
            variant->Value2 = (nint)parameters;

            var completed = new ActivationCompleted();
            nint operation = 0;
            var answered = false;
            try
            {
                var unknown = ComWrappers.GetOrCreateComInterfaceForObject(completed, CreateComInterfaceFlags.None);
                nint handler;
                try
                {
                    var handlerIid = ProcessLoopbackIds.IActivateAudioInterfaceCompletionHandler;
                    if (Marshal.QueryInterface(unknown, in handlerIid, out handler) < 0)
                    {
                        answered = true;
                        return 0;
                    }
                }
                finally
                {
                    Marshal.Release(unknown);
                }

                int activation;
                try
                {
                    activation = MmDevApi.ActivateAudioInterfaceAsync(ProcessLoopbackIds.DevicePath, ProcessLoopbackIds.IAudioClient, variant, handler, out operation);
                }
                finally
                {
                    Marshal.Release(handler);
                }

                if (activation < 0 || operation == 0)
                {
                    answered = true;
                    return 0;
                }

                if (!completed.Done.Wait(ActivationTimeout))
                {
                    return 0;
                }

                answered = true;
                int result;
                nint activated;
                var getResult = (delegate* unmanaged[Stdcall]<nint, int*, nint*, int>)Slot(operation, ProcessLoopbackIds.GetActivateResultSlot);
                if (getResult(operation, &result, &activated) < 0 || result < 0 || activated == 0)
                {
                    return 0;
                }

                try
                {
                    var clientIid = ProcessLoopbackIds.IAudioClient;
                    return Marshal.QueryInterface(activated, in clientIid, out var client) >= 0 ? client : 0;
                }
                finally
                {
                    Marshal.Release(activated);
                }
            }
            finally
            {
                if (operation != 0)
                {
                    Marshal.Release(operation);
                }

                // Left alone (a few bytes) if Windows never answered, since it may still read them.
                if (answered)
                {
                    NativeMemory.Free(variant);
                    NativeMemory.Free(parameters);
                }
            }
        }

        private static nint Slot(nint instance, int slot) => (*(nint**)instance)[slot];
    }

    /// <summary>Lets the capture thread know the activation finished. Agile, as Windows requires.</summary>
    [GeneratedComClass]
    private sealed partial class ActivationCompleted : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEventSlim Done { get; } = new(false);

        public void ActivateCompleted(nint activateOperation) => Done.Set();
    }
}

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Resonate.Windows.Interop;

// Windows' process loopback: a copy of the sound one process tree plays, as
// the audio engine mixes it (ActivateAudioInterfaceAsync on the virtual
// device "VAD\Process_Loopback", Windows 10 build 20348 and later). The
// completion handler is a source-generated COM class (Native AOT); the audio
// client is called through its vtable, like MemoryBufferAccess, so every
// reference is released at a known moment and the capture loop makes no
// managed objects. IIDs, slots and structures are from the Windows SDK's
// audioclient.h, audioclientactivationparams.h and mmdeviceapi.h.

internal static class ProcessLoopbackIds
{
    /// <summary>VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK.</summary>
    public const string DevicePath = @"VAD\Process_Loopback";

    public static readonly Guid IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    public static readonly Guid IActivateAudioInterfaceCompletionHandler = new("41D949AB-9862-444A-80F6-C261334DA5EB");

    public const int ActivationTypeProcessLoopback = 1;     // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
    public const int LoopbackModeIncludeTree = 0;           // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE

    public const int ShareModeShared = 0;                    // AUDCLNT_SHAREMODE_SHARED
    public const uint StreamFlagsLoopback = 0x0002_0000;     // AUDCLNT_STREAMFLAGS_LOOPBACK
    public const uint StreamFlagsEventCallback = 0x0004_0000; // AUDCLNT_STREAMFLAGS_EVENTCALLBACK
    public const uint StreamFlagsAutoConvertPcm = 0x8000_0000; // AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
    public const uint StreamFlagsSrcDefaultQuality = 0x0800_0000; // AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY

    public const uint BufferFlagsSilent = 0x2;               // AUDCLNT_BUFFERFLAGS_SILENT
    public const int BufferEmpty = 0x0889_0001;              // AUDCLNT_S_BUFFER_EMPTY

    public const ushort FormatPcm = 1;                       // WAVE_FORMAT_PCM

    // IAudioClient, after IUnknown's three slots.
    public const int InitializeSlot = 3;
    public const int StartSlot = 10;
    public const int StopSlot = 11;
    public const int SetEventHandleSlot = 13;
    public const int GetServiceSlot = 14;

    // IAudioCaptureClient.
    public const int GetBufferSlot = 3;
    public const int ReleaseBufferSlot = 4;
    public const int GetNextPacketSizeSlot = 5;

    // IActivateAudioInterfaceAsyncOperation.
    public const int GetActivateResultSlot = 3;
}

/// <summary>AUDIOCLIENT_ACTIVATION_PARAMS with its PROCESS_LOOPBACK_PARAMS: 12 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ProcessLoopbackParams
{
    public int ActivationType;
    public uint TargetProcessId;
    public int LoopbackMode;
}

/// <summary>WAVEFORMATEX, packed as mmreg.h declares it: 18 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormat
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

/// <summary>Told when an activation finished. Windows calls it on a thread of its own, so it must be agile.</summary>
[GeneratedComInterface]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
internal partial interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(nint activateOperation);
}

/// <summary>IAgileObject: marks an object that may be called from any thread (no methods).</summary>
[GeneratedComInterface]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileObject
{
}

internal static partial class MmDevApi
{
    [LibraryImport("mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        in Guid riid,
        PropVariant* activationParams,
        nint completionHandler,
        out nint activationOperation);
}

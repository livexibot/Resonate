using System.Runtime.InteropServices;
using Windows.Media;
using WinRT;

namespace Resonate.Windows.Interop;

// Reading an audio frame's samples on the audio thread, under Native AOT,
// without making a single managed object. The documented C# way casts the
// buffer's reference to a [ComImport] IMemoryBufferByteAccess, which needs
// the built-in COM that Native AOT does not have; the projection's own
// wrappers work, but make three small objects with finalizers every 10 ms
// (and look each one up in a shared cache). So these are plain calls through
// the interfaces' vtables. Every IID and slot below was read from the Windows
// SDK projection (Microsoft.Windows.SDK.NET 10.0.26100.57, its ABI *Methods
// classes and WinRT.Runtime's IClosable) and from memorybuffer.h.

internal static class MemoryBufferIds
{
    public static readonly Guid IAudioFrameOutputNode = new("B847371B-3299-45F5-88B3-C9D12A3F1CC8");
    public static readonly Guid IMemoryBuffer = new("FBC4DD2A-245B-11E4-AF98-689423260CF8");
    public static readonly Guid IMemoryBufferByteAccess = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
    public static readonly Guid IClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");

    // WinRT interfaces start after IUnknown's three slots and IInspectable's three.
    public const int GetFrameSlot = 6;         // IAudioFrameOutputNode.GetFrame(out IAudioFrame)
    public const int LockBufferSlot = 6;       // IAudioFrame.LockBuffer(AudioBufferAccessMode, out IAudioBuffer)
    public const int BufferLengthSlot = 7;     // IAudioBuffer.get_Length(out uint), after get_Capacity
    public const int CreateReferenceSlot = 6;  // IMemoryBuffer.CreateReference(out IMemoryBufferReference)
    public const int CloseSlot = 6;            // IClosable.Close()
    public const int GetBufferSlot = 3;        // IMemoryBufferByteAccess.GetBuffer(out byte*, out uint), a plain COM interface

    public const int AccessModeRead = (int)AudioBufferAccessMode.Read;
}

/// <summary>
/// One audio frame taken from a frame output node: its samples stay
/// readable until <see cref="Dispose"/>, which closes and releases the
/// frame, its buffer and the buffer's reference. Allocates nothing and never
/// throws for a failing call: the samples are just empty.
/// </summary>
internal unsafe ref struct AudioFrameSamples
{
    private nint _frame;
    private nint _buffer;
    private nint _reference;

    /// <summary>Interleaved 32-bit float samples, or empty when nothing could be read.</summary>
    public ReadOnlySpan<float> Samples { get; private set; }

    /// <summary>
    /// An AddRef'd IAudioFrameOutputNode pointer for <paramref name="node"/>
    /// (release it with <see cref="Marshal.Release"/>), from the object the
    /// projection already holds. Call off the audio thread.
    /// </summary>
    public static bool TryGetSource(object node, out nint source)
    {
        source = 0;
        return node is IWinRTObject winrt
            && winrt.NativeObject.TryAs(MemoryBufferIds.IAudioFrameOutputNode, out source) >= 0
            && source != 0;
    }

    /// <summary>
    /// What <paramref name="source"/> (an IAudioFrameOutputNode) gathered
    /// since the last call. Dispose the result after reading the samples.
    /// </summary>
    public static AudioFrameSamples Take(nint source)
    {
        var taken = default(AudioFrameSamples);
        nint frame;
        if (GetObject(source, MemoryBufferIds.GetFrameSlot, &frame) < 0 || frame == 0)
        {
            return taken;
        }

        taken._frame = frame;
        nint buffer = 0;
        var lockBuffer = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(frame, MemoryBufferIds.LockBufferSlot);
        if (lockBuffer(frame, MemoryBufferIds.AccessModeRead, &buffer) < 0 || buffer == 0)
        {
            return taken;
        }

        taken._buffer = buffer;
        uint length = 0;
        var getLength = (delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(buffer, MemoryBufferIds.BufferLengthSlot);
        if (getLength(buffer, &length) < 0 || length == 0
            || Marshal.QueryInterface(buffer, in MemoryBufferIds.IMemoryBuffer, out var memoryBuffer) < 0)
        {
            return taken;
        }

        nint reference;
        var created = GetObject(memoryBuffer, MemoryBufferIds.CreateReferenceSlot, &reference);
        Marshal.Release(memoryBuffer);
        if (created < 0 || reference == 0)
        {
            return taken;
        }

        taken._reference = reference;
        if (Marshal.QueryInterface(reference, in MemoryBufferIds.IMemoryBufferByteAccess, out var access) < 0)
        {
            return taken;
        }

        // The bytes stay valid while the reference is open; this only drops the extra interface.
        byte* data = null;
        uint capacity = 0;
        var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)Slot(access, MemoryBufferIds.GetBufferSlot);
        var got = getBuffer(access, &data, &capacity);
        Marshal.Release(access);
        if (got < 0 || data == null)
        {
            return taken;
        }

        var floats = Math.Min(length, capacity) / sizeof(float);
        taken.Samples = new ReadOnlySpan<float>(data, (int)Math.Min(floats, int.MaxValue));
        return taken;
    }

    /// <summary>Closes the reference, then the buffer (which unlocks the frame), then the frame, as disposing the projection's objects would.</summary>
    public void Dispose()
    {
        Samples = default;
        CloseAndRelease(ref _reference);
        CloseAndRelease(ref _buffer);
        CloseAndRelease(ref _frame);
    }

    private static void* Slot(nint instance, int slot) => (*(void***)instance)[slot];

    private static int GetObject(nint instance, int slot, nint* result)
    {
        *result = 0;
        return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, result);
    }

    private static void CloseAndRelease(ref nint instance)
    {
        var target = instance;
        instance = 0;
        if (target == 0)
        {
            return;
        }

        if (Marshal.QueryInterface(target, in MemoryBufferIds.IClosable, out var closable) >= 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, int>)Slot(closable, MemoryBufferIds.CloseSlot))(closable);
            Marshal.Release(closable);
        }

        Marshal.Release(target);
    }
}

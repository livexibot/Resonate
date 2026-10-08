using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Resonate.Windows.Interop;

// What Signal path reads about the default sound output: its property store
// (name, shared-mode format, where it is plugged in) and Core Audio's device
// notifications. Source-generated COM, so it works with Native AOT.

/// <summary>PROPERTYKEY: a property's set and number.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

/// <summary>
/// PROPVARIANT, as far as Resonate reads it: the type, then 16 bytes of value
/// on 64-bit Windows (a pointer, a number, or a BLOB's size and pointer).
/// Always cleared with <see cref="Ole32Properties.PropVariantClear"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort Empty = 0;
    public const ushort UInt32 = 19;
    public const ushort WideString = 31;
    public const ushort Blob = 65;

    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;

    /// <summary>A string pointer, a number, or a BLOB's size (its low 32 bits).</summary>
    public nint Value;

    /// <summary>A BLOB's data pointer.</summary>
    public nint Value2;
}

internal static class AudioEndpointKeys
{
    private static readonly Guid DeviceProperties = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    /// <summary>PKEY_Device_FriendlyName: "Speakers (Realtek(R) Audio)".</summary>
    public static readonly PropertyKey FriendlyName = new(DeviceProperties, 14);

    /// <summary>PKEY_Device_EnumeratorName: the bus, such as "HDAUDIO", "USB" or "BTHENUM", when the store has it.</summary>
    public static readonly PropertyKey EnumeratorName = new(DeviceProperties, 24);

    /// <summary>
    /// The audio device's instance path ("{1}.USB\VID_…", "{1}.BTHENUM\…"),
    /// as endpoint property stores carry it. Not documented; a second way to
    /// spot Bluetooth.
    /// </summary>
    public static readonly PropertyKey DeviceInstancePath = new(new Guid("B3F8FA53-0004-438E-9003-51A46E139BFC"), 2);

    /// <summary>PKEY_AudioEngine_DeviceFormat: the shared-mode format (a WAVEFORMATEX), what Windows mixes at.</summary>
    public static readonly PropertyKey DeviceFormat = new(new Guid("F19F064D-082C-4E27-BC73-6882A1BB8E4C"), 0);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    uint GetCount();

    void GetAt(uint index, out PropertyKey key);

    void GetValue(in PropertyKey key, out PropVariant value);
}

/// <summary>Core Audio's device notifications. Called on a Windows audio thread: must return quickly.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    void OnDeviceStateChanged(string deviceId, uint newState);

    void OnDeviceAdded(string deviceId);

    void OnDeviceRemoved(string deviceId);

    void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId);

    void OnPropertyValueChanged(string deviceId, PropertyKey key);
}

internal static partial class Ole32Properties
{
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);
}

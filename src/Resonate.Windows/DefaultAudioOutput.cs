using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Resonate.Spotify.Audio;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// The default sound output, for Signal path: its name, the shared-mode
/// format Windows mixes at, and whether it is Bluetooth. A few property
/// reads through Core Audio; call off the interface thread. Reads only.
/// </summary>
public static partial class DefaultAudioOutput
{
    private const uint ReadAccess = 0; // STGM_READ
    private const ushort WaveFormatExtensible = 0xFFFE;
    private const int MaxFormatBytes = 64;

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>The default output for music, or null when there is none or Windows did not answer.</summary>
    public static AudioOutputInfo? TryRead()
    {
        try
        {
            var device = CreateEnumerator().GetDefaultAudioEndpoint(CoreAudioIds.ERender, CoreAudioIds.EMultimedia);
            var store = device.OpenPropertyStore(ReadAccess);
            var name = ReadString(store, AudioEndpointKeys.FriendlyName) ?? "Sound output";
            var (rate, bits) = ReadFormat(store);
            var bluetooth = IsBluetoothPath(ReadString(store, AudioEndpointKeys.EnumeratorName))
                || IsBluetoothPath(ReadString(store, AudioEndpointKeys.DeviceInstancePath))
                || name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase);
            return new AudioOutputInfo(name, rate, bits, bluetooth);
        }
        catch (Exception ex) when (IsAudioServiceFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Calls <paramref name="changed"/> (on a Windows audio thread) when the
    /// default output or its format changes, or a device comes or goes.
    /// Null when Windows did not allow it. Dispose to stop.
    /// </summary>
    public static IDisposable? Watch(Action changed)
    {
        try
        {
            var enumerator = CreateEnumerator();
            var client = new NotificationClient(changed);
            enumerator.RegisterEndpointNotificationCallback(client);
            return new Registration(enumerator, client);
        }
        catch (Exception ex) when (IsAudioServiceFailure(ex))
        {
            return null;
        }
    }

    /// <summary>What a Core Audio call can throw (.NET turns some failure codes into other exceptions than COMException).</summary>
    private static bool IsAudioServiceFailure(Exception ex) =>
        ex is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException or NotImplementedException or IOException;

    /// <summary>Bluetooth audio comes through the BTHENUM, BTHHFENUM or BTHLE enumerators.</summary>
    internal static bool IsBluetoothPath(string? path) =>
        path is not null && (path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BTHLE", StringComparison.OrdinalIgnoreCase));

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        Marshal.ThrowExceptionForHR(Ole32.CoCreateInstance(
            CoreAudioIds.MMDeviceEnumerator, 0, CoreAudioIds.ClsCtxAll, CoreAudioIds.IMMDeviceEnumerator, out var pointer));
        try
        {
            return (IMMDeviceEnumerator)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static string? ReadString(IPropertyStore store, in PropertyKey key)
    {
        store.GetValue(key, out var value);
        try
        {
            return value.Type == PropVariant.WideString && value.Value != 0 ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally
        {
            Ole32Properties.PropVariantClear(ref value);
        }
    }

    /// <summary>The shared-mode rate and depth from the WAVEFORMATEX(TENSIBLE) Windows keeps for the device.</summary>
    private static (int Rate, int Bits) ReadFormat(IPropertyStore store)
    {
        store.GetValue(AudioEndpointKeys.DeviceFormat, out var value);
        try
        {
            var size = (int)(value.Value & 0xFFFF_FFFF);
            if (value.Type != PropVariant.Blob || value.Value2 == 0 || size < 16)
            {
                return (0, 0);
            }

            var bytes = new byte[Math.Min(size, MaxFormatBytes)];
            Marshal.Copy(value.Value2, bytes, 0, bytes.Length);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            var rate = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)), int.MaxValue);
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));
            if (tag == WaveFormatExtensible && bytes.Length >= 20 && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(18)) is > 0 and var valid)
            {
                bits = valid;
            }

            return (rate, bits);
        }
        finally
        {
            Ole32Properties.PropVariantClear(ref value);
        }
    }

    private sealed class Registration(IMMDeviceEnumerator enumerator, NotificationClient client) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            try
            {
                enumerator.UnregisterEndpointNotificationCallback(client);
            }
            catch (Exception ex) when (IsAudioServiceFailure(ex))
            {
                // Windows' audio service went away; nothing is registered any more.
            }
        }
    }

    /// <summary>Hands Core Audio's notifications on, only for what changes the output or its format.</summary>
    [GeneratedComClass]
    private sealed partial class NotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => changed();

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            if (flow == CoreAudioIds.ERender)
            {
                changed();
            }
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            if (key == AudioEndpointKeys.DeviceFormat || key == AudioEndpointKeys.FriendlyName)
            {
                changed();
            }
        }
    }
}

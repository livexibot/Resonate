using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Resonate.Windows.Interop;

// The parts of the Windows Core Audio API that set one app's volume in the
// mixer. Source-generated COM (no runtime marshalling), so it works with
// Native AOT. Each interface lists its methods in vtable order up to the last
// one Resonate calls; earlier slots that are never called use plain pointers.

internal static class CoreAudioIds
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public const int ERender = 0;
    public const int EMultimedia = 1;
    public const uint ClsCtxAll = 0x17;
    public const int AudioSessionStateActive = 1;
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);

    IMMDevice GetDefaultAudioEndpoint(int dataFlow, int role);

    void GetDevice(nint id, out nint device);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    IAudioSessionManager2 Activate(in Guid iid, uint clsCtx, nint activationParams);

    IPropertyStore OpenPropertyStore(uint access);
}

[GeneratedComInterface]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    // IAudioSessionManager
    void GetAudioSessionControl(nint audioSessionGuid, uint streamFlags, out nint sessionControl);

    void GetSimpleAudioVolume(nint audioSessionGuid, uint streamFlags, out nint audioVolume);

    // IAudioSessionManager2
    IAudioSessionEnumerator GetSessionEnumerator();
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    int GetCount();

    IAudioSessionControl2 GetSession(int sessionCount);
}

[GeneratedComInterface]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2
{
    // IAudioSessionControl
    int GetState();

    void GetDisplayName(out nint displayName);

    void SetDisplayName(nint value, in Guid eventContext);

    void GetIconPath(out nint iconPath);

    void SetIconPath(nint value, in Guid eventContext);

    void GetGroupingParam(out Guid groupingParam);

    void SetGroupingParam(in Guid groupingParam, in Guid eventContext);

    void RegisterAudioSessionNotification(nint newNotifications);

    void UnregisterAudioSessionNotification(nint newNotifications);

    // IAudioSessionControl2
    void GetSessionIdentifier(out nint sessionIdentifier);

    void GetSessionInstanceIdentifier(out nint sessionInstanceIdentifier);

    /// <summary>Returns a success code other than S_OK for sessions shared by several processes.</summary>
    [PreserveSig]
    int GetProcessId(out uint processId);
}

[GeneratedComInterface]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal partial interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, in Guid eventContext);

    float GetMasterVolume();
}

internal static partial class Ole32
{
    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);
}

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Resonate.Spotify.Playback;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Spotify's volume in the Windows volume mixer (per-app volume). Changing it
/// is a local call into the Windows audio service, so it is instant. Spotify
/// only has an audio session once it has played sound since starting.
/// </summary>
public sealed class SpotifyMixerVolume : IAppVolume
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private static readonly Guid NoEventContext = Guid.Empty;

    public double? TryGetVolume()
    {
        try
        {
            var volumes = FindSpotifyVolumes();
            return volumes.Count == 0 ? null : volumes[0].GetMasterVolume();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    }

    public bool TrySetVolume(double volume)
    {
        try
        {
            var volumes = FindSpotifyVolumes();
            foreach (var session in volumes)
            {
                session.SetMasterVolume((float)Math.Clamp(volume, 0, 1), NoEventContext);
            }

            return volumes.Count > 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>Spotify's sessions on the default output, active ones first.</summary>
    /// <remarks>
    /// Each session names its process, and only those few are asked which
    /// program they run: listing every process on the PC (as
    /// <c>Process.GetProcessesByName</c> does) read the whole process and
    /// thread table every time the player looked at the volume.
    /// </remarks>
    private static List<ISimpleAudioVolume> FindSpotifyVolumes()
    {
        Marshal.ThrowExceptionForHR(Ole32.CoCreateInstance(
            CoreAudioIds.MMDeviceEnumerator, 0, CoreAudioIds.ClsCtxAll, CoreAudioIds.IMMDeviceEnumerator, out var pointer));
        IMMDeviceEnumerator enumerator;
        try
        {
            enumerator = (IMMDeviceEnumerator)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }

        var device = enumerator.GetDefaultAudioEndpoint(CoreAudioIds.ERender, CoreAudioIds.EMultimedia);
        var manager = device.Activate(CoreAudioIds.IAudioSessionManager2, CoreAudioIds.ClsCtxAll, 0);
        var sessions = manager.GetSessionEnumerator();
        var count = sessions.GetCount();
        List<ISimpleAudioVolume>? active = null;
        List<ISimpleAudioVolume>? inactive = null;
        for (var i = 0; i < count; i++)
        {
            var control = sessions.GetSession(i);
            if (control.GetProcessId(out var processId) < 0 || processId == 0 || !ProcessImage.IsSpotify(processId))
            {
                continue;
            }

            if (control is ISimpleAudioVolume volume)
            {
                if (control.GetState() == CoreAudioIds.AudioSessionStateActive)
                {
                    (active ??= []).Add(volume);
                }
                else
                {
                    (inactive ??= []).Add(volume);
                }
            }
        }

        if (active is null)
        {
            return inactive ?? [];
        }

        if (inactive is not null)
        {
            active.AddRange(inactive);
        }

        return active;
    }
}

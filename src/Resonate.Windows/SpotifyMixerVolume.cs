using System.Diagnostics;
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
    private static List<ISimpleAudioVolume> FindSpotifyVolumes()
    {
        var spotifyProcesses = SpotifyProcessIds();
        var result = new List<(ISimpleAudioVolume Volume, bool Active)>();
        if (spotifyProcesses.Count == 0)
        {
            return [];
        }

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
        for (var i = 0; i < count; i++)
        {
            var control = sessions.GetSession(i);
            if (control.GetProcessId(out var processId) < 0 || !spotifyProcesses.Contains(processId))
            {
                continue;
            }

            if (control is ISimpleAudioVolume volume)
            {
                result.Add((volume, control.GetState() == CoreAudioIds.AudioSessionStateActive));
            }
        }

        return result.OrderByDescending(r => r.Active).Select(r => r.Volume).ToList();
    }

    private static HashSet<uint> SpotifyProcessIds()
    {
        var processes = Process.GetProcessesByName("Spotify");
        try
        {
            return processes.Select(p => (uint)p.Id).ToHashSet();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

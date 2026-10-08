using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Finds the Spotify Connect device that is the Spotify app on this
/// computer, so songs started through the Web API play here and nowhere else.
/// The desktop app names its device after the computer.
/// </summary>
public sealed class LocalDeviceResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly ISpotifyWebApi _api;
    private readonly string _machineName;
    private readonly TimeProvider _time;
    private string? _deviceId;
    private DateTimeOffset _resolvedAt;

    public LocalDeviceResolver(ISpotifyWebApi api, string machineName, TimeProvider? time = null)
    {
        _api = api;
        _machineName = machineName;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The device ID, or null when Spotify on this computer is not online.</summary>
    public async Task<string?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_deviceId is not null && _time.GetUtcNow() - _resolvedAt < CacheLifetime)
        {
            return _deviceId;
        }

        var devices = await _api.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        _deviceId = Pick(devices, _machineName)?.Id;
        _resolvedAt = _time.GetUtcNow();
        return _deviceId;
    }

    public void Invalidate() => _deviceId = null;

    public static Device? Pick(IReadOnlyList<Device> devices, string machineName)
    {
        // Every copy of Resonate lists its own player as "Resonate" (Spotify's
        // web player, see OwnPlayer): another PC's, or one closing down, is
        // never taken for the Spotify app on this computer.
        var computers = devices
            .Where(d => d.Id is not null && !d.IsRestricted && string.Equals(d.Type, "Computer", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(d.Name, OwnPlayer.DefaultName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var named = computers
            .Where(d => NameMatches(d.Name, machineName))
            .OrderByDescending(d => d.IsActive)
            .FirstOrDefault();
        if (named is not null)
        {
            return named;
        }

        // A single computer is almost certainly this one. With several and no
        // name match, guessing could start music on someone else's computer.
        return computers.Count == 1 ? computers[0] : null;
    }

    internal static bool NameMatches(string deviceName, string machineName)
    {
        if (string.Equals(deviceName, machineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Windows' NetBIOS name is cut to 15 characters; the device name may not be.
        const int NetBiosLength = 15;
        return machineName.Length == NetBiosLength
            && deviceName.Length > NetBiosLength
            && deviceName.StartsWith(machineName, StringComparison.OrdinalIgnoreCase);
    }
}

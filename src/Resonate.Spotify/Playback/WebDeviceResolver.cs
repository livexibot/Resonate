using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Picks the Spotify Connect device that plays with "Spotify Web API only",
/// from the devices Spotify lists (Resonate closes the Spotify app on this
/// computer in that mode and never starts it): the one already playing, else
/// Resonate's own player on this computer (<see cref="OwnPlayer"/>, waiting
/// a little while it is still starting), as Spotify's own apps play where
/// play was pressed, else the one the user picked last, else the Spotify
/// app on this computer if the user opened it again, else the only device
/// there is. With several unknown devices it does not guess, so music never
/// starts on someone else's speaker.
/// </summary>
public sealed class WebDeviceResolver
{
    /// <summary>How long a play command waits for Resonate's own player while it is still starting.</summary>
    internal static readonly TimeSpan OwnDeviceWait = TimeSpan.FromSeconds(12);

    private readonly ISpotifyWebApi _api;
    private readonly string _machineName;
    private readonly IOwnDevice? _ownDevice;

    public WebDeviceResolver(ISpotifyWebApi api, string machineName, IOwnDevice? ownDevice = null)
    {
        _api = api;
        _machineName = machineName;
        _ownDevice = ownDevice;
    }

    /// <summary>The device the user picked last, by name (Spotify can give a device a new ID).</summary>
    public string? PreferredName { get; set; }

    /// <summary>The device to play on, or null when none is online (or none can be chosen safely).</summary>
    public async Task<Device?> ChooseAsync(CancellationToken cancellationToken)
    {
        var own = _ownDevice is null
            ? null
            : await _ownDevice.WaitForDeviceAsync(OwnDeviceWait, cancellationToken).ConfigureAwait(false);
        var devices = await _api.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        return Pick(devices, PreferredName, _machineName, own, _ownDevice?.Name);
    }

    /// <param name="ownDeviceId">Resonate's own player's device, when it is ready.</param>
    /// <param name="ownName">Its name; it is used even before Spotify lists it.</param>
    public static Device? Pick(
        IReadOnlyList<Device> devices,
        string? preferredName,
        string machineName,
        string? ownDeviceId = null,
        string? ownName = null)
    {
        var usable = devices.Where(d => d.Id is not null && !d.IsRestricted).ToList();
        var own = ownDeviceId is null
            ? null
            : usable.FirstOrDefault(d => d.Id == ownDeviceId)

                // Spotify can take a moment to list a device that just connected.
                ?? new Device { Id = ownDeviceId, Name = ownName ?? OwnPlayer.DefaultName, Type = "Computer" };
        var others = usable.Where(d => d.Id != ownDeviceId).ToList();
        return usable.FirstOrDefault(d => d.IsActive)
            ?? own
            ?? others.FirstOrDefault(d => preferredName is not null && string.Equals(d.Name, preferredName, StringComparison.OrdinalIgnoreCase))
            ?? LocalDeviceResolver.Pick(others, machineName)
            ?? (others.Count == 1 ? others[0] : null);
    }
}

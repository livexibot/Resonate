using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Picks the Spotify Connect device that plays with "Spotify Web API only",
/// from the devices Spotify lists (Resonate closes the Spotify app on this
/// computer in that mode and never starts it): the one already playing, else
/// the one the user picked last, else the Spotify app on this computer if the
/// user opened it again, else the only device there is. With several unknown devices it does not guess, so music
/// never starts on someone else's speaker.
/// </summary>
public sealed class WebDeviceResolver
{
    private readonly ISpotifyWebApi _api;
    private readonly string _machineName;

    public WebDeviceResolver(ISpotifyWebApi api, string machineName)
    {
        _api = api;
        _machineName = machineName;
    }

    /// <summary>The device the user picked last, by name (Spotify can give a device a new ID).</summary>
    public string? PreferredName { get; set; }

    /// <summary>The device to play on, or null when none is online (or none can be chosen safely).</summary>
    public async Task<Device?> ChooseAsync(CancellationToken cancellationToken) =>
        Pick(await _api.GetDevicesAsync(cancellationToken).ConfigureAwait(false), PreferredName, _machineName);

    public static Device? Pick(IReadOnlyList<Device> devices, string? preferredName, string machineName)
    {
        var usable = devices.Where(d => d.Id is not null && !d.IsRestricted).ToList();
        return usable.FirstOrDefault(d => d.IsActive)
            ?? usable.FirstOrDefault(d => preferredName is not null && string.Equals(d.Name, preferredName, StringComparison.OrdinalIgnoreCase))
            ?? LocalDeviceResolver.Pick(usable, machineName)
            ?? (usable.Count == 1 ? usable[0] : null);
    }
}

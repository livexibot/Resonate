using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Controls;

/// <summary>
/// The devices button: where the user picks the Spotify Connect device that
/// plays (this PC's Spotify app or Resonate's own player, a phone, a
/// speaker, the web player), as in Spotify's own apps. Shown whenever a
/// Spotify song plays, in either mode.
/// </summary>
public sealed partial class PlayerBar
{
    private const string WebPlayer = "https://open.spotify.com/";

    private bool _listingDevices;

    private void ShowDevice(PlayerState state)
    {
        // In every mode (the owner's request, 9 October 2026): this PC or any other Spotify Connect device.
        var shown = state.Source == PlaybackSource.Spotify;
        DeviceButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown)
        {
            ToolTipService.SetToolTip(DeviceButton, state.DeviceName is { Length: > 0 } name ? $"Playing on {name}" : "Play on a device");
        }
    }

    private async void OnDeviceClick(object sender, RoutedEventArgs e)
    {
        if (_listingDevices)
        {
            return;
        }

        _listingDevices = true;
        IReadOnlyList<Device> devices;
        try
        {
            var api = App.Services.Api;
            devices = await Task.Run(() => api.GetDevicesAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            return;
        }
        finally
        {
            _listingDevices = false;
        }

        // Built when opened, so it shows the devices online now.
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        menu.Items.Add(new MenuFlyoutItem { Text = devices.Count == 0 ? "No Spotify device is online" : "Play on", IsEnabled = false });
        foreach (var device in devices.Where(d => d.Id is not null))
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = device.Name,
                IsChecked = device.IsActive,
                IsEnabled = !device.IsRestricted,
                Icon = new FontIcon { Glyph = GlyphFor(device.Type) },
            };
            var (id, name) = (device.Id!, device.Name);
            item.Click += (_, _) => _ = App.Services.PlayOnDeviceAsync(id, name);
            menu.Items.Add(item);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var web = new MenuFlyoutItem { Text = "Open Spotify's web player", Icon = new FontIcon { Glyph = "" } };
        web.Click += (_, _) => _ = global::Windows.System.Launcher.LaunchUriAsync(new Uri(WebPlayer));
        menu.Items.Add(web);
        menu.ShowAt(DeviceButton);
    }

    /// <summary>A computer, phone, speaker or TV, as Spotify names the device's type.</summary>
    private static string GlyphFor(string type) => type.ToUpperInvariant() switch
    {
        "COMPUTER" => "",
        "SMARTPHONE" => "",
        "TABLET" => "",
        "SPEAKER" or "AVR" or "STB" or "AUDIODONGLE" => "",
        "TV" or "CASTVIDEO" => "",
        "CASTAUDIO" => "",
        _ => "",
    };
}

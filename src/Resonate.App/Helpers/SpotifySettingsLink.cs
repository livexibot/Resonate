using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Launcher = Windows.System.Launcher;

namespace Resonate.App.Helpers;

/// <summary>
/// Opens the Spotify app on its settings page (for Lossless, normalisation
/// or its own equalizer), as the equalizer and Signal path offer. Never with
/// "Spotify Web API only", which leaves the Spotify app alone.
/// </summary>
internal static class SpotifySettingsLink
{
    public static async Task OpenAsync()
    {
        var services = App.Services;
        if (!services.UsesSpotifyApp)
        {
            return;
        }

        if (services.IsDemo)
        {
            App.MainWindow?.ShowMessage("Demo mode: there is no Spotify app to open.", InfoBarSeverity.Informational);
            return;
        }

        var running = await Task.Run(() => services.Launcher.IsRunning);
        if (running)
        {
            // Resonate keeps Spotify's window hidden; bring it to the front first.
            services.SpotifyWindow.ShowSpotify();
        }

        bool opened;
        try
        {
            opened = await Launcher.LaunchUriAsync(new Uri("spotify:preferences"));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException)
        {
            opened = false;
        }

        if (!opened && !running && !services.SpotifyWindow.ShowSpotify())
        {
            App.MainWindow?.ShowMessage(
                "The Spotify app could not be opened. Install it from spotify.com/download or the Microsoft Store, sign in, then come back.",
                InfoBarSeverity.Error);
        }
    }
}

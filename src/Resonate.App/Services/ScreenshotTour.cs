using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Controls;
using Resonate.App.Demo;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Resonate.App.Services;

/// <summary>
/// For "--screenshots": walks through the main pages with demo data, saves a
/// picture of each, then quits. CI uses it so every pull request shows what
/// changed on screen. Any error on the way is written to
/// <see cref="ErrorFile"/> in the same folder, which fails the CI run.
/// </summary>
internal sealed class ScreenshotTour
{
    private const string ErrorFile = "tour-errors.txt";

    private readonly MainWindow _window;
    private readonly FrameworkElement _root;
    private readonly string _folder;

    public ScreenshotTour(MainWindow window, FrameworkElement root, string folder)
    {
        _window = window;
        _root = root;
        _folder = folder;
    }

    public async Task RunAsync()
    {
        var theme = App.Services.Theme;

        // An error in an event handler would otherwise end the app silently
        // and leave the later screenshots missing. Record it and carry on.
        Application.Current.UnhandledException += OnUnhandledException;
        try
        {
            Directory.CreateDirectory(_folder);

            // Resonate opens on Home.
            await Task.Delay(2500);
            await CaptureAsync("0-home.png");

            // Spotify's own top lists over a year, picked as a click would.
            if (_window.CurrentPage is HomePage home)
            {
                home.PickTopRange(TopRange.LongTerm);
                await Task.Delay(800);
                await CaptureAsync("0-home-top-year.png");
                home.PickTopRange(TopRange.ShortTerm);
            }

            _window.Open(DailyMixSource.KeyFor(1));
            await Task.Delay(1500);
            await CaptureAsync("0-home-mix.png");

            _window.Open(MainWindow.LikedSongsKey);
            await Task.Delay(1500);
            await CaptureAsync("1-liked-songs.png");

            _window.Open(MainWindow.LocalFilesKey);
            await Task.Delay(1500);
            await CaptureAsync("1b-local-files.png");

            _window.OpenPlaylist("late-night");
            await Task.Delay(1500);
            await CaptureAsync("2-playlist.png");

            _window.ToggleQueue();
            await Task.Delay(1200);
            await CaptureAsync("2-queue.png");
            _window.ToggleQueue();

            SearchPage.PendingQuery = "mid";
            _window.OpenSearch();
            await Task.Delay(2000);
            await CaptureAsync("3-search.png");

            // Releases come ten at a time, as Spotify allows.
            _window.Open(TrackActions.ArtistKey(DemoCatalog.ArtistId("Mira Sol")));
            await Task.Delay(1500);
            await CaptureAsync("3b-artist.png");

            ThemeStudio.CustomizeOpen = true;
            _window.OpenSettings();
            await Task.Delay(1500);
            await CaptureAsync("4-settings-look.png");

            // Further down the same page (demo values; there are no Spotify settings to find in CI).
            var settings = _window.CurrentPage as SettingsPage;
            settings?.ShowSection(SettingsSection.Equalizer);
            await Task.Delay(800);
            await CaptureAsync("4b-equalizer.png");

            if (settings is not null)
            {
                settings.ShowCustomize();
                await Task.Delay(600);
                await CaptureAsync("5-customize.png");

                // The plugins, with the sleep timer turned on (demo mode shows its settings without downloading it).
                var plugins = App.Services.Plugins;
                if (plugins.Available.FirstOrDefault(p => p.Id == "sleep-timer") is { } sleepTimer)
                {
                    await plugins.EnableAsync(sleepTimer.Id, CancellationToken.None);
                }

                settings.ShowPlugins();
                await Task.Delay(800);
                await CaptureAsync("6-plugins.png");
            }

            // Every preset, switched at run time, so the live switching of shapes and fonts is checked too.
            var number = 7;
            foreach (var preset in ThemePresets.All.Where(p => p != ThemePresets.Default))
            {
                theme.Select(preset.Id, transition: ThemeTransitionKind.None);
                _window.OpenPlaylist("focus");
                await Task.Delay(1500);
                await CaptureAsync($"{number++}-theme-{preset.Id}.png");
            }

            number = await CoverEffectsAsync(number);
            number = await ClassicPlayerAsync(number);

            theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
            _window.ShowSignIn();
            await Task.Delay(1200);
            await CaptureAsync($"{number}-sign-in.png");
        }
        catch (Exception ex)
        {
            Record(ex.ToString());
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    /// <summary>
    /// The two cover switches: a spinning cover (off unless the user turns it
    /// on), the blurred cover behind Liquid Glass (on unless they turn it
    /// off), and the colour wash Liquid Glass shows without it. Both are as
    /// they were afterwards.
    /// </summary>
    private async Task<int> CoverEffectsAsync(int number)
    {
        var theme = App.Services.Theme;
        theme.SpinningCover = true;
        theme.Select(ThemePresets.Synthwave.Id, transition: ThemeTransitionKind.None);
        _window.OpenPlaylist("focus");
        await Task.Delay(1300);
        await CaptureAsync($"{number++}-cover-spin.png");
        theme.SpinningCover = false;

        theme.BlurredCoverBackground = true;
        theme.Select(ThemePresets.Glass.Id, transition: ThemeTransitionKind.None);
        await Task.Delay(1500);
        await CaptureAsync($"{number++}-cover-backdrop.png");

        theme.BlurredCoverBackground = false;
        await Task.Delay(1300);
        await CaptureAsync($"{number++}-cover-wash.png");
        theme.BlurredCoverBackground = true;
        return number;
    }

    /// <summary>
    /// The classic player with the built-in skin, while a made-up music file
    /// plays so the visualiser moves; then double size, shade mode and its
    /// part of Settings. Afterwards everything is as before: the player bar,
    /// normal size, the default look and a Spotify song.
    /// </summary>
    private async Task<int> ClassicPlayerAsync(int number)
    {
        var services = App.Services;
        var skins = services.Skins;
        services.Theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
        await services.Player.PlayAsync(new PlayRequest(DemoLocalFiles.Tracks(DateTimeOffset.UtcNow), 0, null, "Local Files"));
        _window.Open(MainWindow.LocalFilesKey);
        skins.UsesClassicPlayer = true;
        await Task.Delay(1500);
        await CaptureAsync($"{number++}-classic-player.png");

        skins.DoubleSize = true;
        await Task.Delay(1200);
        await CaptureAsync($"{number++}-classic-double.png");
        if (!_window.ClassicPlayerShowsDoubleSize)
        {
            // Double size gives way in narrow windows; the tour's window must still have room for it.
            Record("The classic player did not show at double size in the screenshot tour's window.");
        }

        skins.DoubleSize = false;

        skins.Shaded = true;
        await Task.Delay(1200);
        await CaptureAsync($"{number++}-classic-shade.png");
        skins.Shaded = false;

        _window.OpenSettings(SettingsSection.ClassicPlayer);
        await Task.Delay(1400);
        await CaptureAsync($"{number++}-settings-classic.png");

        skins.UsesClassicPlayer = false;
        await services.Player.PlayContextAsync("demo:playlist:late-night");
        return number;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Record($"{e.Message}{Environment.NewLine}{e.Exception}");
    }

    private void Record(string error)
    {
        Directory.CreateDirectory(_folder);
        File.AppendAllText(Path.Combine(_folder, ErrorFile), error + Environment.NewLine + Environment.NewLine);
    }

    private async Task CaptureAsync(string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(_root);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = 96 * _root.XamlRoot.RasterizationScale;
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            dpi,
            dpi,
            pixels.ToArray());
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var file = File.Create(Path.Combine(_folder, name));
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}

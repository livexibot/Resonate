using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Controls;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
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

            ThemeStudio.CustomizeOpen = true;
            _window.OpenSettings();
            await Task.Delay(1500);
            await CaptureAsync("4-settings-look.png");

            // Further down the same page (demo values; there are no Spotify settings to find in CI).
            FindDescendant<EqualizerPanel>(_root)?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.1 });
            await Task.Delay(800);
            await CaptureAsync("4b-equalizer.png");

            if (_window.CurrentPage is SettingsPage settings)
            {
                settings.ShowCustomize();
                await Task.Delay(600);
                await CaptureAsync("5-customize.png");
            }

            // Every preset, switched at run time, so the live switching of shapes and fonts is checked too.
            var number = 6;
            foreach (var preset in ThemePresets.All.Where(p => p != ThemePresets.Default))
            {
                theme.Select(preset.Id, transition: ThemeTransitionKind.None);
                _window.OpenPlaylist("focus");
                await Task.Delay(1500);
                await CaptureAsync($"{number++}-theme-{preset.Id}.png");
            }

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

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as T ?? FindDescendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
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

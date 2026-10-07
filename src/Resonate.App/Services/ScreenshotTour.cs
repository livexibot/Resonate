using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Pages;
using Resonate.App.Themes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Resonate.App.Services;

/// <summary>
/// For "--screenshots": walks through the main pages with demo data, saves a
/// picture of each, then quits. CI uses it so every pull request shows what
/// changed on screen.
/// </summary>
internal sealed class ScreenshotTour
{
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
        try
        {
            Directory.CreateDirectory(_folder);
            await Task.Delay(2000);
            await CaptureAsync("1-liked-songs");

            _window.OpenPlaylist("late-night");
            await Task.Delay(1500);
            await CaptureAsync("2-playlist");

            SearchPage.PendingQuery = "mid";
            _window.OpenSearch();
            await Task.Delay(2000);
            await CaptureAsync("3-search");

            App.Services.Theme.Apply(ThemePreset.Daylight);
            _window.ApplyCaptionButtonColors();
            _window.OpenPlaylist("focus");
            await Task.Delay(1500);
            await CaptureAsync("4-daylight-theme");

            App.Services.Theme.Apply(ThemePreset.Midnight);
            _window.ShowSignIn();
            await Task.Delay(1200);
            await CaptureAsync("5-sign-in");
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    private async Task CaptureAsync(string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(_root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var width = (uint)bitmap.PixelWidth;
        var height = (uint)bitmap.PixelHeight;
        var dpi = 96 * _root.XamlRoot.RasterizationScale;

        // Full size, for people.
        await SaveAsync(Path.Combine(_folder, name + ".png"), BitmapEncoder.PngEncoderId, null, pixels, width, height, dpi, 1);

        // A smaller JPEG that CI also prints into its log, for reviewers that
        // can read logs but not download artifacts.
        var quality = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(0.8f, PropertyType.Single) } };
        await SaveAsync(Path.Combine(_folder, name + ".preview.jpg"), BitmapEncoder.JpegEncoderId, quality, pixels, width, height, dpi, 0.75);
    }

    private static async Task SaveAsync(
        string path,
        Guid encoderId,
        BitmapPropertySet? options,
        byte[] pixels,
        uint width,
        uint height,
        double dpi,
        double scale)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = options is null
            ? await BitmapEncoder.CreateAsync(encoderId, stream)
            : await BitmapEncoder.CreateAsync(encoderId, stream, options);
        if (scale < 1)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)(width * scale);
            encoder.BitmapTransform.ScaledHeight = (uint)(height * scale);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, dpi, dpi, pixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}

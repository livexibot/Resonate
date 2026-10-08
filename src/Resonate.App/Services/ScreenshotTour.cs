using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Controls;
using Resonate.App.Demo;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.WebApi;
using Resonate.Themes;
using Resonate.Windows;
using Resonate.Windows.LocalAudio;
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
            await CheckCoversAsync();

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

            // Settings opens in a pane beside the page.
            ThemeStudio.CustomizeOpen = true;
            _window.OpenSettings();
            await Task.Delay(1500);
            await CaptureAsync("4-settings-look.png");

            // Further down the same page (demo values; there are no Spotify settings to find in CI).
            FindDescendant<EqualizerPanel>(_root)?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.1 });
            await Task.Delay(800);
            await CaptureAsync("4b-equalizer.png");

            if (_window.SettingsPage is { } settings)
            {
                settings.ShowCustomize();
                await Task.Delay(600);
                await CaptureAsync("5-customize.png");

                // The plugins, turned on (demo mode shows their settings without downloading them):
                // Skip rules' lists sit under their names, the sleep timer's controls beside them.
                var plugins = App.Services.Plugins;
                foreach (var id in (string[])["skip-rules", "sleep-timer"])
                {
                    if (plugins.Available.FirstOrDefault(p => p.Id == id) is { } plugin)
                    {
                        await plugins.EnableAsync(plugin.Id, CancellationToken.None);
                    }
                }

                settings.ShowPlugins();
                await Task.Delay(800);
                await CaptureAsync("6-plugins.png");

                // A new version downloading (made up: demo mode never downloads), in Settings, then in the page's corner.
                App.Services.Updates.Preview(new UpdateProgress("0.5.0", 14_900_000, 38_400_000, 3_600_000, Preparing: false));
                settings.ShowUpdates();
                await Task.Delay(800);
                await CaptureAsync("6b-update-settings.png");
                _window.CloseSettings();
                await Task.Delay(800);
                await CaptureAsync("6b-update-download.png");
                App.Services.Updates.Preview(null);
            }

            _window.CloseSettings();
            await CaptureBundledFontsAsync();

            // Every preset, switched at run time, so the live switching of shapes and fonts is checked too.
            var number = 7;
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

    /// <summary>
    /// Every font that comes with Resonate, in its regular and semibold
    /// weights, over the whole window. A font file that does not load falls
    /// back to Windows' own font, so its sample measures the same as the
    /// fallback's; that is recorded as an error.
    /// </summary>
    private async Task CaptureBundledFontsAsync()
    {
        if (_root is not ThemeHost host)
        {
            Record("The window's root is not the theme host, so the bundled fonts were not checked.");
            return;
        }

        const string Sample = "Resonate · Hamburgefonstiv 0123";
        var theme = App.Services.Theme;
        var sheet = new Grid
        {
            Background = theme.GetBrush("ResonateSurfaceBrush"),
            Padding = new Thickness(40, 56, 40, 40),
            ColumnSpacing = 40,
            RowSpacing = 14,
        };
        sheet.ColumnDefinitions.Add(new ColumnDefinition());
        sheet.ColumnDefinitions.Add(new ColumnDefinition());

        TextBlock Line(string? font, string text, bool semibold) => new()
        {
            Text = text,
            FontSize = 16,
            FontFamily = new FontFamily(font ?? "Resonate Missing Font"),
            FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = theme.GetBrush("ResonateTextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var fonts = BundledFonts.All;
        var samples = new List<(BundledFont Font, TextBlock Text)>();
        for (var i = 0; i <= fonts.Count; i++)
        {
            sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < fonts.Count; i++)
        {
            var font = fonts[i];
            var family = BundledFonts.Resolve(font.Name);
            var sample = Line(family, Sample, semibold: true);
            var cell = new StackPanel { Spacing = 2, Children = { Line(family, $"{font.Name} ({font.Kind})", semibold: false), sample } };
            Grid.SetRow(cell, i / 2);
            Grid.SetColumn(cell, i % 2);
            sheet.Children.Add(cell);
            samples.Add((font, sample));
        }

        // The same sample in a font that does not exist: Windows' fallback.
        var fallback = Line(null, Sample, semibold: true);
        fallback.Opacity = 0;
        Grid.SetRow(fallback, fonts.Count);
        sheet.Children.Add(fallback);

        host.Children.Add(sheet);
        try
        {
            await Task.Delay(1500);
            await CaptureAsync("6c-fonts.png");
            foreach (var (font, sample) in samples)
            {
                if (Math.Abs(sample.ActualWidth - fallback.ActualWidth) < 0.5)
                {
                    Record($"The bundled font {font.Name} did not load: its text measures {sample.ActualWidth:0.#} px, like Windows' fallback font.");
                }
            }
        }
        finally
        {
            host.Children.Remove(sheet);
        }
    }

    /// <summary>
    /// Covers made from bytes (local files' own covers) go through Windows'
    /// image decoder, which only the installed app can show works: a
    /// picture next to a song is made into a row's thumbnail, shown, and
    /// read for the cover colours. Demo mode has no real files, so this is
    /// the only place CI sees it.
    /// </summary>
    private async Task CheckCoversAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "resonate-cover-check");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var picture = await MakePictureAsync(320, 200);
        await File.WriteAllBytesAsync(Path.Combine(folder, "cover.jpg"), picture);
        var song = Path.Combine(folder, "song.mp3");
        await File.WriteAllBytesAsync(song, new byte[4096]);
        var info = new FileInfo(song);

        using var cache = new LocalCoverCache(Path.Combine(folder, "thumbnails"), new WindowsCoverShrinker());
        var thumbnail = await cache.GetAsync(new LocalFile { Path = song, Size = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks, Title = "song" });
        if (thumbnail is null)
        {
            Record("A local song's cover could not be made into a thumbnail (LocalCoverCache with WindowsCoverShrinker).");
        }

        // A cover inside the file (an ID3 tag's picture), taller than wide.
        var embeddedFolder = Path.Combine(folder, "embedded");
        Directory.CreateDirectory(embeddedFolder);
        var embedded = Path.Combine(embeddedFolder, "song.mp3");
        await File.WriteAllBytesAsync(embedded, [.. Id3WithCover(await MakePictureAsync(200, 320)), .. new byte[4096]]);
        var embeddedInfo = new FileInfo(embedded);
        if (await cache.GetAsync(new LocalFile { Path = embedded, Size = embeddedInfo.Length, LastWriteTicks = embeddedInfo.LastWriteTimeUtc.Ticks, Title = "song" }) is null)
        {
            Record("The cover inside a local song could not be made into a thumbnail (LocalCoverCache with WindowsCoverShrinker).");
        }

        var (image, loaded) = await CoverImages.FromBytesAsync(thumbnail ?? picture, 40);
        if (!loaded || image is not BitmapImage { PixelWidth: > 0 })
        {
            Record("A cover made from bytes could not be shown (CoverImages.FromBytesAsync).");
        }

        if (await CoverDecoder.DecodeAsync(picture, 40, CancellationToken.None) is not { Length: 40 * 40 * 4 })
        {
            Record("A cover could not be read for its colours (CoverDecoder).");
        }
    }

    /// <summary>The start of an MP3 file: an ID3v2.3 tag with <paramref name="cover"/> as its front cover.</summary>
    private static byte[] Id3WithCover(byte[] cover)
    {
        byte[] picture = [0, .. "image/jpeg"u8, 0, 3, 0, .. cover];
        var frame = new byte[10 + picture.Length];
        "APIC"u8.CopyTo(frame);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), picture.Length);
        picture.CopyTo(frame, 10);

        // The tag's size is "synchsafe": seven bits a byte.
        var size = frame.Length;
        byte[] header = [.. "ID3"u8, 3, 0, 0, (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)];
        return [.. header, .. frame];
    }

    /// <summary>A JPEG of a simple gradient, made with Windows' encoder.</summary>
    private static async Task<byte[]> MakePictureAsync(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                pixels[i] = (byte)(x * 255 / width);
                pixels[i + 1] = (byte)(y * 255 / height);
                pixels[i + 2] = 160;
                pixels[i + 3] = 255;
            }
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return await ImageStreams.ToBytesAsync(stream);
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

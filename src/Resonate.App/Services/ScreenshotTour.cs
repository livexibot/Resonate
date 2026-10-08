using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Controls;
using Resonate.App.Demo;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
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

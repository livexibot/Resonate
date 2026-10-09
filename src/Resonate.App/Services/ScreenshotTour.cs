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
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes;
using Resonate.Windows;
using Resonate.Windows.LocalAudio;
using Windows.Foundation;
using Windows.Graphics;
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

                // The mix covers, and the row of songs played lately.
                home.ScrollTo(HomeSection.Mixes);
                await Task.Delay(800);
                await CaptureAsync("0-home-mixes.png");
                home.ScrollTo(HomeSection.Recent);
                await Task.Delay(800);
                await CaptureAsync("0-home-recent.png");
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

            // Lyrics, with the demo's made-up words following the song.
            _window.ToggleLyrics();
            await Task.Delay(1500);
            await CaptureAsync("2b-lyrics.png");
            _window.ToggleLyrics();

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

            // The Playback tab, at the equalizer (demo values; there are no Spotify settings to find in CI).
            var settings = _window.SettingsPage;
            settings?.ShowSection(SettingsSection.Equalizer);
            await Task.Delay(800);
            await CaptureAsync("4b-equalizer.png");

            if (settings is not null)
            {
                // The Layout tab, with Search and DJ hidden from the sidebar for a moment.
                var hidden = App.Services.Settings.HiddenSidebarLinks;
                List<string> before = [.. hidden];
                hidden.AddRange([MainWindow.SearchKey, MainWindow.DjKey]);
                _window.ShowSidebarLinks();
                settings.ShowTab(SettingsTab.Layout);
                await Task.Delay(800);
                await CaptureAsync("4c-layout.png");
                hidden.Clear();
                hidden.AddRange(before);
                _window.ShowSidebarLinks();

                settings.ShowTab(SettingsTab.Player);
                await Task.Delay(800);
                await CaptureAsync("4c2-player.png");

                settings.ShowTab(SettingsTab.About);
                await Task.Delay(600);
                await CaptureAsync("4d-about.png");

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

            // Signal path's pill in the player bar (a made-up Lossless verdict in demo mode); it stays on for the looks below.
            App.Services.BuiltIns.Set(BuiltInPlugins.SignalPath, true);
            await Task.Delay(800);
            await CaptureAsync("6c-signal-path.png");

            await BuiltInPluginsAsync();

            await CaptureBundledFontsAsync();

            // Every preset, switched at run time, so the live switching of
            // shapes and fonts is checked too, and each player position.
            var number = 7;
            foreach (var preset in ThemePresets.All.Where(p => p != ThemePresets.Default))
            {
                theme.Select(preset.Id, transition: ThemeTransitionKind.None);
                _window.OpenPlaylist("focus");
                await Task.Delay(1500);
                await CaptureAsync($"{number++}-theme-{preset.Id}.png");

                // A hovering player is checked against the page's last row.
                if (_window.PlayerHovers)
                {
                    await ScrollToEndAsync();
                }

                CheckPlayerPlacement();
            }

            number = await CoverEffectsAsync(number);
            number = await LayoutsAsync(number);
            number = await ClassicPlayerAsync(number);
            number = await SwitchingAsync(number);

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
    /// The other built-in plugins, turned on in turn so CI draws each of them
    /// once (Native AOT faults show up only at run time), then off again.
    /// Window shapes is left out: it would resize the window for the
    /// screenshots after it.
    /// </summary>
    private async Task BuiltInPluginsAsync()
    {
        var builtIns = App.Services.BuiltIns;
        string[] ids =
        [
            BuiltInPlugins.HomeStage, BuiltInPlugins.Rediscover, BuiltInPlugins.ArtistOrbit,
            BuiltInPlugins.UpNext, BuiltInPlugins.SmartPlaylists, BuiltInPlugins.AwayScreen, BuiltInPlugins.SummonBar,
        ];
        foreach (var id in ids)
        {
            builtIns.Set(id, true);
        }

        _window.Open(MainWindow.HomeKey);
        await Task.Delay(2000);
        await CaptureAsync("6d-home-stage.png");

        // Pictures cannot tell a cloud mask that never loaded, or bars whose motion the compositor refused.
        var mask = CloudField.CheckMaskAsync();
        if (await Task.WhenAny(mask, Task.Delay(10_000)) != mask)
        {
            Record("The Home stage's cloud mask did not load within 10 seconds.");
        }
        else if (await mask is { } maskError)
        {
            Record("The Home stage's cloud mask did not load: " + maskError);
        }

        if (StageVisualizer.CheckMotion(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_root).Compositor) is { } motionError)
        {
            Record("The Home stage's visualizer could not move: " + motionError);
        }

        if (SceneWeatherLayer.CheckMotion(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_root).Compositor) is { } weatherError)
        {
            Record("The special looks' weather could not move: " + weatherError);
        }

        if (_window.CurrentPage is HomePage home)
        {
            home.ScrollTo(HomeSection.Mixes);
            await Task.Delay(800);
            await CaptureAsync("6d-home-rediscover.png");
        }

        _window.Open(TrackActions.ArtistKey(DemoCatalog.ArtistId("Mira Sol")));
        await Task.Delay(1500);
        if (_window.CurrentPage is ArtistPage artist)
        {
            artist.ShowOrbitForTour();
        }

        await Task.Delay(800);
        await CaptureAsync("6e-artist-orbit.png");

        _window.ToggleQueue();
        await Task.Delay(1200);
        await CaptureAsync("6f-up-next.png");
        _window.ToggleQueue();

        _window.Open("smart-new");
        await Task.Delay(1500);
        await CaptureAsync("6g-smart-playlist.png");

        _window.OpenSettings();
        _window.SettingsPage?.ShowPlugins();
        await Task.Delay(1000);
        await CaptureAsync("6h-built-in-plugins.png");
        _window.CloseSettings();

        foreach (var id in ids)
        {
            builtIns.Set(id, false);
        }

        await Task.Delay(500);
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
    /// The two cover switches: a spinning cover (off unless the user turns it
    /// on), the blurred cover behind Liquid Glass (on unless they turn it
    /// off), and the colour wash Liquid Glass shows without it. Both are as
    /// they were afterwards.
    /// </summary>
    private async Task<int> CoverEffectsAsync(int number)
    {
        var theme = App.Services.Theme;
        theme.Select(ThemePresets.Synthwave.Id, transition: ThemeTransitionKind.None);
        _window.OpenPlaylist("focus");
        await Task.Delay(1300);
        await CaptureAsync($"{number++}-cover-spin.png");

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
    /// The player hovering over a long playlist scrolled to its end (its last
    /// song must stay clear of the player), with the queue open, in a narrow
    /// window and at larger App and Text sizes; then the sidebar reaching the
    /// bottom beside a floating player. Afterwards the look, the sidebar
    /// switch, the sizes and the window's size are as they were.
    /// </summary>
    private async Task<int> LayoutsAsync(int number)
    {
        var theme = App.Services.Theme;
        theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
        theme.Edit(look => look with { PlayerLayout = PlayerLayout.Hovering });
        _window.OpenPlaylist("focus");
        await Task.Delay(1500);
        await ScrollToEndAsync();
        await CaptureAsync($"{number++}-layout-hovering.png");
        CheckPlayerPlacement();

        // Next to the queue the page is narrower: over the page only while it
        // has room for the player, else under the panels (as in CI's window).
        _window.ToggleQueue();
        await Task.Delay(1200);
        await ScrollToEndAsync();
        await CaptureAsync($"{number++}-layout-hovering-queue.png");
        CheckPlayerPlacement();
        _window.ToggleQueue();

        // A narrow window: the mini bar.
        var size = _window.AppWindow.Size;
        var scale = _root.XamlRoot.RasterizationScale;
        _window.AppWindow.Resize(new SizeInt32((int)Math.Round(840 * scale), size.Height));
        await Task.Delay(1500);
        await ScrollToEndAsync();
        await CaptureAsync($"{number++}-layout-narrow.png");
        CheckPlayerPlacement();

        // Settings beside so narrow a page leaves a hovering player too little
        // room over it, so it moves under the panels until Settings closes.
        _window.OpenSettings();
        await Task.Delay(1200);
        await CaptureAsync($"{number++}-layout-narrow-settings.png");
        CheckPlayerPlacement();
        if (_window.PlayerHovers)
        {
            Record("The player still hovered over a page too narrow for it (Settings open in an 840-wide window).");
        }

        _window.CloseSettings();
        await Task.Delay(800);
        if (!_window.PlayerHovers)
        {
            Record("The player did not hover over the page again once Settings closed.");
        }

        _window.AppWindow.Resize(size);
        await Task.Delay(800);

        // Larger App and Text sizes (no picture): the page still fills the
        // window and its last song stays clear of the hovering player.
        theme.AppSize = 150;
        theme.TextSize = 125;
        await Task.Delay(1500);
        await ScrollToEndAsync();
        CheckPlayerPlacement();
        if (_window.CheckContentFills() is { } unfilled)
        {
            Record(unfilled);
        }

        theme.AppSize = AppScale.Normal;
        theme.TextSize = AppScale.Normal;
        _window.AppWindow.Resize(size);

        theme.Select(ThemePresets.Daylight.Id, transition: ThemeTransitionKind.None);
        theme.Delete(ThemeLibrary.CustomId);
        theme.SidebarFullHeight = true;
        _window.OpenPlaylist("late-night");
        await Task.Delay(1500);
        await CaptureAsync($"{number++}-layout-full-height.png");

        theme.SidebarFullHeight = false;
        theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
        await Task.Delay(800);
        return number;
    }

    /// <summary>Twice: the list only knows its full length once its last rows are drawn.</summary>
    private async Task ScrollToEndAsync()
    {
        if (_window.CurrentPage is TracksPage tracks)
        {
            tracks.ScrollToEndForTour();
            await Task.Delay(400);
            tracks.ScrollToEndForTour();
        }

        await Task.Delay(800);
    }

    private void CheckPlayerPlacement()
    {
        if (_window.CheckPlayerPlacement() is { } problem)
        {
            Record(problem);
        }
    }

    /// <summary>
    /// The classic player with the built-in skin, while a made-up music file
    /// plays so the visualiser moves; then double size, shade mode, hovering
    /// over the page, and its part of Settings. Afterwards everything is as
    /// before: the player bar, normal size, the default look and a Spotify song.
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

        // Hovering over the page, it hugs the skin and sits in the middle.
        services.Theme.Edit(look => look with { PlayerLayout = PlayerLayout.Hovering });
        await Task.Delay(1200);
        await CaptureAsync($"{number++}-classic-hovering.png");
        services.Theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
        services.Theme.Delete(ThemeLibrary.CustomId);

        _window.OpenSettings(SettingsSection.ClassicPlayer);
        await Task.Delay(1400);
        await CaptureAsync($"{number++}-settings-classic.png");
        _window.CloseSettings();

        number = await MiniPlayerAsync(number);

        skins.UsesClassicPlayer = false;
        await services.Player.PlayContextAsync("demo:playlist:late-night");
        return number;
    }

    /// <summary>
    /// The mini player in its own window, with the equalizer and the playlist
    /// under the main window (a local song plays, so the visualiser moves),
    /// sized to the skin pixel; then everything rolled up, then double size,
    /// then back to the full window.
    /// </summary>
    private async Task<int> MiniPlayerAsync(int number)
    {
        var skins = App.Services.Skins;

        // Normal size first (it opens at double size by default), then double size below.
        skins.MiniSize = 1;
        skins.MiniEqualizer = true;
        skins.MiniPlaylist = true;
        _window.ShowMiniPlayer();
        await Task.Delay(1500);
        if (_window.MiniPlayerRoot is not { } root)
        {
            Record("The mini player did not open.");
            return number;
        }

        await CaptureAsync($"{number++}-mini-player.png", root);
        var scale = ClassicPlayer.PixelScale(1, root.XamlRoot.RasterizationScale);
        var expected = (Width: (116 + 275) * scale, Height: (116 + 116 + skins.MiniPlaylistHeight) * scale);
        if (_window.MiniPlayerClientSize is { } size && (size.Width, size.Height) != expected)
        {
            Record($"The mini player is {size.Width} x {size.Height} pixels, not {expected.Width} x {expected.Height}.");
        }

        skins.MiniShaded = true;
        skins.MiniEqualizerShaded = true;
        skins.MiniPlaylistShaded = true;
        await Task.Delay(1000);
        await CaptureAsync($"{number++}-mini-shade.png", root);
        skins.MiniShaded = false;
        skins.MiniEqualizerShaded = false;
        skins.MiniPlaylistShaded = false;

        skins.MiniSize = 2;
        await Task.Delay(1000);
        await CaptureAsync($"{number++}-mini-double.png", root);
        skins.MiniSize = 1;

        _window.LeaveMiniPlayer();
        await Task.Delay(1000);
        if (_window.IsMiniPlayerShown || !_window.AppWindow.IsVisible)
        {
            Record("Leaving the mini player did not bring the full window back.");
        }

        skins.MiniEqualizer = false;
        skins.MiniPlaylist = false;
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

    /// <summary>
    /// Switching looks with Spread from the middle, Ripple (from a fixed
    /// point) and Cross-fade, each held halfway so the picture shows both
    /// looks, then let finish. Windows' animations may be off on CI's
    /// machine, so the tour asks for them regardless. Afterwards the default
    /// look is back.
    /// </summary>
    private async Task<int> SwitchingAsync(int number)
    {
        var theme = App.Services.Theme;
        theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
        _window.OpenPlaylist("focus");
        await Task.Delay(1300);

        var origin = new Point(_root.ActualWidth * 0.3, _root.ActualHeight * 0.4);
        (ThemeTransitionKind Kind, string Name, Point? Origin)[] switches =
        [
            (ThemeTransitionKind.Grow, "grow", null),
            (ThemeTransitionKind.Ripple, "ripple", origin),
            (ThemeTransitionKind.Fade, "fade", null),
        ];

        theme.AnimateRegardless = true;
        try
        {
            foreach (var (kind, name, from) in switches)
            {
                // Back and forth between a dark look and a light one.
                var target = theme.Current.Id == ThemePresets.Daylight.Id ? ThemePresets.Default : ThemePresets.Daylight;
                theme.Select(target.Id, from, kind);
                var started = theme.TransitionStarted;
                var animation = theme.TransitionTask;
                await Task.WhenAny(started, Task.Delay(5000));
                if (!theme.FreezeTransition(0.5) || theme.TransitionShowing != kind)
                {
                    Record($"The {name} switching animation did not show (showing: {theme.TransitionShowing?.ToString() ?? "nothing"}; failure: {theme.TransitionFailure ?? "none"}).");
                }

                // Long enough for the new look's backdrop and layout to settle under the held animation.
                await Task.Delay(900);
                await CaptureAsync($"{number++}-switch-{name}.png");
                theme.ResumeTransition();
                await Task.WhenAny(animation, Task.Delay(5000));
                if (theme.TransitionShowing is not null)
                {
                    Record($"The {name} switching animation did not end.");
                }

                await Task.Delay(300);
            }
        }
        finally
        {
            theme.AnimateRegardless = false;
        }

        theme.Select(ThemePresets.Default.Id, transition: ThemeTransitionKind.None);
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

    private Task CaptureAsync(string name) => CaptureAsync(name, _root);

    private async Task CaptureAsync(string name, FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = 96 * element.XamlRoot.RasterizationScale;
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

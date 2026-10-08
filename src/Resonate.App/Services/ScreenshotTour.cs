using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Controls;
using Resonate.App.Demo;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
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

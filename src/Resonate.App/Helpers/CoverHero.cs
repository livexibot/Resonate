using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Spotify.Library;
using Resonate.Themes;
using Resonate.Windows;
using Windows.Foundation;

namespace Resonate.App.Helpers;

/// <summary>
/// The glow of colour at the top of a page with a cover (a playlist, an
/// album, an artist), fading to nothing towards what is below: the cover's
/// own colour, as faint as the look needs for every text over it to stay
/// readable (<see cref="ThemePalette.HeroTint"/>). The cover is read in the
/// background at 40 pixels and its colour remembered by address, so a page
/// seen before shows it at once; otherwise the glow fades in once it is
/// known. Without a picture, or when it cannot be read, the colour of the
/// cover's placeholder tile shows (also in demo mode, for CI's screenshots).
/// </summary>
public sealed class CoverHero
{
    // Tiny on purpose, like ArtworkSampler: reading the colour takes well under a millisecond.
    private const int SampleSize = 40;

    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(400);
    private static readonly CoverColourCache Colours = new(200);

    // Made on first use, so a run without covers (demo mode, CI) never opens a connection.
    private static readonly Lazy<HttpClient> Http = new(CreateHttp);

    private readonly Border _glow;
    private readonly GradientStop _top = new() { Offset = 0 };
    private readonly GradientStop _bottom = new() { Offset = 1 };
    private CancellationTokenSource? _reading;
    private ThemeColor? _colour;
    private string? _url;
    private bool _attached;

    /// <summary>Paints <paramref name="glow"/>, hidden until the colour is known.</summary>
    public CoverHero(Border glow)
    {
        _glow = glow;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(_top);
        brush.GradientStops.Add(_bottom);
        glow.Background = brush;
        glow.Opacity = 0;
    }

    /// <summary>
    /// Call when the page shows: the glow follows the look from then on.
    /// Only then is the look's service holding on to it.
    /// </summary>
    public void Attach()
    {
        if (!_attached)
        {
            _attached = true;
            App.Services.Theme.Changed += OnThemeChanged;
            Paint();
        }
    }

    /// <summary>Call when the page leaves: nothing holds on to it any more, and a cover still being read is forgotten.</summary>
    public void Detach()
    {
        if (_attached)
        {
            _attached = false;
            App.Services.Theme.Changed -= OnThemeChanged;
        }

        // A cover left half read is read again if the same page shows it later.
        if (_reading is not null)
        {
            _url = null;
        }

        CancelReading();
    }

    /// <summary>
    /// The page's cover, at <paramref name="url"/> (null when it has none);
    /// <paramref name="placeholder"/> is the colour of its placeholder tile,
    /// which stands in when there is no picture or it cannot be read.
    /// </summary>
    public void Show(ThemeColor placeholder, string? url)
    {
        // The same cover again (the stored header, then the fresh one): nothing to do.
        if (url is not null && url == _url)
        {
            return;
        }

        _url = url;
        CancelReading();
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http"))
        {
            Reveal(placeholder, fade: false);
            return;
        }

        if (Colours.TryGet(url, out var known))
        {
            Reveal(known, fade: false);
            return;
        }

        _reading = new CancellationTokenSource();
        _ = ReadAsync(address, url, placeholder, _reading.Token);
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Resonate/" + AppInfo.Version);
        return http;
    }

    private static async Task<ThemeColor?> ColourOfAsync(CoverStore? store, Uri address, CancellationToken token)
    {
        // The same cover is usually on screen already, so the cover store has it.
        var image = store is not null && CoverStore.Handles(address.OriginalString)
            ? await store.GetAsync(address.OriginalString).WaitAsync(token).ConfigureAwait(false)
            : await Http.Value.GetByteArrayAsync(address, token).ConfigureAwait(false);
        if (image is null)
        {
            return null;
        }

        var pixels = await CoverDecoder.DecodeAsync(image, SampleSize, token).ConfigureAwait(false);
        return pixels is null ? null : CoverColourCache.HeaderColour(pixels, SampleSize, SampleSize);
    }

    private async Task ReadAsync(Uri address, string url, ThemeColor placeholder, CancellationToken token)
    {
        ThemeColor? colour = null;
        try
        {
            // Downloading and decoding never touch the interface thread.
            var store = App.Services.Covers.Store;
            colour = await Task.Run(() => ColourOfAsync(store, address, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Offline, slow or not a picture: the tile's colour stands in.
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (colour is { } read)
        {
            Colours.Remember(url, read);
        }

        Reveal(colour ?? placeholder, fade: true);
    }

    private void CancelReading()
    {
        _reading?.Cancel();
        _reading?.Dispose();
        _reading = null;
    }

    private void Reveal(ThemeColor colour, bool fade)
    {
        _colour = colour;
        Paint();
        if (_glow.Opacity < 1)
        {
            // A colour that arrives late fades in; one known at once just shows.
            _glow.OpacityTransition = fade && App.Services.Theme.AnimationsEnabled ? new ScalarTransition { Duration = FadeIn } : null;
            _glow.Opacity = 1;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Paint();

    private void Paint()
    {
        if (_colour is not { } colour)
        {
            return;
        }

        var tint = App.Services.Theme.Palette.HeroTint(colour);
        _top.Color = tint.ToColor();
        _bottom.Color = tint.WithAlpha(0).ToColor();
    }
}

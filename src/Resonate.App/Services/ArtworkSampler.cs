using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.App.Themes;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Services;

/// <summary>
/// Reads the playing song's cover for looks that use it: the picture behind
/// the song cover backdrop (the cover itself, heavily blurred and made
/// vivid, or, if the user turns that off, a soft wash of its colours) and
/// its most striking colour for an accent that follows the cover. While
/// nothing plays, the backdrop is a glow of the look's own accents. Works
/// only while the look in use needs it.
/// </summary>
public sealed class ArtworkSampler : IDisposable
{
    // Tiny on purpose: decoding, colour picking, blurring and the wash take well under a millisecond.
    private const int Size = 40;
    private const int BlurRadius = 3;

    private readonly PlayerRouter _player;
    private readonly ThemeService _theme;
    private readonly HttpClient _http;
    private readonly CoverStore? _covers;
    private readonly DispatcherQueue _queue;
    private CancellationTokenSource? _sampling;
    private object? _key;
    private int _updateQueued;

    // The cover in _key at Size × Size once read, kept so switching the
    // blurred cover on or off redraws at once, without downloading it again.
    private byte[]? _pixels;

    // Whether Blurred holds the blurred cover (true) or the wash (false).
    private bool _showsBlurredCover;

    // The look's accents Blurred glows with while nothing plays; null while it shows a cover.
    private (ThemeColor, ThemeColor)? _glow;

    /// <summary>Call on the interface thread.</summary>
    /// <param name="covers">Where covers are kept; the player bar has usually fetched the playing one already.</param>
    public ArtworkSampler(PlayerRouter player, ThemeService theme, HttpClient http, CoverStore? covers)
    {
        _player = player;
        _theme = theme;
        _http = http;
        _covers = covers;
        _queue = DispatcherQueue.GetForCurrentThread();
        player.StateChanged += OnStateChanged;
        theme.Changed += (_, _) => Update();
    }

    /// <summary>Raised on the interface thread when <see cref="Blurred"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// What the song cover backdrop shows: the blurred cover, or a wash of
    /// its colours when the user turns that off; while nothing plays, a glow
    /// of the look's accents. Null only before the first look that needs it.
    /// </summary>
    public ImageSource? Blurred { get; private set; }

    public void Dispose()
    {
        _player.StateChanged -= OnStateChanged;
        _sampling?.Cancel();
        _sampling?.Dispose();
        _sampling = null;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; look at the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            _queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                Update();
            });
        }
    }

    private void Update()
    {
        // The user switched the blurred cover on or off: redrawn at once, even
        // while another look shows, so a cover they turned off never comes back.
        if (_showsBlurredCover != _theme.BlurredCoverBackground && Blurred is not null)
        {
            ShowBackdrop(_pixels);
        }

        var look = _theme.Current;
        if (look.Backdrop != WindowBackdrop.Artwork && !look.AdaptiveAccent)
        {
            return;
        }

        var state = _player.State;
        var name = state.Album ?? state.Title;
        object? key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? name;
        if (!Equals(key, _key))
        {
            _key = key;
            _pixels = null;
            _sampling?.Cancel();
            _sampling?.Dispose();
            _sampling = new CancellationTokenSource();
            _ = SampleAsync(state.ArtworkUrl, state.ArtworkBytes, name, _sampling.Token);
        }
        else if (key is null && (Blurred is null || !Equals(_glow, (look.Accent, look.Accent2))))
        {
            // Nothing plays (also at start-up): the glow follows the look's accents.
            ShowBackdrop(null);
        }
    }

    private async Task SampleAsync(string? url, byte[]? image, string? name, CancellationToken cancellationToken)
    {
        byte[]? pixels = null;
        try
        {
            if (url is not null)
            {
                image = _covers is not null && CoverStore.Handles(url)
                    ? await _covers.GetAsync(url).WaitAsync(cancellationToken)
                    : await _http.GetByteArrayAsync(url, cancellationToken);
            }

            if (image is not null)
            {
                pixels = await CoverDecoder.DecodeAsync(image, Size, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Offline, slow or an unreadable picture: the album's tile colours
            // stand in for the cover, so the backdrop never stays dark.
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (pixels is null && name is not null)
        {
            var (from, to) = Artwork.PlaceholderColors(name);
            pixels = ArtworkColors.Gradient(from, to, Size);
        }

        _pixels = pixels;
        ShowBackdrop(pixels);
        _theme.SetArtworkAccent(pixels is null ? null : ArtworkColors.PickAccent(pixels, Size, Size));
    }

    /// <summary>
    /// Draws the backdrop picture from the cover (or, without one, from the
    /// look's two accents): blurred and made vivid, so a dark cover still
    /// glows through the look's tint, or a wash of its colours alone when the
    /// user turns the blurred cover off.
    /// </summary>
    private void ShowBackdrop(byte[]? pixels)
    {
        _showsBlurredCover = _theme.BlurredCoverBackground;
        _glow = null;
        if (pixels is null)
        {
            var look = _theme.Current;
            _glow = (look.Accent, look.Accent2);
            pixels = ArtworkColors.Gradient(look.Accent.Opaque, look.Accent2.Opaque, Size);
        }

        byte[] shown;
        if (_showsBlurredCover)
        {
            shown = (byte[])pixels.Clone();
            ArtworkColors.Blur(shown, Size, Size, BlurRadius);
            ArtworkColors.Vivid(shown, Size, Size);
        }
        else
        {
            shown = ArtworkColors.ColourWash(pixels, Size, Size, Size, Size);
        }

        var picture = new WriteableBitmap(Size, Size);
        shown.CopyTo(picture.PixelBuffer);
        picture.Invalidate();
        Blurred = picture;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // How far a light look's wash on Home is mixed towards its background.
    private const double LightWash = 0.6;

    // Home's greeting: the last cover it washed and the picture made from it,
    // so going back to Home neither downloads nor paints it again.
    private object? _washKey;
    private byte[]? _washCover;
    private (object Key, ThemeColor? Light, ImageSource Picture)? _lastWash;

    /// <summary>
    /// A soft wash of a cover's colours, for the greeting on Home: the cover
    /// at <paramref name="url"/> or in <paramref name="image"/>, or the tile
    /// colours of <paramref name="name"/> when it has none (or can not be
    /// read), or the look's two accents when there is no song at all. With
    /// <paramref name="light"/> (a light look's background) the wash is
    /// mixed well towards it, so dark text reads over it. Works whatever the
    /// look; a cover the backdrop has already read is not downloaded again.
    /// Call on the interface thread.
    /// </summary>
    public async Task<ImageSource> GetWashAsync(string? url, byte[]? image, string? name, ThemeColor? light, CancellationToken cancellationToken)
    {
        var look = _theme.Current;
        object key = url ?? (object?)image ?? (object?)name ?? (look.Accent.Opaque, look.Accent2.Opaque);
        if (_lastWash is { } last && Equals(last.Key, key) && last.Light == light)
        {
            return last.Picture;
        }

        var pixels = Equals(key, _key) ? _pixels : Equals(key, _washKey) ? _washCover : null;
        if (pixels is null)
        {
            try
            {
                if (url is not null)
                {
                    image = _covers is not null && CoverStore.Handles(url)
                        ? await _covers.GetAsync(url).WaitAsync(cancellationToken)
                        : await _http.GetByteArrayAsync(url, cancellationToken);
                }

                if (image is not null)
                {
                    pixels = await CoverDecoder.DecodeAsync(image, Size, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Offline, slow or an unreadable picture: the tile colours stand in.
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (pixels is null)
            {
                var (from, to) = name is not null ? Artwork.PlaceholderColors(name) : (look.Accent.Opaque, look.Accent2.Opaque);
                pixels = ArtworkColors.Gradient(from, to, Size);
            }

            _washKey = key;
            _washCover = pixels;
        }

        var wash = ArtworkColors.ColourWash(pixels, Size, Size, Size, Size);
        if (light is { } background)
        {
            for (var i = 0; i < wash.Length; i += 4)
            {
                var colour = new ThemeColor(0xFF, wash[i + 2], wash[i + 1], wash[i]).Mix(background.Opaque, LightWash);
                wash[i] = colour.B;
                wash[i + 1] = colour.G;
                wash[i + 2] = colour.R;
            }
        }

        var picture = new WriteableBitmap(Size, Size);
        wash.CopyTo(picture.PixelBuffer);
        picture.Invalidate();
        _lastWash = (key, light, picture);
        return picture;
    }
}

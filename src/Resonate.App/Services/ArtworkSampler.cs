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
/// the song cover backdrop (the cover itself, only blurred, as much as the
/// user chose; the owner asked for nothing else on it) and its most
/// striking colour for an accent that follows the cover. While nothing
/// plays there is no picture, and the look's background colour shows.
/// Works only while the look in use needs it.
/// </summary>
public sealed class ArtworkSampler : IDisposable
{
    // Tiny on purpose: decoding and the wash take well under a millisecond.
    private const int Size = 40;

    // The backdrop's cover: sharp across a 5K window with little blur (160 looked
    // coarse there, the owner's report of 9 October 2026), blurred in a few
    // milliseconds when the song or the blur changes, never per frame.
    private const int BackdropSize = 512;

    // The strongest blur (at 100 %), in the backdrop cover's pixels; three box passes make it soft.
    private const int MaxBlurRadius = 64;

    private readonly PlayerRouter _player;
    private readonly ThemeService _theme;
    private readonly HttpClient _http;
    private readonly CoverStore? _covers;
    private readonly DispatcherQueue _queue;
    private CancellationTokenSource? _sampling;
    private object? _key;
    private int _updateQueued;

    // The cover in _key at BackdropSize × BackdropSize once read, kept so a
    // new blur amount redraws at once, without downloading it again.
    private byte[]? _pixels;

    // The blur and the panels' see-through amount Blurred was drawn for.
    private int _shownBlur = -1;
    private int _backdropVersion;
    private double _shownPanelOpacity = -1;

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

    /// <summary>What the song cover backdrop shows: the blurred cover; null while nothing plays.</summary>
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
        // The user moved the blur: redrawn at once.
        if ((_shownBlur != _theme.CoverBlur || _shownPanelOpacity != _theme.Current.PanelOpacity) && _pixels is not null)
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
            _ = SampleAsync(state.FullArtworkUrl ?? state.ArtworkUrl, state.ArtworkBytes, name, _sampling.Token);
        }
        else if (key is null && Blurred is not null)
        {
            // Nothing plays: no picture, only the look's background colour.
            _backdropVersion++;
            Blurred = null;
            Changed?.Invoke(this, EventArgs.Empty);
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
                pixels = await CoverDecoder.DecodeAsync(image, BackdropSize, cancellationToken);
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
            pixels = ArtworkColors.Gradient(from, to, BackdropSize);
        }

        _pixels = pixels;
        ShowBackdrop(pixels);
        _theme.SetArtworkAccent(pixels is null ? null : ArtworkColors.PickAccents(pixels, BackdropSize, BackdropSize));
    }

    /// <summary>
    /// Draws the backdrop picture from the cover, or none without one: only
    /// blurred, by the user's amount, with no tint, wash or colour change.
    /// One tiny bitmap stretched by the GPU, so nothing is blurred per frame.
    /// </summary>
    private void ShowBackdrop(byte[]? pixels)
    {
        _shownBlur = _theme.CoverBlur;
        _shownPanelOpacity = _theme.Current.PanelOpacity;
        if (pixels is null)
        {
            _backdropVersion++;
            Blurred = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _ = DrawBackdropAsync(pixels, (int)Math.Round(MaxBlurRadius * _shownBlur / 100.0), _shownPanelOpacity, ++_backdropVersion);
    }

    /// <summary>Blurs off the interface thread (a few milliseconds at this size); only the newest picture asked for is shown.</summary>
    private async Task DrawBackdropAsync(byte[] pixels, int radius, double panelOpacity, int version)
    {
        var shown = await Task.Run(() =>
        {
            var copy = (byte[])pixels.Clone();
            ArtworkColors.Blur(copy, BackdropSize, BackdropSize, radius);

            // A bright cover is darkened just enough for the white text and icons over it to read; a dark one is left alone.
            ArtworkColors.DimForWhiteText(copy, BackdropSize, BackdropSize, panelOpacity);
            return copy;
        });
        if (version != _backdropVersion)
        {
            return;
        }

        var picture = new WriteableBitmap(BackdropSize, BackdropSize);
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

        // The backdrop's copy of the cover is larger than the wash's own.
        var fromBackdrop = Equals(key, _key) && _pixels is not null;
        var pixels = fromBackdrop ? _pixels : Equals(key, _washKey) ? _washCover : null;
        var side = fromBackdrop ? BackdropSize : Size;
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

        var wash = ArtworkColors.ColourWash(pixels, side, side, Size, Size);
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

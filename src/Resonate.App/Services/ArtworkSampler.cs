using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.App.Themes;
using Resonate.Spotify.Playback;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Services;

/// <summary>
/// Reads the playing song's cover for looks that use it: the picture behind
/// the song cover backdrop (a soft wash of the cover's colours, or, once the
/// user allows it, the cover itself, heavily blurred) and its most striking
/// colour for an accent that follows the cover. Works only while the look in
/// use needs it.
/// </summary>
public sealed class ArtworkSampler : IDisposable
{
    // Tiny on purpose: decoding, colour picking, blurring and the wash take well under a millisecond.
    private const int Size = 40;
    private const int BlurRadius = 3;

    private readonly PlayerRouter _player;
    private readonly ThemeService _theme;
    private readonly HttpClient _http;
    private readonly DispatcherQueue _queue;
    private CancellationTokenSource? _sampling;
    private object? _key;
    private int _updateQueued;

    // The cover in _key at Size × Size once read, kept so switching the
    // blurred cover on or off redraws at once, without downloading it again.
    private byte[]? _pixels;

    // Whether Blurred holds the blurred cover (true) or the wash (false).
    private bool _showsBlurredCover;

    /// <summary>Call on the interface thread.</summary>
    public ArtworkSampler(PlayerRouter player, ThemeService theme, HttpClient http)
    {
        _player = player;
        _theme = theme;
        _http = http;
        _queue = DispatcherQueue.GetForCurrentThread();
        player.StateChanged += OnStateChanged;
        theme.Changed += (_, _) => Update();
    }

    /// <summary>Raised on the interface thread when <see cref="Blurred"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// What the song cover backdrop shows: a wash of the cover's colours, or
    /// the blurred cover when the user allows it; null when nothing is
    /// playing.
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
            return;
        }

        // The same cover, but the user may have switched the blurred cover on or off.
        if (_pixels is not null && _showsBlurredCover != _theme.BlurredCoverBackground)
        {
            ShowBackdrop(_pixels);
        }
    }

    private async Task SampleAsync(string? url, byte[]? image, string? name, CancellationToken cancellationToken)
    {
        byte[]? pixels = null;
        try
        {
            if (url is not null)
            {
                image = await _http.GetByteArrayAsync(url, cancellationToken);
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
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Offline or slow: the album's tile colours stand in for the cover.
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
    /// Draws the backdrop picture from the cover: blurred only when the user
    /// allows it, otherwise a wash made of its colours alone.
    /// </summary>
    private void ShowBackdrop(byte[]? pixels)
    {
        _showsBlurredCover = _theme.BlurredCoverBackground;
        WriteableBitmap? picture = null;
        if (pixels is not null)
        {
            byte[] shown;
            if (_showsBlurredCover)
            {
                shown = (byte[])pixels.Clone();
                ArtworkColors.Blur(shown, Size, Size, BlurRadius);
            }
            else
            {
                shown = ArtworkColors.ColourWash(pixels, Size, Size, Size, Size);
            }

            picture = new WriteableBitmap(Size, Size);
            shown.CopyTo(picture.PixelBuffer);
            picture.Invalidate();
        }

        Blurred = picture;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

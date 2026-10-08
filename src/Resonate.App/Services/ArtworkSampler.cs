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
/// Reads the playing song's cover for looks that use it: a heavily blurred
/// copy for the artwork backdrop, and its most striking colour for an accent
/// that follows the cover. Works only while the look in use needs it.
/// </summary>
public sealed class ArtworkSampler : IDisposable
{
    // Tiny on purpose: decoding, colour picking and blurring take well under a millisecond.
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

    /// <summary>The blurred cover, or null when nothing is playing.</summary>
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
        if (Equals(key, _key))
        {
            return;
        }

        _key = key;
        _sampling?.Cancel();
        _sampling?.Dispose();
        _sampling = new CancellationTokenSource();
        _ = SampleAsync(state.ArtworkUrl, state.ArtworkBytes, name, _sampling.Token);
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

        ThemeColor? accent = null;
        WriteableBitmap? blurred = null;
        if (pixels is not null)
        {
            accent = ArtworkColors.PickAccent(pixels, Size, Size);
            ArtworkColors.Blur(pixels, Size, Size, BlurRadius);
            blurred = new WriteableBitmap(Size, Size);
            pixels.CopyTo(blurred.PixelBuffer);
            blurred.Invalidate();
        }

        Blurred = blurred;
        Changed?.Invoke(this, EventArgs.Empty);
        _theme.SetArtworkAccent(accent);
    }
}

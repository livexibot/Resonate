using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Resonate.App.Helpers;

/// <summary>
/// The large picture at the top of a page: a playlist's or album's cover,
/// an artist's portrait. While it loads, the frame and its shadow stay
/// empty, so no colour tile flashes before the picture; the tile shows only
/// when there is no picture or it can not be had. A picture already shown
/// stays when the page's details are refreshed without one.
/// </summary>
public sealed class PageCover
{
    private readonly Panel _frame;
    private readonly Image _image;
    private readonly UIElement _shadow;
    private readonly int _displayWidth;
    private string? _url;
    private int _version;

    /// <param name="frame">The rounded frame, whose background is the colour tile.</param>
    /// <param name="image">The picture inside the frame.</param>
    /// <param name="shadow">The frame's shadow, hidden while the frame is empty.</param>
    /// <param name="displayWidth">The largest width the picture is shown at (it is decoded at that size).</param>
    public PageCover(Panel frame, Image image, UIElement shadow, int displayWidth)
    {
        _frame = frame;
        _image = image;
        _shadow = shadow;
        _displayWidth = displayWidth;
    }

    /// <summary>Shows the picture at <paramref name="url"/>, or the colour tile <paramref name="placeholder"/> without one.</summary>
    public void Show(string? url, Brush placeholder)
    {
        if (url is null)
        {
            if (_url is null)
            {
                _frame.Background = placeholder;
                _shadow.Opacity = 1;
            }

            return;
        }

        if (url == _url)
        {
            return;
        }

        _url = url;
        var version = ++_version;
        _image.Source = App.Services.Covers.Get(url, _displayWidth, out var missing);
        _frame.Background = null;
        if (!missing.IsCompleted)
        {
            _shadow.Opacity = 0;
        }

        _ = RevealAsync(new WeakReference<PageCover>(this), version, missing, placeholder);
    }

    // Holds the page only weakly: a picture that never answers must not keep a closed page alive.
    private static async Task RevealAsync(WeakReference<PageCover> cover, int version, Task<bool> missing, Brush placeholder)
    {
        var gone = await missing;
        if (cover.TryGetTarget(out var target) && target._version == version)
        {
            if (gone)
            {
                target._frame.Background = placeholder;
            }

            target._shadow.Opacity = 1;
        }
    }
}

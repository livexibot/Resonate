using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.Spotify.Library;
using Resonate.Windows;

namespace Resonate.App.Services;

/// <summary>
/// The covers the interface shows. One picture is made per cover and size
/// and shared by every row, card and page that shows it, and the ones used
/// lately stay ready, so a list opened again shows its covers at once. The
/// bytes come from <see cref="CoverStore"/> (memory, then disk, then
/// Spotify, over one connection). A cover from anywhere else loads the way
/// Windows loads any address. Use on the interface thread only.
/// </summary>
public sealed class CoverImages
{
    /// <summary>Ready pictures kept: several pages of rows and cards.</summary>
    private const int Kept = 400;

    private readonly Dictionary<(string Url, int Width), LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _recent = new();

    /// <summary>
    /// The user's App size as a factor (MainWindow.ApplyAppSize): a cover shown
    /// at a given width covers that many more screen pixels, so it is decoded
    /// larger to stay sharp.
    /// </summary>
    public static double Scale { get; set; } = 1;

    /// <summary>
    /// The display's scale (MainWindow.UpdateMinimumSize): covers are decoded
    /// in screen pixels. "Logical" decoding read pictures made from bytes at
    /// 100 %, before they were on screen, so they were stretched and soft at
    /// 125 % (the owner's sidebar covers, 9 October 2026).
    /// </summary>
    public static double DisplayScale { get; set; } = 1;

    /// <param name="store">Where the bytes come from; null lets Windows load every address itself (demo mode).</param>
    public CoverImages(CoverStore? store) => Store = store;

    public CoverStore? Store { get; }

    /// <summary>
    /// The picture for <paramref name="url"/>, decoded at
    /// <paramref name="displayWidth"/> (the size it is shown at, not full
    /// size), or null without an address. It may still be loading: whatever
    /// shows it fills in when it is ready.
    /// </summary>
    public ImageSource? Get(string? url, int displayWidth) => Find(url, displayWidth)?.Bitmap;

    /// <summary>
    /// Like <see cref="Get"/>, and <paramref name="missing"/> ends true when
    /// the picture can not be had (no address, offline, or a picture Windows
    /// can not read), so a colour tile can stand in for it, or false once it
    /// shows. A picture is never shown over a colour tile while it loads:
    /// that tile would flash for a moment before every cover.
    /// </summary>
    public ImageSource? Get(string? url, int displayWidth, out Task<bool> missing)
    {
        var entry = Find(url, displayWidth);
        missing = entry?.Missing ?? Task.FromResult(true);
        return entry?.Bitmap;
    }

    /// <summary>
    /// Like <see cref="Get"/>, but waits until the picture has its pixels
    /// (for places that fade a cover in). <c>Loaded</c> is false when the
    /// picture could not be had, or when Windows loads the address itself
    /// (not one of Spotify's): then the image's ImageOpened event tells.
    /// </summary>
    public async Task<(ImageSource? Image, bool Loaded)> GetReadyAsync(string? url, int displayWidth)
    {
        if (Find(url, displayWidth) is not { } entry)
        {
            return (null, false);
        }

        return (entry.Bitmap, await entry.Ready);
    }

    /// <summary>
    /// A picture made from a cover's bytes (a local file's own cover), decoded
    /// at <paramref name="displayWidth"/>; null when Windows can not read it.
    /// Call on the interface thread.
    /// </summary>
    public static async Task<(ImageSource? Image, bool Loaded)> FromBytesAsync(byte[] bytes, int displayWidth)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = DecodeWidth(displayWidth), DecodePixelType = DecodePixelType.Physical };
        try
        {
            using var stream = await ImageStreams.FromBytesAsync(bytes);
            await bitmap.SetSourceAsync(stream);
            return (bitmap, true);
        }
        catch (Exception)
        {
            // A damaged picture: the colour tile stays.
            return (null, false);
        }
    }

    /// <summary>The width to decode a cover shown <paramref name="displayWidth"/> wide at, at the user's App size.</summary>
    public static int DecodeWidth(int displayWidth) => (int)Math.Ceiling(displayWidth * Scale * DisplayScale);

    private Entry? Find(string? url, int displayWidth)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        (string Url, int Width) key = (url, DecodeWidth(displayWidth));
        if (_index.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value;
        }

        var bitmap = new BitmapImage { DecodePixelWidth = key.Width, DecodePixelType = DecodePixelType.Physical };
        Task<bool> ready;
        Task<bool> missing;
        if (Store is null || !CoverStore.Handles(url))
        {
            // Windows decodes it once it is on screen, and tells then.
            var told = new TaskCompletionSource<bool>();
            bitmap.ImageOpened += (_, _) => told.TrySetResult(false);
            bitmap.ImageFailed += (_, _) => told.TrySetResult(true);
            bitmap.UriSource = uri;
            ready = Task.FromResult(false);
            missing = told.Task;
        }
        else
        {
            ready = LoadAsync(key, bitmap);
            missing = FailedAsync(ready);
        }

        var entry = new Entry(key, bitmap, ready, missing);
        _index[key] = _recent.AddFirst(entry);
        while (_index.Count > Kept && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _index.Remove(oldest.Value.Key);
        }

        return entry;
    }

    /// <returns>True once the picture has its pixels; false when it could not be had (the colour tile stays).</returns>
    private async Task<bool> LoadAsync((string Url, int Width) key, BitmapImage bitmap)
    {
        var store = Store!;

        // Resumes on the interface thread, where the picture lives.
        var bytes = store.TryGetRecent(key.Url) ?? await store.GetAsync(key.Url);
        if (bytes is not null)
        {
            try
            {
                using var stream = await ImageStreams.FromBytesAsync(bytes);
                await bitmap.SetSourceAsync(stream);
                return true;
            }
            catch (Exception)
            {
                // A damaged copy: fetched again next time.
                _ = Task.Run(() => store.Forget(key.Url));
            }
        }

        // Offline, or a picture Windows could not read: not kept, so the next
        // time it is shown it is tried again.
        if (_index.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Bitmap, bitmap))
        {
            _recent.Remove(node);
            _index.Remove(key);
        }

        return false;
    }

    private static async Task<bool> FailedAsync(Task<bool> ready) => !await ready;

    private sealed record Entry((string Url, int Width) Key, BitmapImage Bitmap, Task<bool> Ready, Task<bool> Missing);
}

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Windows;

namespace Resonate.App.Helpers;

/// <summary>Covers for the rows of Local Files, read from the music files themselves.</summary>
public static class LocalArtwork
{
    /// <summary>
    /// An image for <paramref name="track"/>, empty until its small cover is
    /// read in the background (and for good when the file has none). Call on
    /// the interface thread.
    /// </summary>
    public static ImageSource? For(TrackInfo track, int displayWidth) => For(track, displayWidth, out _);

    /// <summary>
    /// An image for <paramref name="track"/>'s row, empty until its small
    /// cover is read in the background; <paramref name="missing"/> ends true
    /// when the file has no cover (or one Windows can not read), so the
    /// album's colour tile can stand in, and false once the cover shows.
    /// Call on the interface thread.
    /// </summary>
    public static ImageSource? For(TrackInfo track, int displayWidth, out Task<bool> missing)
    {
        if (App.Services.LocalFiles.Covers is null || track.FilePath is null)
        {
            missing = Task.FromResult(true);
            return null;
        }

        var bitmap = new BitmapImage { DecodePixelWidth = CoverImages.DecodeWidth(displayWidth), DecodePixelType = DecodePixelType.Logical };
        missing = LoadAsync(bitmap, track);
        return bitmap;
    }

    /// <returns>True when the file has no cover that can be shown.</returns>
    private static async Task<bool> LoadAsync(BitmapImage bitmap, TrackInfo track)
    {
        // Resumes on the interface thread, where the bitmap lives.
        if (await App.Services.LocalFiles.GetThumbnailAsync(track) is not { } bytes)
        {
            return true;
        }

        try
        {
            using var stream = await ImageStreams.FromBytesAsync(bytes);
            await bitmap.SetSourceAsync(stream);
            return false;
        }
        catch (Exception)
        {
            // A damaged picture: the colour tile shows.
            return true;
        }
    }
}

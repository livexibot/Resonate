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
    /// An image for <paramref name="track"/>'s row, empty until its small
    /// cover is read in the background (the album's colour tile shows
    /// meanwhile, and stays when the file has no cover). Call on the
    /// interface thread.
    /// </summary>
    public static ImageSource? For(TrackInfo track, int displayWidth)
    {
        if (App.Services.LocalFiles.Covers is null || track.FilePath is null)
        {
            return null;
        }

        var bitmap = new BitmapImage { DecodePixelWidth = CoverImages.DecodeWidth(displayWidth), DecodePixelType = DecodePixelType.Logical };
        _ = LoadAsync(bitmap, track);
        return bitmap;
    }

    private static async Task LoadAsync(BitmapImage bitmap, TrackInfo track)
    {
        // Resumes on the interface thread, where the bitmap lives.
        if (await App.Services.LocalFiles.GetThumbnailAsync(track) is not { } bytes)
        {
            return;
        }

        try
        {
            using var stream = await ImageStreams.FromBytesAsync(bytes);
            await bitmap.SetSourceAsync(stream);
        }
        catch (Exception)
        {
            // A damaged picture: the colour tile stays.
        }
    }
}

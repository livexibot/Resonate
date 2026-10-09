using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Helpers;

/// <summary>
/// The special looks' little pictures (<see cref="SceneSprites"/>) as brushes,
/// one per picture and size for the whole app, shared by every sprite that
/// shows it. Each picture is drawn in code at the size closest above the one
/// it shows at (so it is never shrunk much, which would shimmer), then decoded
/// by Windows; a brush shows nothing until its picture is ready, and nothing
/// at all if it fails.
/// </summary>
internal static class SceneSpriteBrushes
{
    // Pixels per side the pictures are drawn at; the largest is SceneSprites.Size.
    private static readonly int[] Sizes = [24, 48, SceneSprites.Size];

    private static readonly Dictionary<(SceneSprite, int), CompositionSurfaceBrush> Brushes = [];
    private static Compositor? _compositor;

    /// <summary>Why the last picture could not be made; null while all is well.</summary>
    public static string? Error { get; private set; }

    /// <summary>
    /// The brush for <paramref name="sprite"/> shown <paramref name="pixels"/>
    /// screen pixels wide at most.
    /// </summary>
    public static CompositionSurfaceBrush Get(Compositor compositor, SceneSprite sprite, double pixels)
    {
        if (!ReferenceEquals(compositor, _compositor))
        {
            Brushes.Clear();
            _compositor = compositor;
        }

        var size = Sizes.FirstOrDefault(s => s >= pixels * 0.9, SceneSprites.Size);
        if (Brushes.TryGetValue((sprite, size), out var brush))
        {
            return brush;
        }

        brush = compositor.CreateSurfaceBrush();
        brush.Stretch = CompositionStretch.Fill;
        brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
        Brushes[(sprite, size)] = brush;
        _ = FillAsync(brush, sprite, size);
        return brush;
    }

    /// <summary>For CI's screenshot tour: makes and decodes one picture of each kind, and says why one failed (null once all are ready).</summary>
    public static async Task<string?> CheckAsync()
    {
        foreach (var sprite in Enum.GetValues<SceneSprite>())
        {
            if (await LoadAsync(sprite, 24) is not { } surface)
            {
                return $"{sprite}: {Error ?? "it did not load."}";
            }

            surface.Dispose();
        }

        return null;
    }

    private static async Task FillAsync(CompositionSurfaceBrush brush, SceneSprite sprite, int size)
    {
        var surface = await LoadAsync(sprite, size);
        if (surface is not null)
        {
            brush.Surface = surface;
        }
    }

    private static async Task<LoadedImageSurface?> LoadAsync(SceneSprite sprite, int size)
    {
        try
        {
            var png = await Task.Run(() => SceneSprites.Png(sprite, size));
            var stream = await ImageStreams.FromBytesAsync(png);
            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            var loaded = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.LoadCompleted += (_, e) =>
            {
                stream.Dispose();
                loaded.TrySetResult(e.Status);
            };

            var status = await loaded.Task;
            if (status != LoadedImageSourceLoadStatus.Success)
            {
                Error = $"Windows could not decode it ({status}).";
                surface.Dispose();
                return null;
            }

            return surface;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return null;
        }
    }
}

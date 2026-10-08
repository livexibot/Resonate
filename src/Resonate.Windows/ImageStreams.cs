using Windows.Storage.Streams;

namespace Resonate.Windows;

/// <summary>
/// Picture bytes as Windows streams and back, made only of Windows' own
/// objects. .NET's adapter for the other direction (AsRandomAccessStream)
/// hands Windows a .NET object to call into, which the Native AOT build does
/// not reliably support, so covers that came from bytes (local files' own
/// covers) never showed; these never need it. The writer and reader are
/// closed as soon as they are done, so their copy of the picture is let go
/// of at once rather than whenever .NET next collects.
/// </summary>
public static class ImageStreams
{
    /// <summary>A stream holding <paramref name="bytes"/>, at its start. The caller disposes it.</summary>
    public static async Task<IRandomAccessStream> FromBytesAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            if (bytes.Length > 0)
            {
                using var writer = new DataWriter(stream);
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask().ConfigureAwait(false);

                // The stream belongs to the caller: closing the writer must not close it.
                writer.DetachStream();
            }

            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Everything in <paramref name="stream"/>, from its start.</summary>
    public static async Task<byte[]> ToBytesAsync(IRandomAccessStream stream)
    {
        var size = checked((uint)stream.Size);
        if (size == 0)
        {
            return [];
        }

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync(size).AsTask().ConfigureAwait(false);
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        return bytes;
    }
}

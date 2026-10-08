using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Resonate.Windows;

/// <summary>
/// Picture bytes as Windows streams and back, made only of Windows' own
/// objects. .NET's adapter for the other direction (AsRandomAccessStream)
/// hands Windows a .NET object to call into, which the Native AOT build does
/// not reliably support, so covers that came from bytes (local files' own
/// covers) never showed; these never need it.
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
                await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(bytes)).AsTask().ConfigureAwait(false);
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

        stream.Seek(0);
        var buffer = await stream.ReadAsync(new global::Windows.Storage.Streams.Buffer(size), size, InputStreamOptions.None).AsTask().ConfigureAwait(false);
        CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
        return bytes ?? [];
    }
}

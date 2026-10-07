namespace Resonate.Plugins.Installing;

/// <summary>Where plugin files are downloaded from.</summary>
public interface IPluginFeed
{
    /// <summary>Opens <paramref name="file"/>; the length is null when the source does not say.</summary>
    Task<(Stream Content, long? Length)> OpenAsync(string file, CancellationToken cancellationToken);
}

/// <summary>A release on GitHub (or any web address that ends in a slash).</summary>
public sealed class HttpPluginFeed : IPluginFeed
{
    private readonly HttpClient _http;
    private readonly Uri _baseAddress;

    public HttpPluginFeed(HttpClient http, Uri baseAddress)
    {
        if (baseAddress.Scheme != Uri.UriSchemeHttps && !baseAddress.IsLoopback)
        {
            throw new ArgumentException("Plugins download over HTTPS only.", nameof(baseAddress));
        }

        _http = http;
        _baseAddress = baseAddress;
    }

    public async Task<(Stream Content, long? Length)> OpenAsync(string file, CancellationToken cancellationToken)
    {
        var response = await _http
            .GetAsync(new Uri(_baseAddress, Uri.EscapeDataString(file)), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return (new ResponseStream(stream, response), response.Content.Headers.ContentLength);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Disposes the response along with its content.</summary>
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>A folder of files, for CI's install test and for trying plugins before a release.</summary>
public sealed class FolderPluginFeed : IPluginFeed
{
    private readonly string _folder;

    public FolderPluginFeed(string folder) => _folder = folder;

    public Task<(Stream Content, long? Length)> OpenAsync(string file, CancellationToken cancellationToken)
    {
        if (file.Contains('/', StringComparison.Ordinal) || file.Contains('\\', StringComparison.Ordinal) || file.Contains("..", StringComparison.Ordinal))
        {
            throw new FileNotFoundException("Not a plain file name.", file);
        }

        Stream stream = File.OpenRead(Path.Combine(_folder, file));
        return Task.FromResult<(Stream, long?)>((stream, stream.Length));
    }
}

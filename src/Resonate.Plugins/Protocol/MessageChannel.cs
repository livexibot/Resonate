using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Resonate.Plugins.Protocol;

/// <summary>
/// Messages as lines of JSON over a pair of streams: Resonate's end of the
/// helper's standard input and output, or the helper's own end of them.
/// Sending is safe from any thread.
/// </summary>
public sealed class MessageChannel : IDisposable
{
    /// <summary>Longer lines are dropped. The helper caps what plugins send well below this.</summary>
    public const int MaxLineLength = 1024 * 1024;

    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Lock _writeGate = new();
    private volatile bool _closed;

    /// <param name="input">Where messages arrive.</param>
    /// <param name="output">Where messages are sent.</param>
    public MessageChannel(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>The other side went away (sending failed or the input ended).</summary>
    public bool IsClosed => _closed;

    /// <summary>Sends a message; returns false when the other side is gone.</summary>
    public bool Send(HostMessage message)
    {
        if (_closed)
        {
            return false;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.HostMessage);
        lock (_writeGate)
        {
            try
            {
                _output.Write(bytes);
                _output.Write(NewLine);
                _output.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _closed = true;
                return false;
            }
        }
    }

    /// <summary>Every message until the input ends. Lines that are not a message are skipped.</summary>
    public async IAsyncEnumerable<HostMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(_input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                line = null;
            }

            if (line is null)
            {
                break;
            }

            if (line.Length == 0 || line.Length > MaxLineLength)
            {
                continue;
            }

            var message = Parse(line);
            if (message is not null)
            {
                yield return message;
            }
        }

        _closed = true;
    }

    public void Dispose()
    {
        _closed = true;
        _input.Dispose();
        lock (_writeGate)
        {
            _output.Dispose();
        }
    }

    internal static HostMessage? Parse(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.HostMessage);
            return message is { Type.Length: > 0 } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

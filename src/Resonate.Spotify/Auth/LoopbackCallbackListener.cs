using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Resonate.Spotify.Auth;

/// <summary>
/// Receives the browser's redirect after the user approves Resonate. A tiny
/// HTTP server on 127.0.0.1 only, so nothing outside this computer can reach
/// it. A raw socket avoids http.sys URL reservations on Windows.
/// </summary>
public sealed class LoopbackCallbackListener : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;

    private readonly TcpListener _listener;
    private readonly string _path;

    public LoopbackCallbackListener(Uri redirectUri)
    {
        if (!redirectUri.IsLoopback || !IPAddress.TryParse(redirectUri.Host.Trim('[', ']'), out var address))
        {
            throw new ArgumentException("The redirect URI must use a loopback IP address such as 127.0.0.1.", nameof(redirectUri));
        }

        _path = redirectUri.AbsolutePath;
        _listener = new TcpListener(address, redirectUri.Port);
    }

    /// <summary>The port actually bound (useful when the redirect URI asked for port 0 in tests).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts listening. Throws if the port is already in use.</summary>
    public void Start() => _listener.Start();

    /// <summary>
    /// Waits for the redirect that carries <paramref name="expectedState"/> and
    /// returns its authorization code. Other requests (a favicon, a stale tab)
    /// are answered and ignored.
    /// </summary>
    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();

            string? target;
            try
            {
                target = await ReadRequestTargetAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            if (target is null || !target.StartsWith(_path, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, HttpStatusCode.NotFound, "Not found.", cancellationToken).ConfigureAwait(false);
                continue;
            }

            var query = ParseQuery(target);
            if (!query.TryGetValue("state", out var state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, HttpStatusCode.BadRequest, "This sign-in link is out of date. Start the sign-in again from Resonate.", cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (query.TryGetValue("error", out var error))
            {
                await WriteResponseAsync(stream, HttpStatusCode.OK, "Sign-in was cancelled. You can close this tab.", cancellationToken).ConfigureAwait(false);
                throw new SpotifyAuthException(error, error == "access_denied"
                    ? "You chose not to give Resonate access."
                    : "Spotify reported a problem with the sign-in: " + error);
            }

            if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                await WriteResponseAsync(stream, HttpStatusCode.BadRequest, "Spotify did not send a sign-in code.", cancellationToken).ConfigureAwait(false);
                continue;
            }

            await WriteResponseAsync(stream, HttpStatusCode.OK, "Signed in. You can close this tab and go back to Resonate.", cancellationToken).ConfigureAwait(false);
            return code;
        }
    }

    public void Dispose() => _listener.Dispose();

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var received = new MemoryStream();
        while (received.Length < MaxRequestBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            received.Write(buffer, 0, read);
            var text = Encoding.ASCII.GetString(received.GetBuffer(), 0, (int)received.Length);
            var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd >= 0)
            {
                // Request line: "GET /callback?code=...&state=... HTTP/1.1"
                var parts = text[..lineEnd].Split(' ');
                return parts is ["GET", var requestTarget, ..] ? requestTarget : null;
            }
        }

        return null;
    }

    internal static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var questionMark = target.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0)
        {
            return result;
        }

        foreach (var pair in target[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpStatusCode status, string message, CancellationToken cancellationToken)
    {
        var body = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>Resonate</title>
            <style>body{font-family:'Segoe UI',system-ui,sans-serif;background:#111214;color:#f2f2f3;display:grid;place-items:center;height:100vh;margin:0}p{font-size:18px}</style>
            </head><body><p>{{WebUtility.HtmlEncode(message)}}</p></body></html>
            """;
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

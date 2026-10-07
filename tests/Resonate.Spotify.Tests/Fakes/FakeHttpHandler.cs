using System.Net;
using System.Text;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Answers HTTP requests from a script and records what was sent.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    /// <param name="after">When given, the answer is only sent once this task completes.</param>
    public FakeHttpHandler Respond(HttpStatusCode status, string? json = null, Action<HttpResponseMessage>? configure = null, Task? after = null)
    {
        _responses.Enqueue(async _ =>
        {
            if (after is not null)
            {
                await after;
            }

            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            body,
            request.Content?.Headers.ContentType?.MediaType));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        return await _responses.Dequeue()(request);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body, string? ContentType);

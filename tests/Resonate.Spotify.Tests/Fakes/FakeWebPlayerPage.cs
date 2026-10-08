using System.Text.Json;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for the hidden page that runs Spotify's Web Playback SDK, and records what it was told.</summary>
internal sealed class FakeWebPlayerPage : IWebPlayerPage
{
    private readonly Lock _gate = new();
    private readonly List<string> _sent = [];

    public event EventHandler<string>? MessageReceived;

    public event EventHandler<string>? Failed;

    /// <summary>Thrown by <see cref="LoadAsync"/>.</summary>
    public Exception? LoadFailure { get; init; }

    public bool Loaded { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>What the page was told, in order: "start Resonate", "token token-1", "reconnect".</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        if (LoadFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        Loaded = true;
        return Task.CompletedTask;
    }

    public void Post(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        var entry = type switch
        {
            "start" => $"start {root.GetProperty("name").GetString()}",
            "token" => $"token {root.GetProperty("token").GetString()}",
            _ => type!,
        };
        lock (_gate)
        {
            _sent.Add(entry);
        }
    }

    /// <summary>The page says something, as Spotify's player would.</summary>
    public void Say(string json) => MessageReceived?.Invoke(this, json);

    public void Crash() => Failed?.Invoke(this, "The page's process ended.");

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Hands out "token-1", and "token-2" once that one was rejected.</summary>
internal sealed class FakeTokens : IAccessTokenSource
{
    public List<string?> Requests { get; } = [];

    /// <summary>Thrown by every request.</summary>
    public Exception? Failure { get; set; }

    public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(rejectedToken);
        }

        return Failure is { } failure
            ? Task.FromException<string>(failure)
            : Task.FromResult(rejectedToken is null ? "token-1" : "token-2");
    }
}

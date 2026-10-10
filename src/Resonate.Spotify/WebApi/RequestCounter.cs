using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Spotify.WebApi;

/// <summary>What a Web API request was for, as Settings, General, Spotify account lists them.</summary>
public enum RequestKind
{
    WhatPlays,
    PlayerCommands,
    Devices,
    Queue,
    History,
    Library,
    Search,
    AlbumsAndArtists,
    YourTop,
    Account,
}

/// <summary>
/// Every request Resonate sends to the Spotify Web API, counted by kind, per
/// day and in total (the owner's request, 10 October 2026, after the
/// developer app's allowance ran out). Every attempt counts, retries and
/// refused ones too, as Spotify counts them. Kept in a small file in the
/// cache folder, written at most every 10 seconds; the last 30 days are kept.
/// Thread-safe.
/// </summary>
public sealed class RequestCounter
{
    private const int DaysKept = 30;
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Counts _counts;
    private DateTimeOffset _savedAt;
    private bool _dirty;

    public RequestCounter(string? path, TimeProvider? time = null)
    {
        _path = path;
        _time = time ?? TimeProvider.System;
        _counts = Load(path) ?? new Counts();
        if (_counts.Since.Length == 0)
        {
            _counts.Since = DayKey();
        }
    }

    /// <summary>Raised on any thread after a request was counted (at most a few times a second is the caller's to ensure).</summary>
    public event EventHandler? Counted;

    /// <summary>What a request to <paramref name="path"/> (relative to the API's address) is for.</summary>
    public static RequestKind KindOf(HttpMethod method, string path)
    {
        var route = path.Split('?', 2)[0].TrimEnd('/');
        return route switch
        {
            "me/player" when method == HttpMethod.Get => RequestKind.WhatPlays,
            "me/player/devices" => RequestKind.Devices,
            "me/player/queue" when method == HttpMethod.Get => RequestKind.Queue,
            "me/player/recently-played" => RequestKind.History,
            "search" => RequestKind.Search,
            "me" => RequestKind.Account,
            _ when route.StartsWith("me/player", StringComparison.Ordinal) => RequestKind.PlayerCommands,
            _ when route.StartsWith("me/top/", StringComparison.Ordinal) => RequestKind.YourTop,
            _ when route.StartsWith("albums/", StringComparison.Ordinal) || route.StartsWith("artists/", StringComparison.Ordinal) => RequestKind.AlbumsAndArtists,
            _ => RequestKind.Library,
        };
    }

    /// <summary>Counts one request.</summary>
    public void Count(HttpMethod method, string path)
    {
        var kind = KindOf(method, path);
        bool save;
        lock (_gate)
        {
            var day = DayKey();
            if (!_counts.Days.TryGetValue(day, out var counts))
            {
                counts = [];
                _counts.Days[day] = counts;
                Trim();
            }

            var name = kind.ToString();
            counts[name] = counts.GetValueOrDefault(name) + 1;
            _counts.Total++;
            _dirty = true;
            save = _time.GetUtcNow() - _savedAt >= SaveEvery;
        }

        if (save)
        {
            Save();
        }

        Counted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every request since <see cref="Since"/>.</summary>
    public long Total
    {
        get
        {
            lock (_gate)
            {
                return _counts.Total;
            }
        }
    }

    /// <summary>The day counting began (the first day this copy of Resonate sent a request).</summary>
    public DateOnly Since
    {
        get
        {
            lock (_gate)
            {
                return DateOnly.ParseExact(_counts.Since, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Today's requests (the local calendar day) by kind, most first.</summary>
    public IReadOnlyList<(RequestKind Kind, long Count)> Today(out long total)
    {
        lock (_gate)
        {
            var list = new List<(RequestKind Kind, long Count)>();
            total = 0;
            if (_counts.Days.TryGetValue(DayKey(), out var counts))
            {
                foreach (var (name, count) in counts)
                {
                    if (Enum.TryParse<RequestKind>(name, out var kind))
                    {
                        list.Add((kind, count));
                        total += count;
                    }
                }
            }

            list.Sort((a, b) => b.Count.CompareTo(a.Count));
            return list;
        }
    }

    /// <summary>Writes the counts now if anything changed (also on closing).</summary>
    public void Save()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty || _path is null)
            {
                return;
            }

            _dirty = false;
            _savedAt = _time.GetUtcNow();
            json = JsonSerializer.Serialize(_counts, RequestCountsJsonContext.Default.Counts);
        }

        try
        {
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Counted again at the next request.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private string DayKey() => _time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void Trim()
    {
        while (_counts.Days.Count > DaysKept)
        {
            _counts.Days.Remove(_counts.Days.Keys.Min(StringComparer.Ordinal)!);
        }
    }

    private static Counts? Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), RequestCountsJsonContext.Default.Counts)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The file: the total, the day counting began, and each day's counts by kind.</summary>
    internal sealed class Counts
    {
        public long Total { get; set; }

        public string Since { get; set; } = string.Empty;

        public Dictionary<string, Dictionary<string, long>> Days { get; set; } = [];
    }
}

[JsonSerializable(typeof(RequestCounter.Counts))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class RequestCountsJsonContext : JsonSerializerContext;

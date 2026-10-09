using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Spotify.Library;

/// <summary>
/// What a song sounds like, in numbers: its tempo, key and loudness, and how
/// energetic it is. Spotify stopped giving these to new apps in November
/// 2024; they come from ReccoBeats instead, only while the user has song
/// stats switched on (the owner's choice, 9 October 2026).
/// </summary>
public sealed record SongStats(double Tempo, int Key, int Mode, double Loudness, double Energy)
{
    private static readonly string[] Notes = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    /// <summary>"113".</summary>
    public string TempoText => Tempo > 0 ? Math.Round(Tempo).ToString(CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>"G♯" for a major key, "G♯m" for a minor one; empty when unknown.</summary>
    public string KeyText => Key is >= 0 and < 12 ? Notes[Key] + (Mode == 0 ? "m" : string.Empty) : string.Empty;

    /// <summary>"-11.8 dB".</summary>
    public string LoudnessText => Loudness.ToString("0.0", CultureInfo.CurrentCulture) + " dB";

    /// <summary>"94 %".</summary>
    public string EnergyText => Math.Round(Math.Clamp(Energy, 0, 1) * 100).ToString(CultureInfo.CurrentCulture) + " %";
}

/// <summary>Where song stats come from.</summary>
public interface ISongStatsSource
{
    /// <summary>The stats of the songs it knows, by Spotify track ID; throws when it can't be reached.</summary>
    Task<IReadOnlyDictionary<string, SongStats>> GetAsync(IReadOnlyList<string> spotifyIds, CancellationToken cancellationToken);
}

/// <summary>
/// ReccoBeats (https://reccobeats.com), a free service with no account or
/// key that answers Spotify track IDs with audio features like Spotify's.
/// Only the IDs are sent. At most <see cref="MaxBatch"/> songs a request.
/// </summary>
public sealed class ReccoBeatsClient : ISongStatsSource
{
    public const int MaxBatch = 40;

    public static readonly Uri DefaultBaseUri = new("https://api.reccobeats.com/v1/");

    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public ReccoBeatsClient(HttpClient http, Uri? baseUri = null)
    {
        _http = http;
        _baseUri = baseUri ?? DefaultBaseUri;
    }

    public async Task<IReadOnlyDictionary<string, SongStats>> GetAsync(IReadOnlyList<string> spotifyIds, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, SongStats>(StringComparer.Ordinal);
        foreach (var batch in spotifyIds.Where(IsSpotifyId).Distinct(StringComparer.Ordinal).Chunk(MaxBatch))
        {
            var uri = new Uri(_baseUri, "audio-features?ids=" + string.Join(",", batch));
            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (id, stats) in Parse(json))
            {
                found[id] = stats;
            }
        }

        return found;
    }

    /// <summary>ReccoBeats' answer, by the Spotify ID at the end of each song's link.</summary>
    public static IEnumerable<(string Id, SongStats Stats)> Parse(string json)
    {
        var page = JsonSerializer.Deserialize(json, SongStatsJsonContext.Default.ReccoBeatsPage);
        foreach (var item in page?.Content ?? [])
        {
            if (item.Href is { } href && href.LastIndexOf('/') is var slash and >= 0 && IsSpotifyId(href[(slash + 1)..]))
            {
                yield return (href[(slash + 1)..], new SongStats(item.Tempo, item.Key, item.Mode, item.Loudness, item.Energy));
            }
        }
    }

    /// <summary>A Spotify track ID: 22 letters and digits.</summary>
    public static bool IsSpotifyId(string id) => id.Length == 22 && id.All(char.IsAsciiLetterOrDigit);
}

internal sealed class ReccoBeatsPage
{
    public List<ReccoBeatsFeatures>? Content { get; set; }
}

internal sealed class ReccoBeatsFeatures
{
    public string? Href { get; set; }

    public double Tempo { get; set; }

    public int Key { get; set; } = -1;

    public int Mode { get; set; } = 1;

    public double Loudness { get; set; }

    public double Energy { get; set; }
}

/// <summary>
/// Song stats kept on this PC (<c>song-stats.json</c> in the cache folder),
/// so each song is asked about once: a song ReccoBeats does not know is kept
/// as unknown for <see cref="RetryUnknownAfter"/>.
/// </summary>
public sealed class SongStatsCache
{
    public static readonly TimeSpan RetryUnknownAfter = TimeSpan.FromDays(14);

    private readonly string? _path;
    private readonly Dictionary<string, CachedSongStats> _entries;

    public SongStatsCache(string? path)
    {
        _path = path;
        _entries = Load(path);
    }

    /// <summary>True when the song was asked about: <paramref name="stats"/> is its stats, or null when unknown.</summary>
    public bool TryGet(string id, DateTimeOffset now, out SongStats? stats)
    {
        stats = null;
        if (!_entries.TryGetValue(id, out var entry))
        {
            return false;
        }

        if (entry.Tempo is null)
        {
            return now - entry.AskedAt < RetryUnknownAfter;
        }

        stats = new SongStats(entry.Tempo.Value, entry.Key, entry.Mode, entry.Loudness, entry.Energy);
        return true;
    }

    public void Store(string id, SongStats? stats, DateTimeOffset now) =>
        _entries[id] = stats is null
            ? new CachedSongStats { AskedAt = now }
            : new CachedSongStats { AskedAt = now, Tempo = stats.Tempo, Key = stats.Key, Mode = stats.Mode, Loudness = stats.Loudness, Energy = stats.Energy };

    public void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, SongStatsJsonContext.Default.DictionaryStringCachedSongStats));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this session; asked again next time.
        }
    }

    private static Dictionary<string, CachedSongStats> Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path)
                && JsonSerializer.Deserialize(File.ReadAllText(path), SongStatsJsonContext.Default.DictionaryStringCachedSongStats) is { } entries)
            {
                return new Dictionary<string, CachedSongStats>(entries, StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file starts over.
        }

        return new Dictionary<string, CachedSongStats>(StringComparer.Ordinal);
    }
}

public sealed class CachedSongStats
{
    public DateTimeOffset AskedAt { get; set; }

    /// <summary>Null when ReccoBeats did not know the song.</summary>
    public double? Tempo { get; set; }

    public int Key { get; set; } = -1;

    public int Mode { get; set; } = 1;

    public double Loudness { get; set; }

    public double Energy { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReccoBeatsPage))]
[JsonSerializable(typeof(Dictionary<string, CachedSongStats>))]
internal sealed partial class SongStatsJsonContext : JsonSerializerContext;

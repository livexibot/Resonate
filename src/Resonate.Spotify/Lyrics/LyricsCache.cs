using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Spotify.Lyrics;

/// <summary>
/// Answers kept on disk for 30 days, one small JSON file per song in the
/// cache's <c>lyrics</c> folder, "no lyrics" included, so a song is looked
/// up once a month at most. Lyrics do not change. Errors reading or writing
/// only cost a new lookup.
/// </summary>
public sealed class LyricsCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly string _folder;
    private readonly TimeProvider _time;

    public LyricsCache(string folder, TimeProvider? time = null)
    {
        _folder = folder;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The answer kept for <paramref name="query"/> while it is fresh: true
    /// with the lyrics, or with null for a kept "none"; false when there is
    /// nothing to go on. Reads the disk: call off the interface thread.
    /// </summary>
    public bool TryGet(LyricsQuery query, out SongLyrics? lyrics)
    {
        lyrics = null;
        CachedLyrics? cached;
        try
        {
            var path = PathFor(query);
            if (!File.Exists(path))
            {
                return false;
            }

            cached = JsonSerializer.Deserialize(File.ReadAllText(path), LyricsJsonContext.Default.CachedLyrics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        if (cached is null || _time.GetUtcNow() - cached.Saved >= Lifetime)
        {
            return false;
        }

        if (cached.Found)
        {
            var lines = cached.Lines?.Select(l => new LyricLine(l.At is { } at ? TimeSpan.FromMilliseconds(at) : null, l.Text ?? string.Empty)).ToList() ?? [];
            lyrics = cached.Instrumental ? SongLyrics.Instrumental : new SongLyrics(lines, cached.Synced);
        }

        return true;
    }

    /// <summary>Keeps an answer, null ("none") included. Writes the disk: call off the interface thread.</summary>
    public void Store(LyricsQuery query, SongLyrics? lyrics)
    {
        var cached = new CachedLyrics
        {
            Saved = _time.GetUtcNow(),
            Found = lyrics is not null,
            Synced = lyrics?.IsSynced == true,
            Instrumental = lyrics?.IsInstrumental == true,
            Lines = lyrics?.Lines.Select(l => new CachedLine { At = (long?)l.At?.TotalMilliseconds, Text = l.Text }).ToList(),
        };

        try
        {
            Directory.CreateDirectory(_folder);
            var path = PathFor(query);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cached, LyricsJsonContext.Default.CachedLyrics));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept; the song is looked up again next time.
        }
    }

    /// <summary>Deletes answers older than <see cref="Lifetime"/>, so the folder does not grow for ever.</summary>
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(_folder))
            {
                return;
            }

            var oldest = _time.GetUtcNow().UtcDateTime - Lifetime;
            foreach (var file in new DirectoryInfo(_folder).EnumerateFiles("*.json"))
            {
                if (file.LastWriteTimeUtc < oldest)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again next run.
        }
    }

    /// <summary>The file for a song: a hash of what was asked, so names never reach the file system.</summary>
    internal string PathFor(LyricsQuery query)
    {
        var seconds = Math.Round(query.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{query.Artist}\n{query.Title}\n{query.Album}\n{seconds}"));
        return Path.Combine(_folder, Convert.ToHexStringLower(hash, 0, 16) + ".json");
    }
}

internal sealed class CachedLyrics
{
    public DateTimeOffset Saved { get; set; }

    public bool Found { get; set; }

    public bool Synced { get; set; }

    public bool Instrumental { get; set; }

    public List<CachedLine>? Lines { get; set; }
}

internal sealed class CachedLine
{
    /// <summary>Milliseconds from the start of the song.</summary>
    public long? At { get; set; }

    public string? Text { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LrcLibRecord))]
[JsonSerializable(typeof(List<LrcLibRecord>))]
[JsonSerializable(typeof(CachedLyrics))]
internal sealed partial class LyricsJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.LocalFiles;

/// <summary>A music file in Local Files, as the index keeps it between launches.</summary>
public sealed record LocalFile
{
    public string Path { get; init; } = string.Empty;

    public long Size { get; init; }

    /// <summary>The file's last change (UTC ticks); with <see cref="Size"/>, tells whether it changed since its tags were read.</summary>
    public long LastWriteTicks { get; init; }

    /// <summary>When Resonate first saw the file, or when it was put in its folder if that was earlier.</summary>
    public DateTimeOffset AddedAt { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Artist { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Album { get; init; }

    public int? TrackNumber { get; init; }

    public int? DiscNumber { get; init; }

    public long DurationMs { get; init; }

    /// <summary>Where the embedded cover is (read when shown, never during a scan).</summary>
    public long? CoverOffset { get; init; }

    public int? CoverLength { get; init; }

    public string? CoverMime { get; init; }

    public CoverEncoding CoverEncoding { get; init; }

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    [JsonIgnore]
    public CoverRef? Cover => CoverOffset is { } offset && CoverLength is { } length
        ? new CoverRef(offset, length, CoverMime, CoverEncoding)
        : null;

    /// <summary>The song as lists show it: a <see cref="TrackInfo.FilePath"/> and a file: address, no Spotify links.</summary>
    public TrackInfo ToTrackInfo() =>
        new(
            UriFor(Path),
            Title,
            Artist ?? AlbumArtist ?? string.Empty,
            Album ?? string.Empty,
            AlbumUri: null,
            Duration,
            SmallImageUrl: null,
            LargeImageUrl: null,
            IsExplicit: false,
            IsPlayable: true)
        {
            FilePath = Path,
            AddedAt = AddedAt,
            TrackNumber = TrackNumber,
            DiscNumber = DiscNumber,
        };

    internal static LocalFile From(string path, long size, long lastWriteTicks, DateTimeOffset addedAt, AudioTags tags) => new()
    {
        Path = path,
        Size = size,
        LastWriteTicks = lastWriteTicks,
        AddedAt = addedAt,
        Title = tags.Title ?? System.IO.Path.GetFileNameWithoutExtension(path),
        Artist = tags.Artist,
        AlbumArtist = tags.AlbumArtist,
        Album = tags.Album,
        TrackNumber = tags.TrackNumber,
        DiscNumber = tags.DiscNumber,
        DurationMs = (long)tags.Duration.TotalMilliseconds,
        CoverOffset = tags.Cover?.Offset,
        CoverLength = tags.Cover?.Length,
        CoverMime = tags.Cover?.MimeType,
        CoverEncoding = tags.Cover?.Encoding ?? CoverEncoding.Raw,
    };

    /// <summary>The file: address of a path ("file:///C:/Music/a%20song.mp3"), which identifies the song in lists and the player.</summary>
    public static string UriFor(string path)
    {
        try
        {
            return new Uri(path).AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return "file:" + path;
        }
    }
}

/// <summary>The index file: every known music file, by path.</summary>
internal sealed class LocalIndex
{
    public int Version { get; set; }

    public List<LocalFile> Files { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LocalIndex))]
internal sealed partial class LocalFilesJsonContext : JsonSerializerContext;

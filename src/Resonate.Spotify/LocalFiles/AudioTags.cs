namespace Resonate.Spotify.LocalFiles;

/// <summary>How an embedded cover is stored in its file.</summary>
public enum CoverEncoding
{
    /// <summary>The picture's bytes, as they are.</summary>
    Raw,

    /// <summary>ID3 unsynchronisation: every 0xFF 0x00 in the file stands for 0xFF.</summary>
    Unsynchronised,

    /// <summary>Inside an Ogg comment (base64, split across pages): found again by reading the tags.</summary>
    InComment,
}

/// <summary>Where a file's embedded cover is, so it is read when shown instead of during a scan.</summary>
/// <param name="Offset">Where the picture starts in the file (not used for <see cref="CoverEncoding.InComment"/>).</param>
/// <param name="Length">Its size in the file.</param>
/// <param name="MimeType">"image/jpeg" or "image/png", when the file says.</param>
public sealed record CoverRef(long Offset, int Length, string? MimeType, CoverEncoding Encoding = CoverEncoding.Raw);

/// <summary>What a music file says about itself, read from its header only.</summary>
public sealed record AudioTags
{
    public static readonly AudioTags Empty = new();

    /// <summary>The title, or the file's name when it has none.</summary>
    public string? Title { get; init; }

    /// <summary>The artists, separated by ", " when there are several.</summary>
    public string? Artist { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Album { get; init; }

    public int? TrackNumber { get; init; }

    public int? DiscNumber { get; init; }

    /// <summary>Zero when the file does not tell.</summary>
    public TimeSpan Duration { get; init; }

    public CoverRef? Cover { get; init; }
}

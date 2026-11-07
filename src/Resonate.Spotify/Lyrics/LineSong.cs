using System.Text;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Lyrics;

/// <summary>One song's answer as the index file keeps it.</summary>
internal sealed class LineRecord
{
    public string Uri { get; set; } = string.Empty;

    public LineAnswer Answer { get; set; }

    public DateTimeOffset Checked { get; set; }

    public string? Title { get; set; }

    public string? Artists { get; set; }

    public string? Album { get; set; }

    public string? AlbumUri { get; set; }

    /// <summary>The cover at about 64 pixels.</summary>
    public string? Image { get; set; }

    /// <summary>The cover at about 300 pixels.</summary>
    public string? LargeImage { get; set; }

    public long DurationMs { get; set; }

    public List<string>? Lines { get; set; }

    /// <summary>When each line starts, in milliseconds; null for lyrics without timing.</summary>
    public List<long>? Times { get; set; }
}

/// <summary>
/// A song in memory, kept small: its lines folded for searching and as
/// written for showing, each as one block of UTF-8 with a line break
/// between lines.
/// </summary>
internal sealed class LineSong
{
    public required string Uri { get; init; }

    public required string Title { get; init; }

    public required string Artists { get; init; }

    public required string Album { get; init; }

    public string? AlbumUri { get; init; }

    public string? Image { get; init; }

    public string? LargeImage { get; init; }

    public long DurationMs { get; init; }

    /// <summary>The folded lines (see <see cref="LineText.Fold"/>), one after another with '\n' between.</summary>
    public required byte[] Folded { get; init; }

    /// <summary>Where each line starts in <see cref="Folded"/>.</summary>
    public required int[] Starts { get; init; }

    /// <summary>The lines as written, with '\n' between.</summary>
    public required byte[] Text { get; init; }

    /// <summary>When each line starts, in milliseconds; null for lyrics without timing.</summary>
    public long[]? Times { get; init; }

    public int LineCount => Starts.Length;

    public static LineSong? From(LineRecord record)
    {
        if (record.Lines is not { Count: > 0 } stored)
        {
            return null;
        }

        var lines = stored.Select(l => (l ?? string.Empty).ReplaceLineEndings(" ")).ToList();

        var times = record.Times is { } t && t.Count == lines.Count ? t : null;
        var kept = new List<int>(lines.Count);
        var folded = new StringBuilder();
        var starts = new List<int>(lines.Count);
        var foldedLength = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var fold = LineText.Fold(lines[i]);
            if (fold.Length == 0)
            {
                continue;
            }

            if (kept.Count > 0)
            {
                folded.Append('\n');
                foldedLength++;
            }

            starts.Add(foldedLength);
            folded.Append(fold);
            foldedLength += Encoding.UTF8.GetByteCount(fold);
            kept.Add(i);
        }

        if (kept.Count == 0)
        {
            return null;
        }

        return new LineSong
        {
            Uri = record.Uri,
            Title = record.Title ?? string.Empty,
            Artists = record.Artists ?? string.Empty,
            Album = record.Album ?? string.Empty,
            AlbumUri = record.AlbumUri,
            Image = record.Image,
            LargeImage = record.LargeImage,
            DurationMs = record.DurationMs,
            Folded = Encoding.UTF8.GetBytes(folded.ToString()),
            Starts = [.. starts],
            Text = Encoding.UTF8.GetBytes(string.Join('\n', kept.Select(i => lines[i]))),
            Times = times is null ? null : [.. kept.Select(i => times[i])],
        };
    }

    /// <summary>The lines as written.</summary>
    public string[] Lines() => Encoding.UTF8.GetString(Text).Split('\n');

    /// <summary>Where line <paramref name="line"/> ends in <see cref="Folded"/> (its last byte plus one).</summary>
    public int EndOf(int line) => line + 1 < Starts.Length ? Starts[line + 1] - 1 : Folded.Length;

    /// <summary>The line that holds byte <paramref name="position"/> of <see cref="Folded"/>.</summary>
    public int LineAt(int position)
    {
        var found = Array.BinarySearch(Starts, position);
        return found >= 0 ? found : ~found - 1;
    }

    public TrackInfo ToTrack() =>
        new(Uri, Title, Artists, Album, AlbumUri, TimeSpan.FromMilliseconds(DurationMs), Image, LargeImage, IsExplicit: false, IsPlayable: true)
        {
            Id = Uri.Split(':') is [_, _, { Length: > 0 } id] ? id : null,
            AlbumId = AlbumUri?.Split(':') is [_, _, { Length: > 0 } album] ? album : null,
        };

    public LineRecord ToRecord(DateTimeOffset checkedAt) => new()
    {
        Uri = Uri,
        Answer = LineAnswer.Words,
        Checked = checkedAt,
        Title = Title,
        Artists = Artists,
        Album = Album,
        AlbumUri = AlbumUri,
        Image = Image,
        LargeImage = LargeImage,
        DurationMs = DurationMs,
        Lines = [.. Lines()],
        Times = Times is null ? null : [.. Times],
    };
}

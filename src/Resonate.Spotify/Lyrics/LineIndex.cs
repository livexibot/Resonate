using System.Text;
using System.Text.Json;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Lyrics;

/// <summary>
/// A song found by a line of its words: the line sung, the ones around it
/// (null at the start or end of the song), when it starts (null for lyrics
/// without timing) and where the words typed are in it.
/// </summary>
public sealed record LineMatch(TrackInfo Track, string Line, string? Before, string? After, TimeSpan? At, IReadOnlyList<LineMark> Marks);

/// <summary>What the lyrics library answered for a song.</summary>
public enum LineAnswer
{
    /// <summary>No words known (yet): looked up again after <see cref="LineIndex.RecheckAfter"/>.</summary>
    None = 0,

    Words = 1,

    Instrumental = 2,
}

/// <summary>
/// Line Finder's index: the words of the user's songs, kept on this PC, so
/// a half-remembered line finds the song and where it is sung without asking
/// anyone. LRCLIB's own search only matches titles and artists, so the words
/// have to be here. One line of JSON per song in a file that only grows
/// (a crash loses at most the song being written); words found are kept for
/// good, and a song without any is looked up again after a month. Safe to
/// use from any thread; reading and writing the file is best effort.
/// </summary>
public sealed class LineIndex
{
    /// <summary>A song with no words found is looked up again after this long (someone may have written them down since).</summary>
    public static readonly TimeSpan RecheckAfter = LyricsCache.Lifetime;

    /// <summary>The most songs a search returns.</summary>
    public const int DefaultLimit = 30;

    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (LineAnswer Answer, DateTimeOffset Checked)> _answers = new(StringComparer.Ordinal);
    private readonly List<LineSong> _songs = [];
    private readonly Dictionary<string, int> _songAt = new(StringComparer.Ordinal);
    private bool _loaded;
    private int _stale;

    /// <param name="path">The file to keep the index in; null keeps it in memory only (demo mode, tests).</param>
    public LineIndex(string? path, TimeProvider? time = null)
    {
        _path = path;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Songs whose words are known.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _songs.Count;
            }
        }
    }

    /// <summary>Reads the file once (later calls do nothing). Reads the disk: call off the interface thread.</summary>
    public void Load()
    {
        lock (_gate)
        {
            LoadLocked();
        }
    }

    /// <summary>The song has an answer that needs no new lookup: words, instrumental, or "none" from the last month.</summary>
    public bool Knows(string uri)
    {
        lock (_gate)
        {
            LoadLocked();
            return _answers.TryGetValue(uri, out var known)
                && (known.Answer != LineAnswer.None || _time.GetUtcNow() - known.Checked < RecheckAfter);
        }
    }

    /// <summary>Keeps the lyrics library's answer for a song (null: none). Writes the disk: call off the interface thread.</summary>
    public void Add(TrackInfo track, SongLyrics? lyrics)
    {
        if (track.Uri is not { } uri)
        {
            return;
        }

        var record = new LineRecord { Uri = uri, Checked = _time.GetUtcNow() };
        if (lyrics is { IsInstrumental: true })
        {
            record.Answer = LineAnswer.Instrumental;
        }
        else if (lyrics is not null)
        {
            // Empty lines (a break in the singing) and lines of only symbols are left out.
            var lines = lyrics.Lines.Where(l => LineText.Fold(l.Text).Length > 0).ToList();
            if (lines.Count > 0)
            {
                record.Answer = LineAnswer.Words;
                record.Title = track.Title;
                record.Artists = track.Artists;
                record.Album = track.Album;
                record.AlbumUri = track.AlbumUri;
                record.Image = track.SmallImageUrl;
                record.LargeImage = track.LargeImageUrl;
                record.DurationMs = (long)track.Duration.TotalMilliseconds;
                record.Lines = [.. lines.Select(l => OneLine(l.Text))];
                record.Times = lyrics.IsSynced ? [.. lines.Select(l => (long)l.At!.Value.TotalMilliseconds)] : null;
            }
        }

        lock (_gate)
        {
            LoadLocked();
            Apply(record);
            Append(record);
        }
    }

    /// <summary>
    /// The songs with a line that fits what was typed, best first, one line
    /// each (the earliest of equally good ones, so a chorus starts where it
    /// is first sung). Runs through every song: call off the interface thread.
    /// </summary>
    public IReadOnlyList<LineMatch> Search(string query, int limit, CancellationToken cancellationToken)
    {
        if (LineQuery.Parse(query) is not { } parsed || limit <= 0)
        {
            return [];
        }

        LineSong[] songs;
        lock (_gate)
        {
            LoadLocked();
            songs = [.. _songs];
        }

        var scorer = new LineScorer(parsed);
        var found = new List<(LineSong Song, int Line, double Score)>();
        for (var i = 0; i < songs.Length; i++)
        {
            if ((i & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (scorer.Best(songs[i]) is { } best)
            {
                found.Add((songs[i], best.Line, best.Score));
            }
        }

        // Best first; equally good ones in the index's order (recent plays first).
        var ranked = found.Select((f, order) => (f.Song, f.Line, f.Score, order))
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.order);

        var results = new List<LineMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (song, line, _, _) in ranked)
        {
            // The same song on an album and a single: the line shows once.
            var lines = song.Lines();
            if (!seen.Add(LineText.Fold(song.Title) + "\n" + LineText.Fold(song.Artists) + "\n" + LineText.Fold(lines[line])))
            {
                continue;
            }

            results.Add(new LineMatch(
                song.ToTrack(),
                lines[line],
                line > 0 ? lines[line - 1] : null,
                line + 1 < lines.Length ? lines[line + 1] : null,
                song.Times is { } times ? TimeSpan.FromMilliseconds(times[line]) : null,
                LineText.Marks(lines[line], query)));
            if (results.Count == limit)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Lets go of the index in memory (the plugin was turned off); the file stays for next time.</summary>
    public void Unload()
    {
        lock (_gate)
        {
            _answers.Clear();
            _songs.Clear();
            _songAt.Clear();
            _stale = 0;
            _loaded = false;
        }
    }

    /// <summary>Forgets every song and deletes the file (signing out: the next account starts afresh).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _answers.Clear();
            _songs.Clear();
            _songAt.Clear();
            _stale = 0;
            _loaded = true;
            if (_path is not null)
            {
                try
                {
                    File.Delete(_path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort.
                }
            }
        }
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private void LoadLocked()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(_path))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                LineRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize(line, LyricsJsonContext.Default.LineRecord);
                }
                catch (JsonException)
                {
                    // Half a line from a crash: that song is looked up again.
                    _stale++;
                    continue;
                }

                if (record is { Uri.Length: > 0 })
                {
                    Apply(record);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What was read so far stays; the rest is looked up again.
        }

        // Answers replaced by newer ones pile up in a file that only grows: write it afresh now and then.
        if (_stale > 100 && _stale > _answers.Count / 5)
        {
            Rewrite();
        }
    }

    /// <summary>Takes in one answer. Call under the lock.</summary>
    private void Apply(LineRecord record)
    {
        if (_answers.ContainsKey(record.Uri))
        {
            _stale++;
        }

        _answers[record.Uri] = (record.Answer, record.Checked);
        if (record.Answer != LineAnswer.Words || LineSong.From(record) is not { } song)
        {
            return;
        }

        if (_songAt.TryGetValue(record.Uri, out var at))
        {
            _songs[at] = song;
        }
        else
        {
            _songAt[record.Uri] = _songs.Count;
            _songs.Add(song);
        }
    }

    /// <summary>Adds an answer to the end of the file. Call under the lock.</summary>
    private void Append(LineRecord record)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, JsonSerializer.Serialize(record, LyricsJsonContext.Default.LineRecord) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this run; looked up again next time.
        }
    }

    /// <summary>Writes the file again with one line per song. Call under the lock.</summary>
    private void Rewrite()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            var temporary = _path + ".tmp";
            using (var writer = new StreamWriter(temporary, append: false, new UTF8Encoding(false)))
            {
                foreach (var (uri, known) in _answers)
                {
                    var record = _songAt.TryGetValue(uri, out var at)
                        ? _songs[at].ToRecord(known.Checked)
                        : new LineRecord { Uri = uri, Answer = known.Answer, Checked = known.Checked };
                    writer.Write(JsonSerializer.Serialize(record, LyricsJsonContext.Default.LineRecord));
                    writer.Write('\n');
                }
            }

            File.Move(temporary, _path, overwrite: true);
            _stale = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again next run.
        }
    }
}

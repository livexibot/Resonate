using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Resonate.Spotify.Lyrics;

/// <summary>Where lyrics come from.</summary>
public interface ILyricsSource
{
    /// <summary>
    /// The words of the song, or null when the source has none. Throws when
    /// the source cannot be reached or answers with an error.
    /// </summary>
    Task<SongLyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// LRCLIB (https://lrclib.net), a free library of plain and synced (LRC)
/// lyrics with no account or key. An exact lookup by artist, title, album
/// and length comes first, then a search whose results are ranked; the
/// song's length is the strongest sign of which recording it is. Ported
/// from spotifast (src/lyrics.rs, MIT licence, copyright Carmine Paolino),
/// without its Spotify transcription path, which needs Spotify's private
/// playback session.
/// </summary>
public sealed class LrcLibClient : ILyricsSource
{
    public static readonly Uri DefaultBaseUri = new("https://lrclib.net/api/");

    /// <summary>A result this far from the song's length is another recording, however well the names match.</summary>
    private const double MaxDriftSeconds = 30;

    private readonly HttpClient _http;
    private readonly string? _userAgent;
    private readonly Uri _baseUri;

    /// <param name="userAgent">Sent with every request, as LRCLIB asks apps to name themselves.</param>
    public LrcLibClient(HttpClient http, string? userAgent = null, Uri? baseUri = null)
    {
        _http = http;
        _userAgent = userAgent;
        _baseUri = baseUri ?? DefaultBaseUri;
    }

    /// <summary>The User-Agent LRCLIB asks for: the app, its version and where it lives.</summary>
    public static string UserAgentFor(string version) => $"Resonate/{version} (https://github.com/livexibot/Resonate)";

    public async Task<SongLyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        var title = LyricsText.CleanTitle(query.Title);
        var artist = LyricsText.CleanArtist(query.Artist);
        if (title.Length == 0 || artist.Length == 0)
        {
            return null;
        }

        var exact = new List<(string, string)> { ("artist_name", artist), ("track_name", title) };
        if (query.Album.Length > 0)
        {
            exact.Add(("album_name", query.Album));
        }

        if (query.Duration > TimeSpan.Zero)
        {
            exact.Add(("duration", Math.Round(query.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
        }

        var record = await GetAsync("get", exact, LyricsJsonContext.Default.LrcLibRecord, cancellationToken).ConfigureAwait(false);
        if (record?.ToLyrics() is { } found)
        {
            return found;
        }

        var candidates = await GetAsync(
            "search",
            [("artist_name", artist), ("track_name", title)],
            LyricsJsonContext.Default.ListLrcLibRecord,
            cancellationToken).ConfigureAwait(false);
        return Pick(candidates ?? [], query)?.ToLyrics();
    }

    /// <summary>The search result that fits the song best, or null when none is close enough.</summary>
    internal static LrcLibRecord? Pick(IEnumerable<LrcLibRecord?> records, LyricsQuery query)
    {
        LrcLibRecord? best = null;
        long bestScore = long.MinValue;
        foreach (var record in records)
        {
            if (record is not null && Score(record, query) is { } score && score > bestScore)
            {
                best = record;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>How well a search result fits the song; null rules it out.</summary>
    internal static long? Score(LrcLibRecord record, LyricsQuery query)
    {
        if (!LyricsText.LooseMatch(record.TrackName, LyricsText.CleanTitle(query.Title)))
        {
            return null;
        }

        long score = 0;
        if (LyricsText.LooseMatch(record.ArtistName, LyricsText.CleanArtist(query.Artist)))
        {
            score += 1000;
        }

        if (query.Duration > TimeSpan.Zero && record.Duration is double length && length > 0)
        {
            var drift = Math.Abs(length - query.Duration.TotalSeconds);
            if (drift > MaxDriftSeconds)
            {
                return null;
            }

            score += (long)((MaxDriftSeconds - drift) * 10);
        }

        // Timing is the point, so a synced upload wins a tie.
        if (record.HasSynced)
        {
            score += 200;
        }
        else if (record.HasPlain)
        {
            score += 50;
        }

        return score;
    }

    /// <summary>
    /// One LRCLIB request. A 404 is "nobody has transcribed this" and a 400
    /// "that is not a question I can answer"; neither is a fault worth showing.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, IEnumerable<(string Name, string Value)> parameters, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        var queryString = string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, $"{path}?{queryString}"));
        request.Headers.Accept.ParseAdd("application/json");
        if (_userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"LRCLIB answered {(int)response.StatusCode}.", inner: null, response.StatusCode);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>One song in LRCLIB's answers.</summary>
internal sealed class LrcLibRecord
{
    public string? TrackName { get; set; }

    public string? ArtistName { get; set; }

    /// <summary>In seconds.</summary>
    public double? Duration { get; set; }

    public bool? Instrumental { get; set; }

    public string? SyncedLyrics { get; set; }

    public string? PlainLyrics { get; set; }

    public bool HasSynced => !string.IsNullOrWhiteSpace(SyncedLyrics);

    public bool HasPlain => !string.IsNullOrWhiteSpace(PlainLyrics);

    /// <summary>Synced words when there are any, else plain ones; instrumental when LRCLIB says so; null for an empty record.</summary>
    public SongLyrics? ToLyrics()
    {
        if (Instrumental == true)
        {
            return SongLyrics.Instrumental;
        }

        if (HasSynced && LyricsText.ParseLrc(SyncedLyrics!) is { Count: > 0 } lines)
        {
            return new SongLyrics(lines, synced: true);
        }

        if (!HasPlain)
        {
            return null;
        }

        var plain = PlainLyrics!.Split('\n').Select(line => new LyricLine(null, line.TrimEnd())).ToList();
        return new SongLyrics(plain, synced: false);
    }
}

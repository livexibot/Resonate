using Resonate.Spotify.Lyrics;

namespace Resonate.App.Demo;

/// <summary>
/// Made-up synced words for the demo's made-up songs, so the lyrics pane
/// has something to follow in "--demo" (and on CI) without the network.
/// The same song always gets the same words.
/// </summary>
public sealed class DemoLyrics : ILyricsSource
{
    private static readonly string[] Lines =
    [
        "Streetlights hum a quiet tune",
        "We drive until the morning comes",
        "Every window holds a different sky",
        "I kept your letters in the glovebox",
        "The radio knows all our names",
        "Hold on, the night is young",
        "Paper boats on a silver river",
        "Counting stars like they were ours",
        "Turn it up, the city's sleeping",
        "Somewhere a satellite is listening",
        "We were golden for a while",
        "Echoes running down the hallway",
        "Say it slow, say it twice",
        "All the roads lead back to you",
        "Static on the line again",
        "Neon fading into blue",
    ];

    public Task<SongLyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        var seed = StableHash(query.Artist + "\n" + query.Title);
        var length = query.Duration > TimeSpan.Zero ? query.Duration : TimeSpan.FromMinutes(3);
        var lines = new List<LyricLine>();
        var at = TimeSpan.FromSeconds(6 + (seed % 5));
        var first = seed % Lines.Length;
        for (var n = 0; at < length - TimeSpan.FromSeconds(8); n++)
        {
            // The first line comes back like a chorus, and now and then the band plays on without words.
            var text = n % 9 == 8 ? string.Empty : Lines[n % 4 == 3 ? first : (first + n) % Lines.Length];
            lines.Add(new LyricLine(at, text));
            at += TimeSpan.FromSeconds(3.5 + (((seed % 4) + n) % 4));
        }

        return Task.FromResult<SongLyrics?>(new SongLyrics(lines, synced: true));
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash & 0x7FFFFFFF;
    }
}

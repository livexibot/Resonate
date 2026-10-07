using System.Security.Cryptography;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Truly random order: an unbiased Fisher–Yates shuffle driven by the
/// operating system's cryptographic random numbers. Every order is equally
/// likely; nothing is weighted by popularity, listening history or artist,
/// unlike Spotify's own shuffle.
/// </summary>
public static class TrueShuffle
{
    /// <summary>Returns <paramref name="items"/> in a random order.</summary>
    /// <param name="nextInt">A random integer in [0, max); the system's cryptographic generator when null (tests pass their own).</param>
    public static List<T> Shuffle<T>(IEnumerable<T> items, Func<int, int>? nextInt = null)
    {
        nextInt ??= RandomNumberGenerator.GetInt32;
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = nextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>A random order that starts with <paramref name="first"/> (the song the user picked), then the rest shuffled.</summary>
    public static List<T> ShuffleAfter<T>(IReadOnlyList<T> items, int firstIndex, Func<int, int>? nextInt = null)
    {
        if (firstIndex < 0 || firstIndex >= items.Count)
        {
            return Shuffle(items, nextInt);
        }

        var rest = items.Where((_, i) => i != firstIndex);
        var result = new List<T>(items.Count) { items[firstIndex] };
        result.AddRange(Shuffle(rest, nextInt));
        return result;
    }
}

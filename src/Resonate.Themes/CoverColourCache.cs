namespace Resonate.Themes;

/// <summary>
/// The colour each cover gives the glow behind a page's header (see
/// <see cref="ThemePalette.HeroTint"/>), remembered for the covers seen last
/// (by their address), so going back to a page shows its colour at once,
/// without reading the cover again. Safe to use from any thread.
/// </summary>
public sealed class CoverColourCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, ThemeColor Colour)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, ThemeColor Colour)> _recent = new();
    private readonly Lock _lock = new();

    public CoverColourCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// The colour of a cover for a header: its lively colour, or its average
    /// colour when it is black and white (or nearly empty). Pixels are 8-bit
    /// BGRA rows with no padding, as from <see cref="ArtworkColors"/>.
    /// </summary>
    public static ThemeColor HeaderColour(ReadOnlySpan<byte> bgra, int width, int height) =>
        ArtworkColors.PickAccent(bgra, width, height) ?? ArtworkColors.Average(bgra, width, height);

    /// <summary>The colour remembered for <paramref name="key"/>, which then counts as just seen.</summary>
    public bool TryGet(string key, out ThemeColor colour)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                colour = node.Value.Colour;
                return true;
            }
        }

        colour = default;
        return false;
    }

    /// <summary>Remembers a colour, forgetting the one seen longest ago when full.</summary>
    public void Remember(string key, ThemeColor colour)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                node.Value = (key, colour);
                _recent.AddFirst(node);
                return;
            }

            _entries[key] = _recent.AddFirst((key, colour));
            if (_entries.Count > _capacity && _recent.Last is { } oldest)
            {
                _recent.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
        }
    }
}

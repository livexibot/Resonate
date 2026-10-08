using System.Globalization;
using Resonate.App.Services;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages.Lists;

/// <summary>
/// A daily mix from Home ("mix:1" to "mix:6"). Resonate makes it on this
/// computer, so it has no Spotify playlist behind it: it plays as
/// Resonate's own list of songs.
/// </summary>
public sealed class DailyMixSource : TrackListSource
{
    public const string Prefix = "mix:";

    /// <summary>A music note, for a mix without its artist's picture.</summary>
    public const string Glyph = "";

    private readonly AppServices _services;
    private readonly int _number;

    public DailyMixSource(AppServices services, int number)
    {
        _services = services;
        _number = number;
    }

    public static string KeyFor(int number) => Prefix + number.ToString(CultureInfo.InvariantCulture);

    public override string Key => KeyFor(_number);

    public override bool HasDateAdded => false;

    public override string OwnOrderName => "Mix order";

    public override string EmptyText => "This mix is not here today. Mixes are made each day from your Liked Songs; go back to Home to see today's.";

    public override ListHeader CachedHeader => Header(Find(_services.Home.Content));

    public override async Task<ListHeader?> LoadHeaderAsync(CancellationToken cancellationToken) =>
        Header(Find(await _services.Home.GetContentAsync(cancellationToken)));

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        new(Find(await _services.Home.GetContentAsync(cancellationToken))?.Tracks ?? [], ItemsHidden: false);

    private DailyMix? Find(HomeContent? content) => content?.Mixes.FirstOrDefault(m => m.Number == _number);

    private ListHeader Header(DailyMix? mix)
    {
        var title = mix?.Title ?? "Daily Mix " + _number.ToString(CultureInfo.CurrentCulture);
        var user = _services.Library.Snapshot?.User?.DisplayName;
        var details = string.IsNullOrEmpty(user) ? "Made for you" : "Made for " + user;
        return new ListHeader("DAILY MIX", title, mix?.Subtitle, details, mix?.ImageUrl, title, mix?.ImageUrl is null ? Glyph : null);
    }
}

/// <summary>"On repeat": the songs played most over the last four weeks, as Spotify counts them.</summary>
public sealed class OnRepeatSource : TrackListSource
{
    public const string ListKey = "onrepeat";

    /// <summary>The repeat symbol.</summary>
    public const string Glyph = "";

    private readonly AppServices _services;

    public OnRepeatSource(AppServices services) => _services = services;

    public override string Key => ListKey;

    public override bool HasDateAdded => false;

    public override string OwnOrderName => "Most played";

    public override string EmptyText => "The songs you play most over a few weeks appear here.";

    public override ListHeader CachedHeader =>
        new("MADE FOR YOU", "On repeat", null, null, null, "On repeat", Glyph);

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        new((await _services.Home.GetContentAsync(cancellationToken)).OnRepeat, ItemsHidden: false);
}

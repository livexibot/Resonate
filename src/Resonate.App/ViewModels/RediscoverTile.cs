using Microsoft.UI.Xaml;
using Resonate.App.Helpers;
using Resonate.Spotify.History;

namespace Resonate.App.ViewModels;

/// <summary>
/// One card in Rediscover's row on Home (a built-in plugin), drawn by
/// <c>RediscoverCardTemplate</c> in HomePage.xaml like the other rows' cards:
/// the cover, a label saying why it is here, the title, the artists and a
/// note, with a play button under the pointer for songs.
/// </summary>
public sealed partial class RediscoverTile(RediscoverCard card)
{
    private const int CoverWidth = 176;

    private CoverTile? _cover;

    public RediscoverCard Card { get; } = card;

    /// <summary>"ON THIS DAY", "GATHERING DUST" or "DEEP CUTS".</summary>
    public string Label => Card.Label;

    public string Title => Card.Title;

    public string Subtitle => Card.Subtitle;

    /// <summary>Why it is here: "Liked 3 years ago today".</summary>
    public string Note => Card.Note;

    public string Tooltip => $"{Card.Title} · {Card.Subtitle}\n{Card.Note}";

    /// <summary>The cover, with no colour tile while it loads (the album's tile only when there is none).</summary>
    public CoverTile Cover => _cover ??= new CoverTile(Card.ImageUrl, CoverWidth, Artwork.PlaceholderBrush(Card.Title));

    /// <summary>Songs play from the card; albums open.</summary>
    public Visibility PlayVisibility => Card.Track is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Card.Label}: {Card.Title}, {Card.Subtitle}, {Card.Note}";
}

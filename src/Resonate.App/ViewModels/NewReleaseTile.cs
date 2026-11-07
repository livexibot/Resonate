using Resonate.App.Helpers;
using Resonate.Spotify.Library;

namespace Resonate.App.ViewModels;

/// <summary>
/// One card in New releases on Home, drawn by <c>NewReleaseCardTemplate</c>
/// in HomePage.xaml like the other rows' cards: the cover, the release, its
/// artists, and what it is and when it came out ("Single · 3 days ago"). A
/// click opens it; the play button under the pointer plays it.
/// </summary>
public sealed partial class NewReleaseTile(NewRelease release, string caption)
{
    private const int CoverWidth = 176;

    private CoverTile? _cover;

    public NewRelease Release { get; } = release;

    public string Title => Release.Name;

    public string Subtitle => Release.Artists;

    /// <summary>"Single · 3 days ago".</summary>
    public string Caption { get; } = caption;

    public string Tooltip => $"{Release.Name} · {Release.Artists}\n{Caption}";

    /// <summary>The cover, with no colour tile while it loads (the release's tile only when there is none).</summary>
    public CoverTile Cover => _cover ??= new CoverTile(Release.ImageUrl, CoverWidth, Artwork.PlaceholderBrush(Release.Name));

    /// <summary>What screen readers say for the play button.</summary>
    public string PlayName => "Play " + Release.Name;

    /// <summary>The play button: the release from its first song, without opening it.</summary>
    public void Play()
    {
        if (Release.Uri is { Length: > 0 } uri)
        {
            _ = App.Services.Player.PlayContextAsync(uri);
        }
    }

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Release.Name}, {Release.Artists}, {Caption}";
}

using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>
/// The classic player's visualiser data. Only the local files player feeds
/// it (Resonate's own audio graph, the user's music files); Spotify's sound
/// is never heard or analysed, so for Spotify songs it stays at rest.
/// </summary>
public sealed class VisualiserFeed
{
    public SpectrumAnalyser Analyser { get; } = new();

    /// <summary>
    /// A visualiser is showing and wants data. While false, the local files
    /// engine detaches its tap and nothing is analysed.
    /// </summary>
    public bool Wanted { get; set; }

    /// <summary>A local file is playing, so the analyser has sound to show; false for Spotify songs.</summary>
    public bool IsLive { get; private set; }

    /// <summary>Raised on the interface thread when <see cref="IsLive"/> changes.</summary>
    public event EventHandler? LiveChanged;

    private void SetLive(bool live)
    {
        if (IsLive != live)
        {
            IsLive = live;
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

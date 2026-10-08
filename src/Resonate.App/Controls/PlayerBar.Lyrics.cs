using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Resonate.App.Controls;

/// <summary>
/// The lyrics button, shown while the Lyrics plugin is on (see
/// MainWindow.Lyrics.cs): it opens and closes the lyrics pane, and is lit
/// while the pane is open.
/// </summary>
public sealed partial class PlayerBar
{
    private bool _lyricsAttached;

    /// <summary>Raised when the lyrics button is clicked; the window opens or closes the lyrics pane.</summary>
    public event EventHandler? LyricsRequested;

    /// <summary>Wires the lyrics button (once).</summary>
    public void AttachLyrics()
    {
        if (!_lyricsAttached)
        {
            _lyricsAttached = true;
            LyricsButton.Click += (_, _) => LyricsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows the lyrics button while the plugin is on, lit while the pane is open.</summary>
    public void ShowLyricsButton(bool shown, bool open)
    {
        LyricsButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(LyricsButton, open ? "Hide lyrics" : "Lyrics");
        if (open)
        {
            LyricsButton.Foreground = App.Services.Theme.GetBrush("ResonateAccentBrush");
        }
        else
        {
            LyricsButton.ClearValue(Control.ForegroundProperty);
        }
    }
}

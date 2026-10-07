namespace Resonate.Themes.Skins;

/// <summary>What the classic player's lamp shows.</summary>
public enum ClassicPlayState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>What the visualiser draws. Clicking it moves to the next one, as in Winamp.</summary>
public enum VisualiserMode
{
    Spectrum,
    Oscilloscope,
    Off,
}

/// <summary>A part of the classic player that reacts to the pointer.</summary>
public enum ClassicControl
{
    None,

    // The title bar
    TitleBar,
    Options,
    Minimize,
    Shade,
    Close,

    // The clutter bar letters
    ClutterOptions,
    ClutterAlwaysOnTop,
    ClutterInfo,
    ClutterDoubleSize,
    ClutterVisualiser,

    // Displays
    Time,
    Visualiser,
    Marquee,

    // Transport
    Previous,
    Play,
    Pause,
    Stop,
    Next,
    Eject,

    // Toggles
    Shuffle,
    Repeat,
    Equalizer,
    Playlist,

    // Sliders
    Volume,
    Balance,
    Seek,

    /// <summary>The skin's logo corner (Winamp's About).</summary>
    About,
}

/// <summary>
/// Everything the classic player's main window shows at one moment. The
/// renderer turns it into pixels; the control builds a new one whenever the
/// player, the pointer or the clock changes something.
/// </summary>
public sealed record ClassicView
{
    public ClassicPlayState State { get; init; } = ClassicPlayState.Stopped;

    /// <summary>A command is on its way (the lamp's work indicator).</summary>
    public bool Working { get; init; }

    public TimeSpan Elapsed { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>The time display counts down (with a minus sign) instead of up.</summary>
    public bool ShowRemaining { get; init; }

    /// <summary>False during the "off" half of the paused blink: the digits are hidden.</summary>
    public bool BlinkOn { get; init; } = true;

    /// <summary>The song's line, e.g. "Artist - Title (3:45)"; scrolled by <see cref="MarqueeOffset"/>.</summary>
    public string Marquee { get; init; } = string.Empty;

    /// <summary>How many characters the marquee has scrolled (see <see cref="Skins.Marquee"/>).</summary>
    public int MarqueeOffset { get; init; }

    /// <summary>Up to three characters, or empty.</summary>
    public string Kbps { get; init; } = string.Empty;

    /// <summary>Up to two characters, or empty.</summary>
    public string Khz { get; init; } = string.Empty;

    /// <summary>Lights the stereo lamp (otherwise the mono lamp when <see cref="Mono"/>, or neither).</summary>
    public bool Stereo { get; init; }

    public bool Mono { get; init; }

    /// <summary>0 to 1.</summary>
    public double Volume { get; init; } = 1;

    /// <summary>-1 (left) to 1 (right); 0 is the centre.</summary>
    public double Balance { get; init; }

    /// <summary>0 to 1 through the song, or null when the song cannot be sought (the thumb is hidden).</summary>
    public double? Seek { get; init; }

    public bool Shuffle { get; init; }

    public bool Repeat { get; init; }

    /// <summary>The EQ toggle is lit (the equaliser is on).</summary>
    public bool EqualizerOn { get; init; }

    /// <summary>The PL toggle is lit (the queue is open).</summary>
    public bool PlaylistOn { get; init; }

    /// <summary>The clutter bar's D is lit (double size).</summary>
    public bool DoubleSize { get; init; }

    /// <summary>The control under a pressed pointer, drawn pressed.</summary>
    public ClassicControl Pressed { get; init; }

    /// <summary>The window has focus: the title bar is drawn active.</summary>
    public bool WindowActive { get; init; } = true;

    /// <summary>Shade mode: only the 275x14 bar.</summary>
    public bool Shaded { get; init; }

    public VisualiserMode Visualiser { get; init; } = VisualiserMode.Spectrum;
}

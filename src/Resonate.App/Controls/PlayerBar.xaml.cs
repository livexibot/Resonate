using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Plugins;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>How the player bar is laid out: a bar, the minimal player in the sidebar, or the controls of the column beside the page.</summary>
internal enum PlayerBarMode
{
    Bar,
    Sidebar,
    Column,
}

/// <summary>
/// Now playing, play and pause, skip, seek and volume. Every control acts on
/// the player at once (the player is optimistic), so the bar never waits for
/// Spotify. The look decides the bar's shape, its progress bar and how the
/// cover is drawn; the user decides whether the cover spins; the width it
/// is given decides how much of it shows.
/// </summary>
public sealed partial class PlayerBar : UserControl
{
    private const string PlayGlyph = "\uF5B0";
    private const string PauseGlyph = "\uF8AE";
    private const string VolumeGlyph = "\uE767";
    private const string MutedGlyph = "\uE74F";
    private const string RepeatAllGlyph = "\uE8EE";
    private const string RepeatOneGlyph = "\uE8ED";
    private const double VolumeWheelStep = 0.05;

    // The clock moves the progress bar on about one screen pixel a tick: at
    // least four ticks a second, so the time never lags a second change by
    // more than a quarter of a second, and at most thirty (a short song on a
    // wide bar then moves a few pixels a tick).
    private static readonly TimeSpan SlowestTick = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FastestTick = TimeSpan.FromSeconds(1.0 / 30);

    private readonly DispatcherQueueTimer _clock;
    private readonly CoverSpin _spin;
    private PlayerRouter? _player;
    private PlayerState _shown = PlayerState.Empty;
    private bool _windowShown = true;
    private bool _settingValues;
    private int _updateQueued;
    private double _volumeBeforeMute = 0.5;
    private object? _artworkKey;
    private PluginManager? _plugins;
    private int _pluginsQueued;
    private string? _positionLabel;
    private string? _durationLabel;
    private PlayerWidthClass _widthClass = PlayerWidthClass.Full;

    /// <summary>A bar, the minimal player at the foot of the sidebar, or the controls of the column beside the page (see <see cref="ShowMode"/>).</summary>
    private PlayerBarMode _mode;

    /// <summary>The height a player in the sidebar, which is as tall as what it shows, rounds its corners for.</summary>
    private const double SidebarCornerHeight = 96;

    /// <summary>The width of the column beside the page, which decides how its controls are laid out (see <see cref="FitColumn"/>).</summary>
    private double _columnWidth = PlayerPlacement.SideWidth;

    /// <summary>Below this the column beside the page is a rail: everything in one line down the middle.</summary>
    internal const double ColumnRailBelow = 160;

    /// <summary>Below this the column beside the page tightens its buttons and puts the volume over them.</summary>
    private const double ColumnNarrowBelow = 300;
    private bool _lyricsShown;
    private (bool On, string? Line, string? Next, string? Note, string? After) _lyricArgs;
    private Brush? _sungLyricBrush;
    private float _artworkSize = 56;

    public PlayerBar()
    {
        InitializeComponent();

        // The playing song's artists open their pages.
        SongLinks.Attach(ArtistText, SongLinks.Artists, PlayingTrack.Get);

        // Fade covers in instead of popping them (runs on the compositor).
        ArtworkImage.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };

        // The whole frame turns, so the cover, its tile and the record's centre move together.
        _spin = new CoverSpin(ArtworkFrame);

        // A drag only moves the bar; the seek is sent when it is let go.
        PositionBar.DragCompleted += OnSeekDragCompleted;

        App.Services.Theme.Changed += (_, _) => ApplyLook();
        ApplyLook();

        _clock = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clock.Interval = SlowestTick;
        _clock.Tick += (_, _) => UpdateClock();
    }

    /// <summary>
    /// Lyrics in the player. While it is on: "Song · Artist" on one line, and
    /// under it the line being sung (on two lines when it needs them) and the
    /// next one, or a word when there are none ("No lyrics"). Off: the song
    /// above its artists. The mini bar has no room for lyrics.
    /// </summary>
    public void ShowLyrics(bool on, string? line = null, string? next = null, string? note = null, string? after = null)
    {
        _lyricArgs = (on, line, next, note, after);
        on &= _widthClass != PlayerWidthClass.Mini;

        // Before the first sung line (or in a pause) the coming line moves up
        // right under the song, dimmed, so no empty line sits between them.
        var singing = !string.IsNullOrWhiteSpace(line);
        var shown = singing ? line : !string.IsNullOrWhiteSpace(next) ? next : note;
        LyricLineText.Text = shown ?? string.Empty;
        _sungLyricBrush ??= LyricLineText.Foreground;
        LyricLineText.Foreground = singing ? _sungLyricBrush : NextLyricText.Foreground;

        // Under the line sung, the next; before the first, the coming line and the one after it.
        NextLyricText.Text = (singing ? next : shown == next ? after : null) ?? string.Empty;
        LyricLineText.Visibility = on && !string.IsNullOrEmpty(shown) ? Visibility.Visible : Visibility.Collapsed;
        NextLyricText.Visibility = on && !string.IsNullOrWhiteSpace(NextLyricText.Text) ? Visibility.Visible : Visibility.Collapsed;
        ShowSongDot();
        if (on == _lyricsShown)
        {
            return;
        }

        var lyrics = on;
        _lyricsShown = lyrics;
        Grid.SetColumnSpan(TitleText, lyrics ? 1 : 3);
        TitleText.FontWeight = lyrics ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold;
        ShowSongDot();
        Grid.SetRow(ArtistText, lyrics ? 0 : 1);
        Grid.SetColumn(ArtistText, lyrics ? 2 : 0);
        Grid.SetColumnSpan(ArtistText, lyrics ? 1 : 3);
        foreach (var text in (TextBlock[])[TitleText, ArtistText])
        {
            if (lyrics)
            {
                text.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            else
            {
                text.ClearValue(TextBlock.TextTrimmingProperty);
            }
        }
    }

    /// <summary>The dot between the song and its artists, while lyrics show and there are artists to name.</summary>
    private void ShowSongDot() =>
        SongDotText.Visibility = _lyricsShown && ArtistText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// A sung line on two lines leaves the next one a single line, so the
    /// three lines together stay inside the bar.
    /// </summary>
    private void OnLyricLineSizeChanged(object sender, SizeChangedEventArgs e) =>
        NextLyricText.MaxLines = e.NewSize.Height > LyricLineText.FontSize * 1.9 ? 1 : 2;

    /// <summary>The window says when it is minimised or hidden, so the clock can rest.</summary>
    public void SetWindowShown(bool shown)
    {
        _windowShown = shown;
        UpdateClock();
        RunClockWhenNeeded();
        RunBarVisualizer();
        UpdateAdvancing();
        UpdateSpin();
    }

    /// <summary>
    /// The clock only moves while music plays and the window can be seen;
    /// otherwise it would wake the app four times a second for nothing.
    /// </summary>
    private void RunClockWhenNeeded()
    {
        if (!_shown.IsPlaying || !_windowShown)
        {
            _clock.Stop();
        }
        else if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    /// <summary>Raised when the queue button is clicked; the window shows the queue.</summary>
    public event EventHandler? QueueRequested;

    public void Attach(PlayerRouter player)
    {
        _player = player;
        player.StateChanged += OnStateChanged;
        App.Services.ControlChannelChanged += (_, _) => ShowDevice(_shown);
        Show(player.State);
    }

    /// <summary>Shows the plugin button while a plugin that is on offers commands.</summary>
    public void AttachPlugins(PluginManager plugins)
    {
        _plugins = plugins;
        plugins.Changed += (_, _) =>
        {
            if (Interlocked.Exchange(ref _pluginsQueued, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref _pluginsQueued, 0);
                    ShowPlugins();
                });
            }
        };
        ShowPlugins();
    }

    private void ShowPlugins()
    {
        if (_plugins is { } plugins)
        {
            PluginMenu.UpdateButton(PluginsButton, plugins);
        }
    }

    private void OnPluginsClick(object sender, RoutedEventArgs e)
    {
        if (_plugins is { } plugins)
        {
            var menu = PluginMenu.Build(plugins);
            menu.Placement = FlyoutPlacementMode.TopEdgeAlignedRight;
            menu.ShowAt(PluginsButton);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; draw the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            if (_player is not null)
            {
                Show(_player.State);
            }
        });
    }

    private void Show(PlayerState state)
    {
        var before = _shown;
        _shown = state;
        if (PositionBar.IsDragging && (!state.CanSeek || !IsSameSong(state, before)))
        {
            // The song changed under the pointer; the clock below shows where the new one is.
            PositionBar.CancelDrag();
        }

        ShowSongChange(state);
        AnimateSongChange(state);
        if (state.IsPlaying != before.IsPlaying)
        {
            RunBarVisualizer();
        }
        TitleText.Text = state.Title ?? "Nothing playing";
        ArtistText.Text = state.Artists ?? (state.IsConnected ? string.Empty : "Pick a song to start");
        ShowSongDot();

        PlayPauseButton.Content = state.IsPlaying ? PauseGlyph : PlayGlyph;
        AutomationPropertiesHelper.SetName(PlayPauseButton, state.IsPlaying ? "Pause" : "Play");

        _settingValues = true;
        try
        {
            PositionBar.IsEnabled = state.CanSeek;
            if (!VolumeBar.IsDragging)
            {
                VolumeBar.Value = Math.Round(state.Volume * 100);
            }

            MuteButton.Content = state.Volume <= 0.001 ? MutedGlyph : VolumeGlyph;
        }
        finally
        {
            _settingValues = false;
        }

        ShowModes(state);
        ShowDevice(state);
        ShowArtwork(state);
        UpdateClock();
        RunClockWhenNeeded();
        UpdateAdvancing();
        UpdateSpin();
    }

    /// <summary>The bar itself, without its margins, for a special look's decorations that sit on it.</summary>
    internal FrameworkElement Surface => Bar;

    /// <summary>How round the bar's corners are.</summary>
    internal double SurfaceCorner => Bar.CornerRadius.TopLeft;

    /// <summary>The look's progress bar, cover style and corners.</summary>
    private void ApplyLook()
    {
        UpdateBarVisualizer();
        var theme = App.Services.Theme;
        var look = theme.Current;
        PositionBar.BarStyle = look.Progress;
        PositionBar.Glow = look.ProgressGlow;

        // Volume does not play: the moving styles become the plain line there.
        VolumeBar.BarStyle = ProgressPatterns.ForVolume(look.Progress);

        // Settings, Layout, Advanced: the user's own height.
        Bar.Height = BarHeightFor(_widthClass);

        // A pill when it hovers in a look with round buttons: the corners
        // follow the bar's height, which is lower for the mini bar. In the
        // column beside the page the controls lie on the column's own panel.
        var column = _mode == PlayerBarMode.Column;
        var corner = new CornerRadius(column ? 0 : PlayerPlacement.Corner(look.PlayerLayout, look.Buttons, theme.Palette.CornerLarge, double.IsNaN(Bar.Height) ? SidebarCornerHeight : Bar.Height));
        Bar.CornerRadius = corner;
        BarHost.CornerRadius = corner;
        Bar.Background = column ? theme.GetBrush("ResonateTransparentBrush") : theme.GetBrush("ResonatePlayerBrush");
        Bar.BorderThickness = column ? new Thickness(0) : PlayerPlacement.Outline(look.PlayerLayout, theme.Palette.BorderWidth).ToThickness();
        BarHost.Level = column ? ElevationLevel.Flat : ElevationLevel.Player;

        // The mini bar keeps to the page's corner; a pill no wider than its slot sits in the middle.
        BarHost.HorizontalAlignment = look.PlayerLayout switch
        {
            PlayerLayout.Corner => HorizontalAlignment.Right,
            PlayerLayout.CornerLeft => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Stretch,
        };

        // Settings, Layout, Advanced: the user's own width and height. In the sidebar, the sidebar's width.
        BarHost.MaxWidth = _mode != PlayerBarMode.Bar ? double.PositiveInfinity : look.PlayerWidth ?? PlayerPlacement.MaxWidth(look.PlayerLayout);

        // A record for the vinyl style, and for every look while the user
        // lets covers spin; otherwise the look's own shape.
        var record = theme.CoverIsRecord;
        var artworkCorner = new CornerRadius(record
            ? _artworkSize / 2
            : look.Cover == CoverStyle.Square ? 0 : theme.Palette.CornerMedium);
        ArtworkFrame.CornerRadius = artworkCorner;
        ArtworkShadow.CornerRadius = artworkCorner;
        VinylCentre.Visibility = record ? Visibility.Visible : Visibility.Collapsed;
        UpdateSpin();
        ArrangeSide();
    }

    /// <summary>
    /// The buttons and the volume on the right: in one row, or the buttons
    /// (smaller) in a row over the speaker and the slider, both lined up on
    /// the right, which leaves the middle more room: on a full bar while the
    /// user wants it, and always on a narrower one (the owner's choice,
    /// 9 October 2026, instead of leaving the slider out). The mini bar keeps
    /// only the queue.
    /// </summary>
    private void ArrangeSide()
    {
        var column = _mode == PlayerBarMode.Column;
        ArrangeTransport(column);
        if (column)
        {
            ArrangeColumnSide();
            return;
        }

        var stacked = _widthClass == PlayerWidthClass.Compact || (App.Services.Theme.ButtonsAboveVolume && _widthClass == PlayerWidthClass.Full);
        SideArea.ColumnDefinitions[0].Width = GridLength.Auto;
        SideArea.HorizontalAlignment = HorizontalAlignment.Right;
        SideButtons.Orientation = Orientation.Horizontal;
        VolumeControls.Orientation = Orientation.Horizontal;
        SideButtons.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(SideButtons, 0);
        Grid.SetColumn(SideButtons, 0);
        Grid.SetRow(VolumeControls, stacked ? 1 : 0);
        Grid.SetColumn(VolumeControls, stacked ? 0 : 1);
        VolumeControls.HorizontalAlignment = HorizontalAlignment.Right;
        SideArea.ColumnSpacing = stacked ? 0 : 4;
        SideArea.RowSpacing = stacked ? 2 : 0;
        SideButtons.Spacing = stacked ? 6 : 4;
        SizeSideButtons(stacked ? 32.0 : 36.0);

        // Room for the slider row alone, rather than for every button beside it.
        if (_widthClass != PlayerWidthClass.Mini)
        {
            VolumeColumn.MinWidth = !stacked ? 272 : _widthClass == PlayerWidthClass.Full ? 180 : 156;
        }
    }

    private void SizeSideButtons(double size)
    {
        foreach (var button in new[] { PluginsButton, DeviceButton, LyricsButton, QueueButton, MuteButton })
        {
            button.Width = size;
            button.Height = size;
        }
    }

    /// <summary>
    /// The width of the column beside the page (as the user drags it): wide,
    /// the controls as they are; narrow, smaller buttons closer together and
    /// the volume over the buttons; a rail, everything in one line down the
    /// middle (the owner's request, 10 October 2026: "just move controls to
    /// make it fit nicely").
    /// </summary>
    internal void FitColumn(double width)
    {
        if (Math.Abs(width - _columnWidth) < 0.5)
        {
            return;
        }

        _columnWidth = width;
        if (_mode == PlayerBarMode.Column)
        {
            ShowWidthClass(PlayerWidthClass.Mini, force: true);
        }
    }

    private bool ColumnIsRail => _columnWidth < ColumnRailBelow;

    private bool ColumnIsNarrow => _columnWidth < ColumnNarrowBelow;

    /// <summary>
    /// Shuffle, previous, play, next and repeat: in a row as the look has
    /// them, smaller and closer together in a narrow column, one under the
    /// other in a rail.
    /// </summary>
    private void ArrangeTransport(bool column)
    {
        var rail = column && ColumnIsRail;
        var narrow = column && ColumnIsNarrow;
        TransportButtons.Orientation = rail ? Orientation.Vertical : Orientation.Horizontal;
        foreach (var button in new Control[] { ShuffleButton, PreviousButton, NextButton, RepeatButton })
        {
            if (narrow && !rail)
            {
                button.Width = 32;
                button.Height = 32;
            }
            else
            {
                button.ClearValue(WidthProperty);
                button.ClearValue(HeightProperty);
            }
        }

        if (!column)
        {
            return;
        }

        // The inside of the column, less its padding; four buttons and the play button share it.
        var inside = _columnWidth - Bar.Padding.Left - Bar.Padding.Right;
        TransportButtons.Spacing = rail ? 4 : narrow ? Math.Clamp(Math.Floor((inside - 168) / 4), 0, 14) : 14;
        PositionText.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        DurationText.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The speaker, the volume and the buttons in the column beside the page:
    /// wide, the speaker and the slider on the left and the buttons on the
    /// right; narrow, the slider across the column over the buttons; a rail,
    /// the buttons and the speaker one under the other (its wheel sets the
    /// volume).
    /// </summary>
    private void ArrangeColumnSide()
    {
        var rail = ColumnIsRail;
        var narrow = ColumnIsNarrow;
        var inside = _columnWidth - Bar.Padding.Left - Bar.Padding.Right;
        SideArea.HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        SideArea.ColumnDefinitions[0].Width = rail ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        SideButtons.Orientation = rail ? Orientation.Vertical : Orientation.Horizontal;
        VolumeControls.Orientation = Orientation.Horizontal;
        SideButtons.HorizontalAlignment = rail || narrow ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        VolumeControls.HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // Wide: one row. Narrow: the slider over the buttons. Rail: the buttons over the speaker.
        Grid.SetRow(VolumeControls, rail ? 1 : 0);
        Grid.SetColumn(VolumeControls, 0);
        Grid.SetRow(SideButtons, narrow && !rail ? 1 : 0);
        Grid.SetColumn(SideButtons, narrow ? 0 : 1);
        SideArea.ColumnSpacing = narrow ? 0 : 8;
        SideArea.RowSpacing = rail ? 4 : narrow ? 6 : 0;
        SideButtons.Spacing = rail ? 4 : narrow ? 10 : 4;
        SizeSideButtons(narrow ? 32.0 : 36.0);
        VolumeBar.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        VolumeBar.Width = narrow ? Math.Max(60, inside - 32 - VolumeControls.Spacing) : 120;
    }

    /// <summary>The bar's width comes from the window, never from what it shows, so changing what it shows cannot change the width back.</summary>
    private void OnBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 1)
        {
            ShowWidthClass(_mode != PlayerBarMode.Bar ? PlayerWidthClass.Mini : PlayerPlacement.WidthClassFor(e.NewSize.Width));
        }
    }

    /// <summary>The user's own height, else the class's; in the sidebar or the column the bar is as tall as what it shows.</summary>
    private double BarHeightFor(PlayerWidthClass widthClass) => _mode switch
    {
        PlayerBarMode.Column => double.NaN,
        PlayerBarMode.Sidebar => App.Services.Theme.Current.PlayerHeight ?? double.NaN,
        _ => App.Services.Theme.Current.PlayerHeight ?? PlayerPlacement.HeightFor(widthClass),
    };

    /// <summary>
    /// The bar's layout. The minimal player at the foot of the sidebar
    /// (Settings, Player, Placement, Sidebar; the owner's request,
    /// 10 October 2026): the mini bar stacked, the cover and the song over
    /// previous, play, next and the progress, as wide as the sidebar, without
    /// the plugins, devices, queue and volume buttons. The column beside the
    /// page (Left or Right; the column shows the cover and the song above):
    /// the progress with its times under it, shuffle, previous, play, next
    /// and repeat, then the speaker, the volume and the buttons, all on the
    /// column's panel.
    /// </summary>
    internal void ShowMode(PlayerBarMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        var stacked = mode != PlayerBarMode.Bar;
        var column = mode == PlayerBarMode.Column;
        Bar.RowDefinitions.Clear();
        if (stacked)
        {
            Bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        NowPlayingArea.Visibility = column ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(NowPlayingArea, stacked ? 3 : 1);
        Grid.SetRow(Transport, mode == PlayerBarMode.Sidebar ? 1 : 0);
        Grid.SetColumn(Transport, stacked ? 0 : 1);
        Grid.SetColumnSpan(Transport, stacked ? 3 : 1);
        Grid.SetRow(SideArea, column ? 1 : 0);
        Grid.SetColumn(SideArea, stacked ? 0 : 2);
        Grid.SetColumnSpan(SideArea, stacked ? 3 : 1);
        SideArea.HorizontalAlignment = column ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        SideArea.Visibility = mode == PlayerBarMode.Sidebar ? Visibility.Collapsed : Visibility.Visible;

        // The progress over the buttons, its times under its two ends.
        Grid.SetRow(TransportButtons, column ? 1 : 0);
        Grid.SetRow(SeekRow, column ? 0 : 1);
        SeekRow.RowDefinitions.Clear();
        if (column)
        {
            SeekRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SeekRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(PositionBar, 0);
        Grid.SetColumn(PositionBar, column ? 0 : 1);
        Grid.SetColumnSpan(PositionBar, column ? 3 : 1);
        Grid.SetRow(PositionText, column ? 1 : 0);
        Grid.SetRow(DurationText, column ? 1 : 0);
        PositionText.TextAlignment = column ? TextAlignment.Left : TextAlignment.Right;
        DurationText.TextAlignment = column ? TextAlignment.Right : TextAlignment.Left;
        ShowWidthClass(stacked || Bar.ActualWidth < 1 ? PlayerWidthClass.Mini : PlayerPlacement.WidthClassFor(Bar.ActualWidth), force: true);
    }

    /// <summary>
    /// Everything on a wide bar; without the volume slider (the wheel on the
    /// speaker button still sets it) and with tighter spacing when it is
    /// narrower; and a shorter mini bar with the song, previous, play, next,
    /// the progress and the queue when it is narrower still. Only a change of
    /// class touches the layout, never each pixel of a resize.
    /// </summary>
    private void ShowWidthClass(PlayerWidthClass widthClass, bool force = false)
    {
        if (widthClass == _widthClass && !force)
        {
            return;
        }

        _widthClass = widthClass;
        var full = widthClass == PlayerWidthClass.Full;
        var mini = widthClass == PlayerWidthClass.Mini;
        var stacked = _mode != PlayerBarMode.Bar;
        var column = _mode == PlayerBarMode.Column;

        // The column beside the page has room for everything, the mini bar does not.
        var shown = mini && !column ? Visibility.Collapsed : Visibility.Visible;

        Bar.Height = BarHeightFor(widthClass);
        var padding = full ? 20 : mini ? 14 : 16;
        Bar.Padding = _mode switch
        {
            PlayerBarMode.Sidebar => new Thickness(12, 12, 12, 8),
            PlayerBarMode.Column => ColumnIsNarrow ? new Thickness(12, 4, 12, 12) : new Thickness(20, 4, 20, 12),
            _ => new Thickness(padding, 0, padding, 0),
        };
        Bar.ColumnSpacing = stacked ? 0 : full ? 24 : mini ? 12 : 16;
        Bar.RowSpacing = _mode switch
        {
            PlayerBarMode.Sidebar => 8,
            PlayerBarMode.Column => 14,
            _ => 0,
        };

        // Full and compact keep the controls in the middle; the mini bar gives the song what is left; in the sidebar, the song and the controls each take a row of their own.
        NowPlayingColumn.Width = new GridLength(mini ? 1 : 3, GridUnitType.Star);
        NowPlayingColumn.MinWidth = full ? 220 : mini ? 0 : 150;
        ControlsColumn.Width = stacked ? new GridLength(0) : mini ? GridLength.Auto : new GridLength(4, GridUnitType.Star);
        ControlsColumn.MinWidth = full ? 320 : mini ? 0 : 216;
        VolumeColumn.Width = stacked ? new GridLength(0) : mini ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        // Room for all four buttons (plugins, device, queue, speaker) and, in full, the slider.
        VolumeColumn.MinWidth = full ? 272 : mini ? 0 : 156;

        TransportButtons.Spacing = full || column ? 14 : 8;
        Transport.RowSpacing = _mode switch
        {
            PlayerBarMode.Sidebar => 4,
            PlayerBarMode.Column => 12,
            _ => mini ? 0 : 2,
        };
        ShuffleButton.Visibility = shown;
        RepeatButton.Visibility = shown;
        PositionText.Visibility = shown;
        DurationText.Visibility = shown;
        PositionColumn.Width = mini && !column ? new GridLength(0) : GridLength.Auto;
        DurationColumn.Width = mini && !column ? new GridLength(0) : GridLength.Auto;
        SeekRow.ColumnSpacing = column ? 0 : mini ? 0 : 10;
        SeekRow.RowSpacing = column ? 2 : 0;
        SeekRow.MinWidth = mini && !stacked ? 150 : 0;
        MuteButton.Visibility = shown;

        // Narrower, the buttons go above the volume rather than the slider going away.
        VolumeBar.Visibility = shown;
        VolumeBar.Width = column ? 120 : full ? 112 : 96;

        // The mini bar has no room for lyrics; a wider one shows them again.
        var (lyricsOn, line, next, note, after) = _lyricArgs;
        ShowLyrics(lyricsOn, line, next, note, after);

        _artworkSize = mini ? 48 : 56;
        NowPlaying.ColumnSpacing = mini ? 10 : 14;
        ArtworkColumn.Width = new GridLength(_artworkSize);
        ArtworkFrame.Width = _artworkSize;
        ArtworkFrame.Height = _artworkSize;
        ApplyLook();
    }

    /// <summary>Whether a spinning cover turns right now: while the shown song plays and the window can be seen.</summary>
    private bool CoverMoving => _shown.IsPlaying && _windowShown;

    /// <summary>
    /// The cover turns like a record only when the user switched Spinning
    /// cover on (and Windows allows animations), and stops where it is when
    /// the music does. Without the switch nothing turns, not even vinyl.
    /// </summary>
    private void UpdateSpin() => _spin.Update(App.Services.Theme.CoverMaySpin, CoverMoving, _artworkSize);

    /// <summary>Shuffle and repeat are lit (the look's toggle style) while on.</summary>
    private void ShowModes(PlayerState state)
    {
        ShuffleButton.IsEnabled = state.CanShuffle;
        ShowShuffle(state.Shuffle);
        RepeatButton.IsEnabled = state.CanRepeat;
        ShowRepeat(state.Repeat);
    }

    private void ShowShuffle(bool on)
    {
        ShuffleButton.IsChecked = on;
        AutomationPropertiesHelper.SetName(ShuffleButton, on ? "Shuffle on" : "Shuffle off");
    }

    private void ShowRepeat(RepeatMode mode)
    {
        RepeatButton.IsChecked = mode != RepeatMode.Off;
        RepeatButton.Content = mode == RepeatMode.One ? RepeatOneGlyph : RepeatAllGlyph;
        AutomationPropertiesHelper.SetName(RepeatButton, mode switch
        {
            RepeatMode.All => "Repeat all",
            RepeatMode.One => "Repeat this song",
            _ => "Repeat off",
        });
    }

    private void ShowArtwork(PlayerState state)
    {
        // The cover's address or bytes, or, without one, the album's tile.
        object key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? "tile:" + (state.Album ?? state.Title);
        if (ReferenceEquals(key, _artworkKey) || Equals(key, _artworkKey))
        {
            return;
        }

        _artworkKey = key;
        ArtworkImage.Opacity = 0;

        // Until (or unless) a cover arrives, a colour tile for the album.
        var name = state.Album ?? state.Title;
        ArtworkFrame.Background = name is null
            ? App.Services.Theme.GetBrush("ResonateSurfaceHoverBrush")
            : Artwork.PlaceholderBrush(name);
        ArtworkGlyph.Visibility = name is null ? Visibility.Visible : Visibility.Collapsed;

        if (state.ArtworkUrl is { } url)
        {
            _ = ShowCoverAsync(key, App.Services.Covers.GetReadyAsync(url, 56));
        }
        else if (state.ArtworkBytes is { } bytes)
        {
            _ = ShowCoverAsync(key, CoverImages.FromBytesAsync(bytes, 56));
        }
        else
        {
            ArtworkImage.Source = null;
        }
    }

    private async Task ShowCoverAsync(object key, Task<(ImageSource? Image, bool Loaded)> loading)
    {
        var (image, loaded) = await loading;
        if (!ReferenceEquals(_artworkKey, key))
        {
            return;
        }

        ArtworkImage.Source = image;

        // A picture that already has its pixels may not raise ImageOpened, so it is shown here.
        if (loaded)
        {
            OnArtworkOpened(ArtworkImage, new RoutedEventArgs());
        }
    }

    private void OnArtworkOpened(object sender, RoutedEventArgs e)
    {
        ArtworkImage.Opacity = 1;
        ArtworkGlyph.Visibility = Visibility.Collapsed;
    }

    private void UpdateClock()
    {
        var position = _shown.PositionAt(DateTimeOffset.UtcNow);
        var duration = _shown.Duration;

        // Text is only set when it changes; the clock ticks far more often than the seconds do.
        var durationLabel = duration > TimeSpan.Zero ? Format.Duration(duration) : "-:--";
        if (durationLabel != _durationLabel)
        {
            _durationLabel = durationLabel;
            DurationText.Text = durationLabel;
        }

        if (PositionBar.IsDragging)
        {
            return;
        }

        var positionLabel = Format.Duration(position);
        if (positionLabel != _positionLabel)
        {
            _positionLabel = positionLabel;
            PositionText.Text = positionLabel;
        }

        _settingValues = true;
        try
        {
            PositionBar.Maximum = Math.Max(1, duration.TotalSeconds);
            PositionBar.Value = Math.Min(position.TotalSeconds, PositionBar.Maximum);
        }
        finally
        {
            _settingValues = false;
        }

        PaceClock();
    }

    /// <summary>One tick for each pixel the bar moves (the bar's values are seconds).</summary>
    private void PaceClock()
    {
        // Without a length (the DJ talking) the bar does not move; only the time does.
        var seconds = _shown.Duration > TimeSpan.Zero ? PositionBar.ValuePerPixel : 0;
        var interval = seconds > 0
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, FastestTick.TotalSeconds, SlowestTick.TotalSeconds))
            : SlowestTick;
        if (Math.Abs((interval - _clock.Interval).TotalMilliseconds) < 2)
        {
            return;
        }

        var running = _clock.IsRunning;
        _clock.Stop();
        _clock.Interval = interval;
        if (running)
        {
            _clock.Start();
        }
    }

    /// <summary>The wave style rolls while a song plays, and rests while the window is minimised.</summary>
    private void UpdateAdvancing() =>
        PositionBar.IsAdvancing = _shown.IsPlaying && _shown.Duration > TimeSpan.Zero && _windowShown;

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _ = _player?.TogglePlayPauseAsync();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _ = _player?.PreviousAsync();

    private void OnNextClick(object sender, RoutedEventArgs e) => _ = _player?.NextAsync();

    // A toggle button flips itself on click; these show the player's next state instead.
    private void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        var on = !_shown.Shuffle;
        ShowShuffle(on);
        _ = _player?.SetShuffleAsync(on);
    }

    private void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        var mode = MainWindow.NextRepeat(_shown.Repeat);
        ShowRepeat(mode);
        _ = _player?.SetRepeatAsync(mode);
    }

    private void OnQueueClick(object sender, RoutedEventArgs e) => QueueRequested?.Invoke(this, EventArgs.Empty);

    // The song's title opens what it plays from; underlined under the pointer when it can.
    private void OnTitleTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (!_swipeMoved)
        {
            App.MainWindow?.OpenNowPlaying();
        }
    }

    private void OnTitlePointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (App.MainWindow?.CanOpenNowPlaying(_shown) == true)
        {
            TitleText.TextDecorations = global::Windows.UI.Text.TextDecorations.Underline;
        }
    }

    private void OnTitlePointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        TitleText.TextDecorations = global::Windows.UI.Text.TextDecorations.None;

    // By title: the local channel and the Web API may spell the artists differently for the same song.
    private static bool IsSameSong(PlayerState a, PlayerState b) =>
        a.Source == b.Source && string.Equals(a.Title, b.Title, StringComparison.Ordinal);

    private void OnSeekDragCompleted(object? sender, EventArgs e) =>
        _ = _player?.SeekAsync(TimeSpan.FromSeconds(PositionBar.Value));

    private void OnSeekValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingValues)
        {
            return;
        }

        if (PositionBar.IsDragging)
        {
            // While dragging, only the label follows; the seek is sent on release.
            _positionLabel = Format.Duration(TimeSpan.FromSeconds(e.NewValue));
            PositionText.Text = _positionLabel;
            return;
        }

        // Keyboard arrows on the focused seek bar.
        _ = _player?.SeekAsync(TimeSpan.FromSeconds(e.NewValue));
    }

    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingValues)
        {
            return;
        }

        _ = _player?.SetVolumeAsync(e.NewValue / 100);
    }

    /// <summary>The mouse wheel over the speaker button sets the volume, a step a notch (the slider may be left out).</summary>
    private void OnMuteWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(MuteButton).Properties.MouseWheelDelta;
        if (_player is null || delta == 0)
        {
            return;
        }

        e.Handled = true;
        var volume = Math.Clamp(_player.State.Volume + (Math.Sign(delta) * VolumeWheelStep), 0, 1);
        _ = _player.SetVolumeAsync(Math.Round(volume, 2));
    }

    private void OnMuteClick(object sender, RoutedEventArgs e) => ToggleMute();

    /// <summary>Mutes, or puts the volume back to where it was before.</summary>
    internal void ToggleMute()
    {
        if (_player is null)
        {
            return;
        }

        if (_shown.Volume > 0.001)
        {
            _volumeBeforeMute = _shown.Volume;
            _ = _player.SetVolumeAsync(0);
        }
        else
        {
            _ = _player.SetVolumeAsync(_volumeBeforeMute);
        }
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}

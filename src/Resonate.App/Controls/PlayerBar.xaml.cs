using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Plugins;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes;

namespace Resonate.App.Controls;

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
    private float _artworkSize = 56;

    public PlayerBar()
    {
        InitializeComponent();

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

    /// <summary>The window says when it is minimised or hidden, so the clock can rest.</summary>
    public void SetWindowShown(bool shown)
    {
        _windowShown = shown;
        UpdateClock();
        RunClockWhenNeeded();
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
        TitleText.Text = state.Title ?? "Nothing playing";
        ArtistText.Text = state.Artists ?? (state.IsConnected ? string.Empty : "Pick a song to start");

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

    /// <summary>The look's progress bar, cover style and corners.</summary>
    private void ApplyLook()
    {
        var theme = App.Services.Theme;
        var look = theme.Current;
        PositionBar.BarStyle = look.Progress;

        // A rolling wave makes no sense for volume; it gets the plain line.
        VolumeBar.BarStyle = look.Progress == ProgressStyle.Wave ? ProgressStyle.Line : look.Progress;

        // A pill when it hovers in a look with round buttons: the corners
        // follow the bar's height, which is lower for the mini bar.
        var corner = new CornerRadius(PlayerPlacement.Corner(look.PlayerLayout, look.Buttons, theme.Palette.CornerLarge, Bar.Height));
        Bar.CornerRadius = corner;
        BarHost.CornerRadius = corner;

        // The mini bar keeps to the page's corner; a pill no wider than its slot sits in the middle.
        BarHost.HorizontalAlignment = look.PlayerLayout == PlayerLayout.Corner ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;

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
    }

    /// <summary>The bar's width comes from the window, never from what it shows, so changing what it shows cannot change the width back.</summary>
    private void OnBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 1)
        {
            ShowWidthClass(PlayerPlacement.WidthClassFor(e.NewSize.Width));
        }
    }

    /// <summary>
    /// Everything on a wide bar; without the volume slider (the wheel on the
    /// speaker button still sets it) and with tighter spacing when it is
    /// narrower; and a shorter mini bar with the song, previous, play, next,
    /// the progress and the queue when it is narrower still. Only a change of
    /// class touches the layout, never each pixel of a resize.
    /// </summary>
    private void ShowWidthClass(PlayerWidthClass widthClass)
    {
        if (widthClass == _widthClass)
        {
            return;
        }

        _widthClass = widthClass;
        var full = widthClass == PlayerWidthClass.Full;
        var mini = widthClass == PlayerWidthClass.Mini;
        var shown = mini ? Visibility.Collapsed : Visibility.Visible;

        Bar.Height = PlayerPlacement.HeightFor(widthClass);
        var padding = full ? 20 : mini ? 14 : 16;
        Bar.Padding = new Thickness(padding, 0, padding, 0);
        Bar.ColumnSpacing = full ? 24 : mini ? 12 : 16;

        // Full and compact keep the controls in the middle; the mini bar gives the song what is left.
        NowPlayingColumn.Width = new GridLength(mini ? 1 : 3, GridUnitType.Star);
        NowPlayingColumn.MinWidth = full ? 220 : mini ? 0 : 150;
        ControlsColumn.Width = mini ? GridLength.Auto : new GridLength(4, GridUnitType.Star);
        ControlsColumn.MinWidth = full ? 320 : mini ? 0 : 216;
        VolumeColumn.Width = mini ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        // Room for all four buttons (plugins, device, queue, speaker) and, in full, the slider.
        VolumeColumn.MinWidth = full ? 272 : mini ? 0 : 156;

        TransportButtons.Spacing = full ? 14 : 8;
        Transport.Spacing = mini ? 0 : 2;
        ShuffleButton.Visibility = shown;
        RepeatButton.Visibility = shown;
        PositionText.Visibility = shown;
        DurationText.Visibility = shown;
        PositionColumn.Width = mini ? new GridLength(0) : GridLength.Auto;
        DurationColumn.Width = mini ? new GridLength(0) : GridLength.Auto;
        SeekRow.ColumnSpacing = mini ? 0 : 10;
        SeekRow.MinWidth = mini ? 150 : 0;
        MuteButton.Visibility = shown;
        VolumeBar.Visibility = full ? Visibility.Visible : Visibility.Collapsed;

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

    private void OnMuteClick(object sender, RoutedEventArgs e)
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

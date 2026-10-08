using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
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
/// cover is drawn.
/// </summary>
public sealed partial class PlayerBar : UserControl
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string VolumeGlyph = "";
    private const string MutedGlyph = "";
    private const string RepeatAllGlyph = "\uE8EE";
    private const string RepeatOneGlyph = "\uE8ED";
    private const string HeartGlyph = "\uEB51";
    private const string HeartFilledGlyph = "\uEB52";
    private const float ArtworkSize = 56;
    private static readonly TimeSpan VinylTurn = TimeSpan.FromSeconds(7);

    // The clock moves the progress bar on about one screen pixel a tick: at
    // least four ticks a second, so the time never lags a second change by
    // more than a quarter of a second, and at most thirty (a short song on a
    // wide bar then moves a few pixels a tick).
    private static readonly TimeSpan SlowestTick = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FastestTick = TimeSpan.FromSeconds(1.0 / 30);

    private readonly DispatcherQueueTimer _clock;
    private PlayerRouter? _player;
    private PlayerState _shown = PlayerState.Empty;
    private bool _windowShown = true;
    private bool _settingValues;
    private int _updateQueued;
    private CoverStyle? _coverStyle;
    private AnimationController? _vinylSpin;
    private double _volumeBeforeMute = 0.5;
    private object? _artworkKey;
    private PluginManager? _plugins;
    private int _pluginsQueued;
    private string? _positionLabel;
    private string? _durationLabel;

    public PlayerBar()
    {
        InitializeComponent();

        // Fade covers in instead of popping them (runs on the compositor).
        ArtworkImage.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };

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
        App.Services.Likes.Changed += (_, change) =>
        {
            if (change.Uri is null || change.Uri == _shown.TrackUri)
            {
                DispatcherQueue.TryEnqueue(() => ShowLike(_shown));
            }
        };
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
        var active = _plugins?.WithCommands() ?? [];
        PluginsButton.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // What plugins report (a sleep timer counting down) shows in the tooltip, and lights the button.
        var notes = active.Where(p => !string.IsNullOrEmpty(p.StatusText)).Select(p => $"{p.Manifest.Name}: {p.StatusText}").ToList();
        ToolTipService.SetToolTip(PluginsButton, notes.Count == 0 ? "Plugins" : string.Join(Environment.NewLine, notes));
        if (notes.Count == 0)
        {
            PluginsButton.ClearValue(ForegroundProperty);
        }
        else
        {
            PluginsButton.Foreground = App.Services.Theme.GetBrush("ResonateAccentBrush");
        }
    }

    private void OnPluginsClick(object sender, RoutedEventArgs e)
    {
        if (_plugins is not { } plugins)
        {
            return;
        }

        // Built when opened, so it always shows the plugins' latest commands.
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        var active = plugins.WithCommands();
        foreach (var plugin in active)
        {
            var items = active.Count == 1 ? menu.Items : AddGroup(menu, plugin.Manifest.Name);
            if (!string.IsNullOrEmpty(plugin.StatusText))
            {
                items.Add(new MenuFlyoutItem { Text = plugin.StatusText, IsEnabled = false });
                items.Add(new MenuFlyoutSeparator());
            }

            foreach (var command in plugin.Commands)
            {
                var item = new MenuFlyoutItem { Text = command.Title };
                var (id, commandId) = (plugin.Manifest.Id, command.Id);
                item.Click += (_, _) => plugins.Invoke(id, commandId);
                items.Add(item);
            }
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var settings = new MenuFlyoutItem { Text = "Plugin settings" };
        settings.Click += (_, _) => App.MainWindow?.OpenSettings();
        menu.Items.Add(settings);
        menu.ShowAt(PluginsButton);
    }

    private static IList<MenuFlyoutItemBase> AddGroup(MenuFlyout menu, string name)
    {
        var group = new MenuFlyoutSubItem { Text = name };
        menu.Items.Add(group);
        return group.Items;
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
        _shown = state;
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
        ShowLike(state);
        ShowArtwork(state);
        UpdateClock();
        RunClockWhenNeeded();
        UpdateAdvancing();
        UpdateVinylSpin();
    }

    /// <summary>The look's progress bar and cover style.</summary>
    private void ApplyLook()
    {
        var look = App.Services.Theme.Current;
        PositionBar.BarStyle = look.Progress;

        // A rolling wave makes no sense for volume; it gets the plain line.
        VolumeBar.BarStyle = look.Progress == ProgressStyle.Wave ? ProgressStyle.Line : look.Progress;

        var palette = App.Services.Theme.Palette;
        ArtworkFrame.CornerRadius = new CornerRadius(look.Cover switch
        {
            CoverStyle.Square => 0,
            CoverStyle.Vinyl => ArtworkSize / 2,
            _ => palette.CornerMedium,
        });

        if (_coverStyle != look.Cover)
        {
            _coverStyle = look.Cover;
            VinylCentre.Visibility = look.Cover == CoverStyle.Vinyl ? Visibility.Visible : Visibility.Collapsed;
            UpdateVinylSpin();
        }
    }

    /// <summary>A vinyl cover turns slowly while the song plays, and stops where it is when paused.</summary>
    private void UpdateVinylSpin()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ArtworkFrame);
        if (_coverStyle != CoverStyle.Vinyl || !App.Services.Theme.AnimationsEnabled)
        {
            if (_vinylSpin is not null)
            {
                _vinylSpin = null;
                visual.StopAnimation("RotationAngleInDegrees");
                visual.RotationAngleInDegrees = 0;
            }

            return;
        }

        if (_vinylSpin is null)
        {
            var spin = visual.Compositor.CreateScalarKeyFrameAnimation();
            spin.InsertKeyFrame(0, 0);
            spin.InsertKeyFrame(1, 360, visual.Compositor.CreateLinearEasingFunction());
            spin.Duration = VinylTurn;
            spin.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.CenterPoint = new Vector3(ArtworkSize / 2, ArtworkSize / 2, 0);
            visual.StartAnimation("RotationAngleInDegrees", spin);
            _vinylSpin = visual.TryGetAnimationController("RotationAngleInDegrees");
        }

        if (_shown.IsPlaying)
        {
            _vinylSpin?.Resume();
        }
        else
        {
            _vinylSpin?.Pause();
        }
    }

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

    /// <summary>The heart for the playing song (Spotify songs only).</summary>
    private void ShowLike(PlayerState state)
    {
        var canLike = state.TrackUri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true;
        LikeButton.Visibility = canLike ? Visibility.Visible : Visibility.Collapsed;
        if (!canLike)
        {
            return;
        }

        var liked = App.Services.Likes.IsLiked(state.TrackUri);
        LikeButton.Content = liked ? HeartFilledGlyph : HeartGlyph;
        LikeButton.Foreground = App.Services.Theme.GetBrush(liked ? "ResonateAccentBrush" : "ResonateTextSecondaryBrush");
        var label = liked ? "Remove from Liked Songs" : "Save to Liked Songs";
        AutomationPropertiesHelper.SetName(LikeButton, label);
        ToolTipService.SetToolTip(LikeButton, label);
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

    private void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (_shown.TrackUri is not { } uri)
        {
            return;
        }

        // The bar knows the song by its address; that is all liking needs.
        var track = new Resonate.Spotify.Library.TrackInfo(
            uri,
            _shown.Title ?? string.Empty,
            _shown.Artists ?? string.Empty,
            _shown.Album ?? string.Empty,
            null,
            _shown.Duration,
            _shown.ArtworkUrl,
            _shown.ArtworkUrl,
            false,
            true);
        _ = Pages.Lists.TrackActions.SetLikedAsync(track, !App.Services.Likes.IsLiked(uri));
    }

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

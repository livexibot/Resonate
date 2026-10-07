using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
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

    private readonly DispatcherQueueTimer _clock;
    private PlayerRouter? _player;
    private PlayerState _shown = PlayerState.Empty;
    private bool _settingValues;
    private int _updateQueued;
    private CoverStyle? _coverStyle;
    private AnimationController? _vinylSpin;
    private double _volumeBeforeMute = 0.5;
    private object? _artworkKey;

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
        _clock.Interval = TimeSpan.FromMilliseconds(250);
        _clock.Tick += (_, _) => UpdateClock();
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

        // The clock only moves while music plays; paused, it would wake the app four times a second for nothing.
        if (!state.IsPlaying)
        {
            _clock.Stop();
        }
        else if (!_clock.IsRunning)
        {
            _clock.Start();
        }

        PositionBar.IsAdvancing = state.IsPlaying && state.Duration > TimeSpan.Zero;
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
            ArtworkImage.Source = Artwork.FromUrl(url, 56);
        }
        else if (state.ArtworkBytes is { } bytes)
        {
            _ = LoadArtworkBytesAsync(bytes);
        }
        else
        {
            ArtworkImage.Source = null;
        }
    }

    private async Task LoadArtworkBytesAsync(byte[] bytes)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = 56, DecodePixelType = DecodePixelType.Logical };
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        catch (Exception)
        {
            return;
        }

        if (ReferenceEquals(_artworkKey, bytes))
        {
            ArtworkImage.Source = bitmap;
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

        DurationText.Text = duration > TimeSpan.Zero ? Format.Duration(duration) : "-:--";
        if (PositionBar.IsDragging)
        {
            return;
        }

        PositionText.Text = Format.Duration(position);
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
    }

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
            PositionText.Text = Format.Duration(TimeSpan.FromSeconds(e.NewValue));
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

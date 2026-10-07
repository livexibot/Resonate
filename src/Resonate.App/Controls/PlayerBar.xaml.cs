using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Controls;

/// <summary>
/// Now playing, play and pause, skip, seek and volume. Every control acts on
/// the player at once (the player is optimistic), so the bar never waits for
/// Spotify.
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

    private readonly DispatcherQueueTimer _clock;
    private PlayerRouter? _player;
    private PlayerState _shown = PlayerState.Empty;
    private bool _settingValues;
    private bool _seeking;
    private int _updateQueued;
    private double _volumeBeforeMute = 0.5;
    private object? _artworkKey;

    public PlayerBar()
    {
        InitializeComponent();

        // Fade covers in instead of popping them (runs on the compositor).
        ArtworkImage.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };

        // Tell a drag on the seek bar from the clock moving it.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

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
        _clock.Start();
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
            SeekSlider.IsEnabled = state.CanSeek;
            if (!_seeking)
            {
                VolumeSlider.Value = Math.Round(state.Volume * 100);
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
    }

    /// <summary>Shuffle and repeat light up in the accent colour while on.</summary>
    private void ShowModes(PlayerState state)
    {
        var theme = App.Services.Theme;
        ShuffleButton.IsEnabled = state.CanShuffle;
        ShuffleButton.Foreground = theme.GetBrush(state.Shuffle ? "ResonateAccentBrush" : "ResonateTextSecondaryBrush");
        AutomationPropertiesHelper.SetName(ShuffleButton, state.Shuffle ? "Shuffle on" : "Shuffle off");

        RepeatButton.IsEnabled = state.CanRepeat;
        RepeatButton.Content = state.Repeat == RepeatMode.One ? RepeatOneGlyph : RepeatAllGlyph;
        RepeatButton.Foreground = theme.GetBrush(state.Repeat == RepeatMode.Off ? "ResonateTextSecondaryBrush" : "ResonateAccentBrush");
        AutomationPropertiesHelper.SetName(RepeatButton, state.Repeat switch
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
        if (_seeking)
        {
            return;
        }

        PositionText.Text = Format.Duration(position);
        _settingValues = true;
        try
        {
            SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            SeekSlider.Value = Math.Min(position.TotalSeconds, SeekSlider.Maximum);
        }
        finally
        {
            _settingValues = false;
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _ = _player?.TogglePlayPauseAsync();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _ = _player?.PreviousAsync();

    private void OnNextClick(object sender, RoutedEventArgs e) => _ = _player?.NextAsync();

    private void OnShuffleClick(object sender, RoutedEventArgs e) => _ = _player?.SetShuffleAsync(!_shown.Shuffle);

    private void OnRepeatClick(object sender, RoutedEventArgs e) => _ = _player?.SetRepeatAsync(MainWindow.NextRepeat(_shown.Repeat));

    private void OnQueueClick(object sender, RoutedEventArgs e) => QueueRequested?.Invoke(this, EventArgs.Empty);

    private void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (_shown.TrackUri is not { } uri)
        {
            return;
        }

        // The bar knows the song by its address; that is all liking needs.
        var track = new Resonate.Spotify.Library.TrackInfo(uri, _shown.Title ?? string.Empty, _shown.Artists ?? string.Empty, _shown.Album ?? string.Empty, null, _shown.Duration, null, _shown.ArtworkUrl, false, true);
        _ = Pages.Lists.TrackActions.SetLikedAsync(track, !App.Services.Likes.IsLiked(uri));
    }

    private void OnSeekPointerPressed(object sender, PointerRoutedEventArgs e) => _seeking = true;

    private void OnSeekPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_seeking)
        {
            return;
        }

        _seeking = false;
        _ = _player?.SeekAsync(TimeSpan.FromSeconds(SeekSlider.Value));
    }

    private void OnSeekValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingValues)
        {
            return;
        }

        if (_seeking)
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

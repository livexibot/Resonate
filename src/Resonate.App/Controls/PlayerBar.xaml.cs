using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.Spotify.Playback;

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

    private readonly DispatcherQueueTimer _clock;
    private PlayerController? _player;
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

    public void Attach(PlayerController player)
    {
        _player = player;
        player.StateChanged += OnStateChanged;
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

        ShowArtwork(state);
        UpdateClock();
    }

    private void ShowArtwork(PlayerState state)
    {
        object? key = state.ArtworkUrl ?? (object?)state.ArtworkBytes;
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

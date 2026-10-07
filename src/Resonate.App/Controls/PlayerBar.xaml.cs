using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.Plugins;
using Resonate.Spotify.Playback;
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
    private const float ArtworkSize = 56;
    private static readonly TimeSpan VinylTurn = TimeSpan.FromSeconds(7);

    private readonly DispatcherQueueTimer _clock;
    private PlayerController? _player;
    private PlayerState _shown = PlayerState.Empty;
    private bool _settingValues;
    private int _updateQueued;
    private CoverStyle? _coverStyle;
    private AnimationController? _vinylSpin;
    private double _volumeBeforeMute = 0.5;
    private object? _artworkKey;
    private PluginManager? _plugins;
    private int _pluginsQueued;

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

    public void Attach(PlayerController player)
    {
        _player = player;
        player.StateChanged += OnStateChanged;
        Show(player.State);
        _clock.Start();
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

        ShowArtwork(state);
        UpdateClock();
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

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Controls;

/// <summary>Where a now-playing stage is shown.</summary>
internal enum StageKind
{
    /// <summary>At the top of Home: a card as tall as the page, with a play button and the next three songs.</summary>
    Home,

    /// <summary>The away screen: the whole window, nothing to press, and the next song on one line.</summary>
    Away,
}

/// <summary>
/// The song that plays, large: its cover (Spotify's 640 px picture once
/// known, the smaller one or the media session's until then), the title in
/// big type, the artists, where it plays from and what comes next, over
/// slowly drifting clouds in the cover's colours (<see cref="CloudField"/>)
/// and, if the user chose it, the cover blurred, with the visualizer's bars
/// (<see cref="StageVisualizer"/>) in the room under the cover and the
/// words, unless the user turned them off. Shared by the Home stage
/// and the away screen (built-in plugins). A new song's cover fades in
/// where the old one was, and the clouds take its colours over a second.
/// The clouds drift and the bars move only while music plays, the window
/// shows, the stage is on screen, Windows' animations are on, and no
/// full-screen game, video, presentation, lock screen or dark display has
/// the screen. Built in code.
/// </summary>
internal sealed partial class NowPlayingStage : Grid
{
    /// <summary>The largest the cover is drawn, and the width pictures are decoded at.</summary>
    private const int CoverMax = 440;

    private const int SampleSize = 40;
    private const int BlurRadius = 3;
    private const int UpNextCount = 3;
    private const double CoverCorner = 8;
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string NextGlyph = "";

    // Space kept between the words (or the cover) and the visualizer's tallest bar.
    private const double BarGap = 28;

    private static readonly TimeSpan CoverFade = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ScreenCheck = TimeSpan.FromSeconds(5);

    // The away screen covers the window: the Home stage beneath it rests.
    private static bool _covered;

    private readonly AppServices _services;
    private readonly StageKind _kind;
    private readonly Image _blur = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly CloudField _clouds = new();
    private readonly StageVisualizer _visualizer;
    private readonly Grid _coverBox = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _cover = new() { CornerRadius = new CornerRadius(CoverCorner) };
    private readonly Image _coverBack = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _coverFront = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly StackPanel _text = new() { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _eyebrow;
    private readonly TextBlock _title;
    private readonly TextBlock _artists;
    private readonly TextBlock _source;
    private readonly Button? _play;
    private readonly Button? _next;
    private readonly StackPanel? _controls;
    private readonly StackPanel? _upNext;
    private readonly List<(Grid Cover, Image Image, TextBlock Title, TextBlock Artists, Grid Row)> _upNextRows = [];
    private readonly TextBlock? _upNextLine;
    private readonly DispatcherQueueTimer _screenTimer;
    private readonly ScalarTransition _fade = new() { Duration = CoverFade };

    private bool _attached;
    private bool _onScreen = true;
    private bool _screenTaken;
    private int _updateQueued;
    private PlayRecord? _lastPlayed;
    private Song? _shown;
    private bool _songShown;
    private object? _coverSong;
    private object? _coverArt;
    private string? _coverFull;
    private bool _frontIsFull;
    private object? _colourKey;
    private CancellationTokenSource? _colourLoading;
    private IReadOnlyList<ThemeColor>? _raw;
    private byte[]? _pixels;
    private int _blurVersion;
    private string? _upNextKey;
    private bool _upNextStale;
    private CancellationTokenSource? _upNextLoading;
    private bool _wide = true;
    private ThemeColor _page;
    private bool _barRoomQueued;
    private bool _visualizerAroundCover;

    public NowPlayingStage(AppServices services, StageKind kind)
    {
        _services = services;
        _kind = kind;
        var resources = Application.Current.Resources;
        var away = kind == StageKind.Away;

        _eyebrow = new TextBlock { Style = (Style)resources["ResonateEyebrowTextStyle"] };
        _title = new TextBlock
        {
            Style = (Style)resources["ResonateDisplayTextStyle"],
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 2,
            Margin = new Thickness(0, 4, 0, 2),
        };
        _artists = new TextBlock { Style = (Style)resources["ResonateTitleTextStyle"], FontSize = away ? 26 : 22, FontWeight = Microsoft.UI.Text.FontWeights.Normal };
        _source = new TextBlock { Style = (Style)resources["ResonateSecondaryTextStyle"], FontSize = away ? 16 : 14 };
        SongLinks.Attach(_artists, SongLinks.Artists, PlayingTrack.Get);
        _text.Children.Add(_eyebrow);
        _text.Children.Add(_title);
        _text.Children.Add(_artists);
        _text.Children.Add(_source);

        if (!away)
        {
            _title.Tapped += OnTitleTapped;
            _play = new Button
            {
                Style = (Style)resources["ResonatePlayButtonStyle"],
                Width = 64,
                Height = 64,
                FontSize = 24,
                Content = PlayGlyph,
            };
            _play.Click += OnPlayClick;

            // Skip beside the big play button (the owner's request, 9 October 2026).
            _next = new Button
            {
                Style = (Style)resources["ResonateIconButtonStyle"],
                Width = 48,
                Height = 48,
                FontSize = 22,
                Content = NextGlyph,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(_next, "Next");
            ToolTipService.SetToolTip(_next, "Next");
            _next.Click += (_, _) => _ = _services.Player.NextAsync();
            _controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 18, 0, 0),
            };
            _controls.Children.Add(_play);
            _controls.Children.Add(_next);
            _text.Children.Add(_controls);

            _upNext = new StackPanel { Spacing = 10, Margin = new Thickness(0, 26, 0, 0), Visibility = Visibility.Collapsed };
            _upNext.Children.Add(new TextBlock { Style = (Style)resources["ResonateEyebrowTextStyle"], Text = "UP NEXT" });
            for (var i = 0; i < UpNextCount; i++)
            {
                _upNextRows.Add(UpNextRow(resources));
                _upNext.Children.Add(_upNextRows[i].Row);
            }

            _text.Children.Add(_upNext);
        }
        else
        {
            IsHitTestVisible = false;
            _upNextLine = new TextBlock
            {
                Style = (Style)resources["ResonateSecondaryTextStyle"],
                FontSize = 16,
                Margin = new Thickness(0, 24, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            _text.Children.Add(_upNextLine);
        }

        _cover.Children.Add(_coverBack);
        _cover.Children.Add(_coverFront);
        _coverBox.Children.Add(new Elevation { Level = ElevationLevel.Item, CornerRadius = new CornerRadius(CoverCorner) });
        _coverBox.Children.Add(_cover);
        _coverFront.ImageOpened += (_, _) =>
        {
            if (_coverFront.Source is not null)
            {
                ShowFront();
            }
        };
        _coverFront.ImageFailed += (_, _) => _coverBack.Source = null;

        Body = new Grid
        {
            ColumnSpacing = away ? 64 : 48,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
            Children = { _coverBox, _text },
        };

        _visualizer = new StageVisualizer(services.Visualiser) { CoverCorner = CoverCorner };

        Children.Add(_blur);
        Children.Add(_clouds);
        Children.Add(_visualizer);
        Children.Add(Body);

        _screenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _screenTimer.Interval = ScreenCheck;
        _screenTimer.IsRepeating = true;

        // The last cover's colours, so the very first frame is already in them.
        if (services.Settings.HomeStageColours is { Count: > 0 } saved)
        {
            var colours = new List<ThemeColor>();
            foreach (var text in saved)
            {
                if (ThemeColor.TryParse(text, out var colour))
                {
                    colours.Add(colour);
                }
            }

            if (colours.Count > 0)
            {
                _raw = colours;
            }
        }

        ApplyLook();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, e) => Fit(e.NewSize);

        // The bars keep below the cover and the words, wherever those end up.
        _text.SizeChanged += (_, _) => QueueBarRoom();
        _coverBox.SizeChanged += (_, _) => QueueBarRoom();
    }

    /// <summary>Raised (static) when a stage option changes in Settings, so every stage shown follows at once.</summary>
    public static event EventHandler? OptionsChanged;

    /// <summary>The cover and the words: what the Home stage moves and fades as the page scrolls, and the away screen shifts.</summary>
    public Grid Body { get; }

    /// <summary>The cover's box, which the Home stage shrinks as the page scrolls.</summary>
    public FrameworkElement Cover => _coverBox;

    /// <summary>Tells every stage shown that an option (the blurred cover) changed.</summary>
    public static void NotifyOptionsChanged() => OptionsChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>The away screen covers (true) or uncovers the window; the Home stage's clouds rest while covered.</summary>
    public static void SetCovered(bool covered)
    {
        if (covered != _covered)
        {
            _covered = covered;
            OptionsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>What played last, shown (with a play button) while nothing is loaded.</summary>
    public void SetLastPlayed(PlayRecord? play)
    {
        if (!ReferenceEquals(play, _lastPlayed))
        {
            _lastPlayed = play;
            Show();
        }
    }

    /// <summary>Whether any of the stage is on screen (Home scrolled past it: false). The clouds rest while it is not.</summary>
    public void SetOnScreen(bool onScreen)
    {
        if (onScreen != _onScreen)
        {
            _onScreen = onScreen;
            UpdateRunning();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;

        // The timer's handler only while shown: one left on it keeps the stage, and the page around it, in memory.
        _screenTimer.Tick += OnScreenTick;
        _services.Player.StateChanged += OnPlayerChanged;
        _services.Player.QueueChanged += OnQueueChanged;
        _services.Theme.Changed += OnThemeChanged;
        _services.Visualiser.LiveChanged += OnLiveChanged;
        OptionsChanged += OnOptionsChanged;
        if (App.MainWindow is { } window)
        {
            window.ShownChanged += OnShownChanged;
        }

        ApplyLook();
        Show();
        QueueBarRoom();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _services.Player.StateChanged -= OnPlayerChanged;
        _services.Player.QueueChanged -= OnQueueChanged;
        _services.Theme.Changed -= OnThemeChanged;
        _services.Visualiser.LiveChanged -= OnLiveChanged;
        OptionsChanged -= OnOptionsChanged;
        if (App.MainWindow is { } window)
        {
            window.ShownChanged -= OnShownChanged;
        }

        _screenTimer.Stop();
        _screenTimer.Tick -= OnScreenTick;
        _colourLoading?.Cancel();
        _upNextLoading?.Cancel();
        _colourKey = null;
        _upNextKey = null;
        _songShown = false;
        UpdateRunning();
    }

    private void OnPlayerChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads, sometimes in bursts; look at the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                if (_attached)
                {
                    Show();
                }
            });
        }
    }

    private void OnQueueChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_attached)
            {
                _upNextKey = null;
                Show();
            }
        });

    private void OnShownChanged(object? sender, EventArgs e) => UpdateRunning();

    // A local file started or stopped playing: the bars follow its sound, or sway on their own.
    private void OnLiveChanged(object? sender, EventArgs e) => UpdateRunning();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyLook();
        UpdateRunning();
    }

    private void OnOptionsChanged(object? sender, EventArgs e)
    {
        _visualizer.ApplyOptions();
        ApplyLook();
        UpdateRunning();
    }

    /// <summary>
    /// The look's visualizer style: along the bottom of the stage, or, for
    /// the styles drawn around the cover, in the cover's box behind the
    /// cover, so it moves and shrinks with it as the page scrolls.
    /// </summary>
    private void PlaceVisualizer(VisualizerStyle style)
    {
        var around = VisualizerShapes.AroundCover(style);
        if (around != _visualizerAroundCover)
        {
            _visualizerAroundCover = around;
            (around ? (Panel)this : _coverBox).Children.Remove(_visualizer);
            if (around)
            {
                _visualizer.Margin = new Thickness(0);
                _coverBox.Children.Insert(0, _visualizer);
            }
            else
            {
                Children.Insert(Children.IndexOf(Body), _visualizer);
            }

            // Moving it may have stopped it; it starts again where it now is.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdateRunning);
        }

        _visualizer.DrawStyle = style;
        FitReach();
        QueueBarRoom();
    }

    /// <summary>How far a visualizer around the cover may reach: short of the words beside or under it.</summary>
    private void FitReach()
    {
        var away = _kind == StageKind.Away;
        _visualizer.Reach = _wide ? (away ? 52 : 40) : 20;
    }

    /// <summary>The look's colours and corners, and the clouds and blurred cover made readable for its text.</summary>
    private void ApplyLook()
    {
        PlaceVisualizer(_services.Theme.Current.StageVisualizer);
        var palette = _services.Theme.Palette;
        var home = _kind == StageKind.Home;
        var page = home ? palette.Surface.Over(palette.Background).Opaque : palette.Background.Opaque;
        _page = page;
        Background = new SolidColorBrush(page.ToColor());
        var corner = home ? palette.CornerLarge : 0;
        CornerRadius = new CornerRadius(corner);
        _clouds.CornerRadiusValue = (float)corner;
        PaintClouds(animate: false);
        ShowBlur();
    }

    /// <summary>Shows what plays now, what played last, or nothing; only what changed is touched.</summary>
    private void Show()
    {
        var state = _services.Player.State;
        Song? song = null;
        if (state.HasTrack)
        {
            song = new Song(
                state.Title!,
                state.Artists ?? string.Empty,
                state.Album ?? state.Title!,
                state.SourceName is { Length: > 0 } from ? "Playing from " + from : state.Album ?? string.Empty,
                state.ArtworkUrl,
                state.ArtworkBytes,
                state.FullArtworkUrl,
                state.IsPlaying ? "NOW PLAYING" : "PAUSED");
        }
        else if (_lastPlayed is { } play)
        {
            song = new Song(
                play.Title,
                string.Join(", ", play.Artists.Select(a => a.Name)),
                play.Album.Length > 0 ? play.Album : play.Title,
                play.Album,
                play.ImageUrl,
                null,
                null,
                "LAST PLAYED");
        }

        if (!_songShown || song != _shown)
        {
            _songShown = true;
            _shown = song;
            ShowSong(song);
        }

        if (_play is not null)
        {
            _play.Content = state.HasTrack && state.IsPlaying ? PauseGlyph : PlayGlyph;
            AutomationProperties.SetName(_play, state.HasTrack && state.IsPlaying ? "Pause" : "Play");
        }

        if (_controls is not null && _next is not null)
        {
            _controls.Visibility = song is null ? Visibility.Collapsed : Visibility.Visible;
            _next.Visibility = state.HasTrack ? Visibility.Visible : Visibility.Collapsed;
        }

        ShowUpNext(state);
        UpdateRunning();
    }

    private void ShowSong(Song? song)
    {
        if (song is null)
        {
            _eyebrow.Text = string.Empty;
            _title.Text = "Nothing playing";
            _artists.Visibility = Visibility.Collapsed;
            _source.Text = "Play something and it shows here.";
            _source.Visibility = Visibility.Visible;
            _coverSong = null;
            _coverArt = null;
            _coverFull = null;
            _frontIsFull = false;
            _coverBack.Source = null;
            HideFront();
            _coverFront.Source = null;
            _cover.Background = _services.Theme.GetBrush("ResonateAccentBrush");
            ShowColours(null, null, null, null);
            return;
        }

        _eyebrow.Text = song.Label;
        _title.Text = song.Title;
        _artists.Text = song.Artists;
        _artists.Visibility = song.Artists.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _source.Text = song.Source;
        _source.Visibility = song.Source.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowCover(song);
        ShowColours(song.SmallUrl ?? song.FullUrl, song.Bytes, song.Name, song.SmallUrl ?? (object?)song.Bytes ?? song.FullUrl ?? song.Name);
    }

    /// <summary>
    /// A new song's cover fades in over the old one, in place; a sharper
    /// picture of the same song (the 640 px one) simply takes over.
    /// </summary>
    private void ShowCover(Song song)
    {
        object songKey = (song.Title, song.Artists, song.Name);
        if (!Equals(songKey, _coverSong))
        {
            _coverSong = songKey;
            _coverArt = null;
            _coverFull = null;
            _frontIsFull = false;
            if (_coverFront.Opacity > 0 && _coverFront.Source is not null)
            {
                _coverBack.Source = _coverFront.Source;
            }

            HideFront();
            _coverFront.Source = null;
            _cover.Background = Artwork.PlaceholderBrush(song.Name);
            if (song.SmallUrl is null && song.Bytes is null && song.FullUrl is null)
            {
                // No picture at all: the album's colour tile.
                _coverBack.Source = null;
            }
        }

        object? art = song.SmallUrl ?? (object?)song.Bytes;
        if (art is not null && !Equals(art, _coverArt))
        {
            _coverArt = art;
            _ = ShowCoverAsync(songKey, full: false, song.SmallUrl is { } url
                ? _services.Covers.GetReadyAsync(url, CoverMax)
                : CoverImages.FromBytesAsync(song.Bytes!, CoverMax));
        }

        // The sharper picture comes through the cover store (Spotify's covers), so it swaps in only once it has its pixels.
        if (song.FullUrl is { } full && full != _coverFull && _services.Covers.Store is not null && CoverStore.Handles(full))
        {
            _coverFull = full;
            _ = ShowCoverAsync(songKey, full: true, _services.Covers.GetReadyAsync(full, CoverMax));
        }
    }

    private async Task ShowCoverAsync(object songKey, bool full, Task<(ImageSource? Image, bool Loaded)> loading)
    {
        var (image, loaded) = await loading;
        if (image is null || !Equals(songKey, _coverSong) || (full ? !loaded : _frontIsFull))
        {
            return;
        }

        _coverFront.Source = image;
        _frontIsFull = full;

        // A picture that already has its pixels may not raise ImageOpened, so it is shown here.
        if (loaded)
        {
            ShowFront();
        }
    }

    private void ShowFront()
    {
        _coverFront.OpacityTransition = _services.Theme.AnimationsEnabled ? _fade : null;
        _coverFront.Opacity = 1;
    }

    /// <summary>Hides the front picture at once (the old cover stays behind it until the new one fades in).</summary>
    private void HideFront()
    {
        _coverFront.OpacityTransition = null;
        _coverFront.Opacity = 0;
    }

    /// <summary>Reads the cover's colours (off this thread) for the clouds and the blurred cover; the look's accents with no song.</summary>
    private void ShowColours(string? url, byte[]? bytes, string? name, object? key)
    {
        var look = _services.Theme.Current;
        key ??= (look.Accent.Opaque, look.Accent2.Opaque);
        if (Equals(key, _colourKey) || !_attached)
        {
            return;
        }

        _colourKey = key;
        _colourLoading?.Cancel();
        _colourLoading = new CancellationTokenSource();
        _ = LoadColoursAsync(url, bytes, name, _colourLoading.Token);
    }

    private async Task LoadColoursAsync(string? url, byte[]? bytes, string? name, CancellationToken token)
    {
        byte[]? pixels = null;
        try
        {
            if (bytes is null && url is not null && _services.Covers.Store is { } store && CoverStore.Handles(url))
            {
                bytes = store.TryGetRecent(url) ?? await store.GetAsync(url).WaitAsync(token);
            }

            if (bytes is not null)
            {
                pixels = await CoverDecoder.DecodeAsync(bytes, SampleSize, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Offline or an unreadable picture: the album's tile colours stand in.
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (pixels is null)
        {
            var look = _services.Theme.Current;
            var (from, to) = name is not null ? Artwork.PlaceholderColors(name) : (look.Accent.Opaque, look.Accent2.Opaque);
            pixels = ArtworkColors.Gradient(from, to, SampleSize);
        }

        IReadOnlyList<ThemeColor> raw;
        try
        {
            raw = await Task.Run(() => StageColours.Palette(pixels, SampleSize, SampleSize), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        _raw = raw;
        _pixels = pixels;
        PaintClouds(animate: true);
        ShowBlur();

        // Kept for the next start, so the stage opens in these colours.
        var saved = raw.Select(c => c.Opaque.ToString()).ToList();
        if (!saved.SequenceEqual(_services.Settings.HomeStageColours))
        {
            _services.Settings.HomeStageColours = saved;
            _services.SaveSettings();
        }
    }

    private void PaintClouds(bool animate)
    {
        var palette = _services.Theme.Palette;
        var look = _services.Theme.Current;
        var raw = _raw ?? [look.Accent.Opaque, look.Accent2.Opaque];
        var safe = raw.Select(c => StageColours.ForText(c, palette)).ToList();

        // Dimmer on true black, for OLED screens; softer over the blurred cover.
        var strength = _services.Settings.HomeStageBlurredCover ? 0.55 : 1.0;
        if (palette.Background.Opaque == ThemeColor.Black)
        {
            strength *= 0.6;
        }

        _clouds.SetColours(safe, strength, animate && _services.Theme.AnimationsEnabled);

        // The bars never sit under text, so they keep the cover's own colours, made to stand out from the page.
        _visualizer.SetColours(raw.Select(c => StageColours.ForBars(c, _page)).ToList(), animate && _services.Theme.AnimationsEnabled);
    }

    /// <summary>The cover blurred, made vivid and then safe for the text, as one tiny picture the GPU stretches (if the user chose it).</summary>
    private void ShowBlur()
    {
        var version = ++_blurVersion;
        if (!_services.Settings.HomeStageBlurredCover || _pixels is not { } pixels)
        {
            _blur.Visibility = Visibility.Collapsed;
            _blur.Source = null;
            return;
        }

        _ = ShowBlurAsync(pixels, _services.Theme.Palette, version);
    }

    private async Task ShowBlurAsync(byte[] pixels, ThemePalette palette, int version)
    {
        var shown = await Task.Run(() =>
        {
            var copy = (byte[])pixels.Clone();
            ArtworkColors.Blur(copy, SampleSize, SampleSize, BlurRadius);
            ArtworkColors.Vivid(copy, SampleSize, SampleSize);
            StageColours.ForText(copy, palette);
            return copy;
        });

        if (version != _blurVersion)
        {
            return;
        }

        var picture = new WriteableBitmap(SampleSize, SampleSize);
        shown.CopyTo(picture.PixelBuffer);
        picture.Invalidate();
        _blur.Source = picture;
        _blur.Visibility = Visibility.Visible;
    }

    /// <summary>What plays next: from the local player, or from Spotify's queue once per song (only while the stage is seen).</summary>
    private void ShowUpNext(PlayerState state)
    {
        if (_upNext is null && _upNextLine is null)
        {
            return;
        }

        var player = _services.Player;
        var key = state.HasTrack
            ? string.Join('|', player.ActiveSource, state.TrackUri ?? state.Title, state.ContextUri, state.Shuffle, state.Repeat)
            : null;
        if (key == _upNextKey && !_upNextStale)
        {
            return;
        }

        _upNextKey = key;
        _upNextLoading?.Cancel();
        if (key is null)
        {
            _upNextStale = false;
            ShowUpNextRows([]);
            return;
        }

        if (player.ActiveSource == PlaybackSource.LocalFiles)
        {
            _upNextStale = false;
            ShowUpNextRows(player.Local.Upcoming);
            return;
        }

        if (!Seen)
        {
            // Read when the stage is seen again; nothing is asked of Spotify meanwhile.
            _upNextStale = true;
            return;
        }

        _upNextStale = false;
        _upNextLoading = new CancellationTokenSource();
        _ = LoadUpNextAsync(key, _upNextLoading.Token);
    }

    private async Task LoadUpNextAsync(string key, CancellationToken token)
    {
        try
        {
            var queue = await Task.Run(() => _services.Api.GetQueueAsync(token), token);
            if (key == _upNextKey && !token.IsCancellationRequested)
            {
                ShowUpNextRows(QueuePreview.Upcoming(queue));
            }
        }
        catch (Exception)
        {
            // Offline, refused or cancelled: nothing is shown as next.
            if (key == _upNextKey && !token.IsCancellationRequested)
            {
                ShowUpNextRows([]);
            }
        }
    }

    private void ShowUpNextRows(IReadOnlyList<TrackInfo> upcoming)
    {
        if (_upNextLine is not null)
        {
            _upNextLine.Text = upcoming.Count > 0 ? $"Up next: {upcoming[0].Title} · {upcoming[0].Artists}" : string.Empty;
            _upNextLine.Visibility = upcoming.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_upNext is null)
        {
            return;
        }

        for (var i = 0; i < _upNextRows.Count; i++)
        {
            var (cover, image, title, artists, row) = _upNextRows[i];
            if (i >= upcoming.Count)
            {
                row.Visibility = Visibility.Collapsed;
                continue;
            }

            var track = upcoming[i];
            row.Visibility = Visibility.Visible;
            title.Text = track.Title;
            artists.Text = track.Artists;
            cover.Background = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
            image.Source = track.FilePath is not null
                ? LocalArtwork.For(track, 44)
                : Artwork.FromUrl(track.SmallImageUrl ?? track.LargeImageUrl, 44);
        }

        _upNext.Visibility = upcoming.Count > 0 && ActualHeight >= 560 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static (Grid Cover, Image Image, TextBlock Title, TextBlock Artists, Grid Row) UpNextRow(ResourceDictionary resources)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var cover = new Grid { Width = 44, Height = 44, CornerRadius = new CornerRadius(4), Children = { image } };
        var title = new TextBlock { Style = (Style)resources["ResonateBodyTextStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var artists = new TextBlock { Style = (Style)resources["ResonateCaptionTextStyle"] };
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, artists } };
        Grid.SetColumn(words, 1);
        var row = new Grid
        {
            ColumnSpacing = 12,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { cover, words },
        };
        return (cover, image, title, artists, row);
    }

    /// <summary>The stage is in the window, the window shows, and the stage is on screen (not under the away screen).</summary>
    private bool Seen => _attached && _onScreen && !(_covered && _kind == StageKind.Home) && (App.MainWindow?.IsShown ?? true);

    /// <summary>The clouds drift only while the music plays and the stage can be seen.</summary>
    private void UpdateRunning()
    {
        var playing = Seen && _services.Player.State.IsPlaying;
        if (playing && !_screenTimer.IsRunning)
        {
            _screenTaken = ScreenTaken();
            _screenTimer.Start();
        }
        else if (!playing)
        {
            _screenTimer.Stop();
        }

        var animate = _services.Theme.AnimationsEnabled;
        _clouds.SetRunning(playing && !_screenTaken, animate);

        // With Windows' animations off the bars could only stand still, so they are not shown.
        var bars = _services.Settings.HomeStageVisualizer && animate;
        _visualizer.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
        _visualizer.SetRunning(bars && playing && !_screenTaken, _services.Visualiser.IsLive, animate);
        if (Seen && _upNextStale)
        {
            ShowUpNext(_services.Player.State);
        }
    }

    private void OnScreenTick(DispatcherQueueTimer sender, object args) => CheckScreen();

    private void CheckScreen()
    {
        var taken = ScreenTaken();
        if (taken != _screenTaken)
        {
            _screenTaken = taken;
            UpdateRunning();
        }
    }

    /// <summary>A full-screen game or video, a presentation, the lock screen, or a display that is off or dimmed.</summary>
    private static bool ScreenTaken()
    {
        if (UserPresence.IsScreenTaken())
        {
            return true;
        }

        try
        {
            var display = Microsoft.Windows.System.Power.PowerManager.DisplayStatus;
            return display != Microsoft.Windows.System.Power.DisplayStatus.On;
        }
        catch (Exception)
        {
            // Not available here: the display is taken to be on.
            return false;
        }
    }

    /// <summary>Lays the cover and the words out for the stage's size: side by side, or the cover above on a narrow stage.</summary>
    private void Fit(global::Windows.Foundation.Size size)
    {
        var away = _kind == StageKind.Away;
        var pad = away ? 72.0 : size.Width < 640 ? 28 : 48;

        // The away screen keeps the top for its clock.
        var top = away ? 160 : pad;
        Body.Margin = new Thickness(pad, top, pad, pad);
        _visualizer.Margin = _visualizer.AroundCover ? new Thickness(0) : new Thickness(pad, 0, pad, 0);
        var innerWidth = Math.Max(0, size.Width - (2 * pad));
        var innerHeight = Math.Max(0, size.Height - top - pad);
        _wide = innerWidth >= 600;

        double cover = _wide
            ? Math.Min(away ? 560 : CoverMax, Math.Min(innerHeight, innerWidth * 0.42))
            : Math.Min(240, Math.Min(innerHeight * 0.4, innerWidth));
        cover = Math.Max(96, Math.Floor(cover));
        _coverBox.Width = cover;
        _coverBox.Height = cover;

        Grid.SetRow(_text, _wide ? 0 : 1);
        Grid.SetColumn(_text, _wide ? 1 : 0);
        Grid.SetColumnSpan(_text, _wide ? 1 : 2);
        Body.ColumnDefinitions[0].Width = _wide ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        Body.ColumnDefinitions[1].Width = _wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Body.RowDefinitions[0].Height = _wide ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Body.RowDefinitions[1].Height = _wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Body.ColumnSpacing = _wide ? (away ? 64 : 48) : 0;
        Body.RowSpacing = _wide ? 0 : 28;
        _text.VerticalAlignment = _wide ? VerticalAlignment.Center : VerticalAlignment.Top;

        FitReach();
        _title.FontSize = away ? (_wide ? 64 : 44) : !_wide ? 32 : cover >= 360 ? 52 : 40;
        if (_upNext is not null)
        {
            _upNext.Visibility = _upNextRows.Any(r => r.Row.Visibility == Visibility.Visible) && size.Height >= 560
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        QueueBarRoom();
    }

    /// <summary>Works out the room under the cover and the words once layout has settled (many changes, one look).</summary>
    private void QueueBarRoom()
    {
        if (_barRoomQueued)
        {
            return;
        }

        _barRoomQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _barRoomQueued = false;
            FitBars();
        });
    }

    /// <summary>The bars may grow up to a little below whichever of the cover and the words ends lower.</summary>
    private void FitBars()
    {
        if (!_attached || ActualHeight <= 0)
        {
            return;
        }

        var bottom = 0.0;
        foreach (FrameworkElement part in (FrameworkElement[])[_coverBox, _text])
        {
            if (part.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var box = part.TransformToVisual(this).TransformBounds(new global::Windows.Foundation.Rect(0, 0, part.ActualWidth, part.ActualHeight));
                bottom = Math.Max(bottom, box.Bottom);
            }
            catch (ArgumentException)
            {
                // Not laid out in this stage yet: the next change looks again.
                return;
            }
        }

        _visualizer.SetRoom(ActualHeight - bottom - BarGap);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_services.Player.State.HasTrack)
        {
            // The player shows the new state at once; the stage follows it.
            _ = _services.Player.TogglePlayPauseAsync();
        }
        else if (_lastPlayed is { } play)
        {
            var track = play.ToTrack();
            _ = _services.Player.PlayAsync(new PlayRequest([track], 0, track.AlbumUri, track.Album));
        }
    }

    private void OnTitleTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_services.Player.State.HasTrack)
        {
            App.MainWindow?.OpenNowPlaying();
        }
    }

    /// <summary>A song as the stage shows it.</summary>
    private sealed record Song(
        string Title,
        string Artists,
        string Name,
        string Source,
        string? SmallUrl,
        byte[]? Bytes,
        string? FullUrl,
        string Label);
}

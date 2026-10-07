using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.App.Helpers;
using Resonate.App.Pages;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes.Skins;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The classic player: a Winamp 2 style main window drawn from a skin, with
/// the cover, the song and the heart beside it in the app's own look. Every
/// skin pixel covers a whole number of screen pixels, so skins stay sharp at
/// any display scale. Like the player bar, every control acts on the player
/// at once (the player is optimistic). It works only while it is in the
/// window: MainWindow adds it when the classic player is chosen and removes
/// it again, and while it is out nothing of it runs.
/// </summary>
public sealed partial class ClassicPlayer : UserControl
{
    private const double MinCoverSize = 48;

    /// <summary>Spotify asks for its artwork to keep small rounded corners.</summary>
    private const double CoverCorner = 8;

    /// <summary>Enough for double size on a 400 % display; keeps a bad scale from taking much memory.</summary>
    private const int MaxScale = 8;

    /// <summary>What the marquee says when nothing is loaded.</summary>
    private const string IdleLine = "Resonate";

    private const string HeartGlyph = "\uEB51";
    private const string HeartFilledGlyph = "\uEB52";

    /// <summary>A picture older than this means the music stopped flowing: the bars rest.</summary>
    private static readonly TimeSpan StaleFrame = TimeSpan.FromMilliseconds(250);

    /// <summary>Clock ticks land just after the second turns, never just before it.</summary>
    private static readonly TimeSpan TickSlack = TimeSpan.FromMilliseconds(5);

    private readonly AppServices _services = App.Services;
    private readonly SkinLibrary _skins;
    private readonly PlayerRouter _player;
    private readonly CoverSpin _coverSpin;

    // While it is in the window
    private bool _loaded;
    private bool _windowShown = true;
    private bool _windowActive = true;
    private XamlRoot? _root;
    private DispatcherQueueTimer? _clock;
    private DispatcherQueueTimer? _marquee;
    private bool _animating;
    private bool _wanted;

    // What it shows
    private PlayerState _shown = PlayerState.Empty;
    private ClassicPlayState _play = ClassicPlayState.Stopped;
    private long _pausedAt;
    private int _updateQueued;
    private string _songLine = IdleLine;
    private int _marqueeStep;
    private object? _coverKey;
    private int _coverDecodeSize;

    // Drawing: the skin's own pixels, enlarged into bitmaps of exactly the pixels on screen
    private double _raster = 1;
    private int _scale = 1;
    private SkinImage? _frame;
    private WriteableBitmap? _bitmap;
    private byte[]? _pixels;
    private SkinImage? _visFrame;
    private WriteableBitmap? _visBitmap;
    private byte[]? _visPixels;
    private ClassicView? _drawn;
    private bool _renderQueued;
    private VisualiserFrame? _lastFrame;
    private long _visSequence = -1;
    private bool _visAtRest;

    // The pointer
    private ClassicControl _pressed;
    private uint _pointerId;
    private bool _pressedOver;
    private double _grabOffset;
    private double? _dragValue;
    private int _sentVolume = -1;
    private ClassicControl _hovered;

    public ClassicPlayer()
    {
        InitializeComponent();
        _skins = _services.Skins;
        _player = _services.Player;
        _coverSpin = new CoverSpin(CoverFrame);

        // Fade covers in instead of popping them (runs on the compositor).
        CoverImage.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Whether the window can be seen (false while minimised or hidden). The
    /// cover and the visualiser move, and the clock ticks, only while it can.
    /// </summary>
    public void SetWindowShown(bool shown)
    {
        if (_windowShown == shown)
        {
            return;
        }

        _windowShown = shown;
        if (_loaded)
        {
            ApplyCoverLook();
            UpdateVisualiser();
            UpdateTimers();
            Invalidate();
        }
    }

    /// <summary>Whether the window has focus: the skin draws its title bar active or inactive.</summary>
    public void SetWindowActive(bool active)
    {
        if (_windowActive != active)
        {
            _windowActive = active;
            Invalidate();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _root = XamlRoot;
        _root.Changed += OnXamlRootChanged;
        _services.Theme.Changed += OnThemeChanged;
        _skins.Changed += OnSkinsChanged;
        _skins.OptionsChanged += OnOptionsChanged;
        _player.StateChanged += OnStateChanged;
        _services.Visualiser.LiveChanged += OnLiveChanged;
        _services.Likes.Changed += OnLikesChanged;
        _services.Equalizer.Changed += OnEqualizerChanged;
        if (App.MainWindow is { } window)
        {
            window.QueueOpenChanged += OnQueueOpenChanged;
        }

        _clock = DispatcherQueue.CreateTimer();
        _clock.IsRepeating = false;
        _clock.Tick += OnClockTick;
        _marquee = DispatcherQueue.CreateTimer();
        _marquee.Interval = Marquee.Step;
        _marquee.IsRepeating = true;
        _marquee.Tick += OnMarqueeTick;

        _skins.EnsureLoaded();
        RebuildSurface();
        Show(_player.State);
    }

    // Everything that could keep this control alive (services' events, timers, the frame callback) lets go here.
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        if (_root is not null)
        {
            _root.Changed -= OnXamlRootChanged;
            _root = null;
        }

        _services.Theme.Changed -= OnThemeChanged;
        _skins.Changed -= OnSkinsChanged;
        _skins.OptionsChanged -= OnOptionsChanged;
        _player.StateChanged -= OnStateChanged;
        _services.Visualiser.LiveChanged -= OnLiveChanged;
        _services.Likes.Changed -= OnLikesChanged;
        _services.Equalizer.Changed -= OnEqualizerChanged;
        if (App.MainWindow is { } window)
        {
            window.QueueOpenChanged -= OnQueueOpenChanged;
        }

        if (_clock is not null)
        {
            _clock.Stop();
            _clock.Tick -= OnClockTick;
            _clock = null;
        }

        if (_marquee is not null)
        {
            _marquee.Stop();
            _marquee.Tick -= OnMarqueeTick;
            _marquee = null;
        }

        _pressed = ClassicControl.None;
        _dragValue = null;
        UpdateVisualiser();
        ApplyCoverLook();
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
            if (_loaded)
            {
                Show(_player.State);
            }
        });
    }

    private void Show(PlayerState state)
    {
        var play = PlayStateOf(state, DateTimeOffset.UtcNow);
        if (play == ClassicPlayState.Paused && _play != ClassicPlayState.Paused)
        {
            _pausedAt = Stopwatch.GetTimestamp();
        }

        _shown = state;
        _play = play;

        TitleText.Text = state.Title ?? "Nothing playing";
        ArtistText.Text = state.Artists ?? (state.IsConnected ? string.Empty : "Pick a song to start");
        AlbumText.Text = state.Album ?? string.Empty;
        ShowLike(state);
        ShowCover(state);

        // A new line starts from its beginning.
        var line = state.HasTrack ? Marquee.Line(state.Artists, state.Title, state.Duration) : IdleLine;
        if (line != _songLine)
        {
            _songLine = line;
            _marqueeStep = 0;
        }

        ApplyCoverLook();
        UpdateVisualiser();
        UpdateTimers();
        Invalidate();
    }

    /// <summary>Spotify has no stop: "stopped" is nothing loaded, or paused at the very start (as after Stop).</summary>
    private static ClassicPlayState PlayStateOf(PlayerState state, DateTimeOffset now) =>
        state.IsPlaying
            ? ClassicPlayState.Playing
            : state.HasTrack && state.PositionAt(now) > TimeSpan.Zero
                ? ClassicPlayState.Paused
                : ClassicPlayState.Stopped;

    // Drawing

    /// <summary>Sizes the bitmaps for the display's scale, double size and shade mode; every skin pixel becomes a whole number of screen pixels.</summary>
    private void RebuildSurface()
    {
        if (!_loaded || _root is null)
        {
            return;
        }

        _raster = _root.RasterizationScale > 0 ? _root.RasterizationScale : 1;
        var shaded = _skins.Shaded;
        _scale = Math.Clamp((int)Math.Round((_skins.DoubleSize ? 2 : 1) * _raster, MidpointRounding.AwayFromZero), 1, MaxScale);

        var height = shaded ? ClassicRenderer.ShadeHeight : ClassicRenderer.Height;
        if (_frame is null || _frame.Height != height)
        {
            _frame = new SkinImage(ClassicRenderer.Width, height);
        }

        _bitmap = Surface(_bitmap, ref _pixels, ClassicRenderer.Width, height);
        SkinView.Source = _bitmap;
        SkinView.Width = _bitmap.PixelWidth / _raster;
        SkinView.Height = _bitmap.PixelHeight / _raster;

        var area = ClassicLayout.VisualiserArea(shaded);
        if (_visFrame is null || _visFrame.Width != area.Width || _visFrame.Height != area.Height)
        {
            _visFrame = new SkinImage(area.Width, area.Height);
        }

        _visBitmap = Surface(_visBitmap, ref _visPixels, area.Width, area.Height);
        VisualiserView.Source = _visBitmap;
        VisualiserView.Width = _visBitmap.PixelWidth / _raster;
        VisualiserView.Height = _visBitmap.PixelHeight / _raster;
        VisualiserView.Margin = new Thickness(area.X * _scale / _raster, area.Y * _scale / _raster, 0, 0);

        // The cover is a square as tall as the skin (never tiny in shade mode).
        var cover = Math.Max(MinCoverSize, SkinView.Height);
        CoverFrame.Width = cover;
        CoverFrame.Height = cover;
        CoverHole.Width = CoverHole.Height = Math.Max(8, Math.Round(cover * 0.1));

        _drawn = null;
        _visSequence = -1;
    }

    private WriteableBitmap Surface(WriteableBitmap? bitmap, ref byte[]? pixels, int width, int height)
    {
        var pixelWidth = width * _scale;
        var pixelHeight = height * _scale;
        if (bitmap is not null && pixels is not null && bitmap.PixelWidth == pixelWidth && bitmap.PixelHeight == pixelHeight)
        {
            return bitmap;
        }

        pixels = new byte[pixelWidth * pixelHeight * 4];
        return new WriteableBitmap(pixelWidth, pixelHeight);
    }

    /// <summary>Draws the window again on the next turn of the interface thread; many changes in one frame draw once.</summary>
    private void Invalidate()
    {
        if (_renderQueued || !_loaded)
        {
            return;
        }

        _renderQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, Render);
    }

    private void Render()
    {
        _renderQueued = false;
        if (!_loaded || !_skins.IsReady || _frame is null || _bitmap is null || _pixels is null)
        {
            return;
        }

        var view = BuildView();
        if (view == _drawn)
        {
            return;
        }

        var skin = _skins.Current;
        try
        {
            ClassicRenderer.Render(skin, view, _frame);
        }
        catch (Exception ex) when (!skin.IsBuiltIn)
        {
            // A skin the user added is untrusted: back to the built-in one rather than fail.
            _skins.ReportBroken(skin, ex);
            return;
        }

        PixelScaler.Scale(_frame, _scale, MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan()));
        _pixels.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
        _drawn = view;
    }

    private ClassicView BuildView()
    {
        var state = _shown;
        var play = _play;
        var shaded = _skins.Shaded;
        var duration = state.Duration > TimeSpan.Zero ? state.Duration : TimeSpan.Zero;

        // The display shows whole seconds, so the picture changes (and is drawn) once a second.
        var position = WholeSeconds(state.PositionAt(DateTimeOffset.UtcNow));
        double? seek = null;
        if (CanSeek(state, play))
        {
            seek = _pressed == ClassicControl.Seek && _dragValue is { } target
                ? target
                : Math.Clamp(position / duration, 0, 1);
        }

        // In shade mode there is no marquee: the mini time shows where a drag would seek to.
        if (shaded && _pressed == ClassicControl.Seek && _dragValue is { } seekTo)
        {
            position = WholeSeconds(seekTo * duration);
        }

        var (line, offset) = MarqueeText();
        var stopped = play == ClassicPlayState.Stopped;
        return new ClassicView
        {
            State = play,
            Elapsed = position,
            Duration = duration,
            ShowRemaining = _skins.ShowRemaining,
            BlinkOn = play != ClassicPlayState.Paused || BlinkVisible(),
            Marquee = line,
            MarqueeOffset = offset,

            // Spotify can't say what it streams; 44 kHz stereo is what its songs are.
            Kbps = string.Empty,
            Khz = stopped ? string.Empty : "44",
            Stereo = !stopped,
            Volume = _pressed == ClassicControl.Volume && _dragValue is { } volume ? volume : state.Volume,
            Balance = 0,
            Seek = seek,
            Shuffle = state.Shuffle,
            Repeat = state.Repeat != RepeatMode.Off,
            EqualizerOn = _services.Equalizer.Current.Enabled,
            PlaylistOn = App.MainWindow?.IsQueueOpen == true,
            DoubleSize = _skins.DoubleSize,
            Pressed = PressedToDraw(),
            WindowActive = _windowActive,
            Shaded = shaded,
            Visualiser = _skins.Visualiser,
        };
    }

    private static TimeSpan WholeSeconds(TimeSpan time) => TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds));

    private static bool CanSeek(PlayerState state, ClassicPlayState play) =>
        play != ClassicPlayState.Stopped && state.Duration > TimeSpan.Zero && state.CanSeek;

    /// <summary>Paused time blinks as Winamp's did: shown for a second, hidden for a second.</summary>
    private bool BlinkVisible() => (Stopwatch.GetElapsedTime(_pausedAt).Ticks / TimeSpan.TicksPerSecond) % 2 == 0;

    /// <summary>The marquee: what a held slider or button is doing, otherwise the song.</summary>
    private (string Line, int Offset) MarqueeText()
    {
        switch (_pressed)
        {
            case ClassicControl.Volume when _dragValue is { } volume:
                return ($"Volume: {Math.Round(volume * 100)}%", 0);
            case ClassicControl.Balance:
                return ("Balance: Center", 0);
            case ClassicControl.Seek when _dragValue is { } seek && _shown.Duration > TimeSpan.Zero:
                return ($"Seek to: {Format.Duration(seek * _shown.Duration)}/{Format.Duration(_shown.Duration)} ({(int)(seek * 100)}%)", 0);
            case ClassicControl.ClutterDoubleSize:
                return (_skins.DoubleSize ? "Disable doublesize mode" : "Enable doublesize mode", 0);
            default:
                return (_songLine, _marqueeStep);
        }
    }

    private ClassicControl PressedToDraw() => _pressed switch
    {
        // Sliders and the D stay pressed wherever the pointer goes; buttons only while it is over them.
        ClassicControl.Volume or ClassicControl.Balance or ClassicControl.Seek or ClassicControl.ClutterDoubleSize => _pressed,
        _ => _pressedOver ? _pressed : ClassicControl.None,
    };

    // The visualiser

    /// <summary>
    /// Lays the visualiser over the skin while a local file is loaded, and
    /// redraws it every frame only while that file plays and the window is
    /// shown. For Spotify songs the skin shows it at rest: Spotify's sound is
    /// never analysed.
    /// </summary>
    private void UpdateVisualiser()
    {
        var feed = _services.Visualiser;
        var on = _loaded && _skins.Visualiser != VisualiserMode.Off;
        var wanted = on && _windowShown;
        if (wanted != _wanted)
        {
            _wanted = wanted;
            feed.Wanted = wanted;
        }

        var overlay = on && feed.IsLive && _play != ClassicPlayState.Stopped && _skins.IsReady && _visBitmap is not null;
        VisualiserView.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;

        var animate = overlay && _windowShown && _play == ClassicPlayState.Playing;
        if (animate != _animating)
        {
            _animating = animate;
            if (animate)
            {
                _visSequence = -1;
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        if (overlay && !animate)
        {
            // Paused (or out of sight): the last picture stays, as in Winamp.
            DrawVisualiser(_lastFrame, atRest: _lastFrame is null);
        }
    }

    private void OnRendering(object? sender, object e)
    {
        // Pictures come about 100 times a second; frames that bring nothing new draw nothing.
        var frame = _services.Visualiser.Analyser.Read();
        _lastFrame = frame;
        DrawVisualiser(frame, atRest: Stopwatch.GetElapsedTime(frame.Timestamp) > StaleFrame);
    }

    private void DrawVisualiser(VisualiserFrame? frame, bool atRest)
    {
        var sequence = frame?.Sequence ?? -2;
        if ((sequence == _visSequence && atRest == _visAtRest) || !_skins.IsReady || _visFrame is null || _visBitmap is null || _visPixels is null)
        {
            return;
        }

        _visSequence = sequence;
        _visAtRest = atRest;
        var skin = _skins.Current;
        try
        {
            ClassicRenderer.RenderVisualiser(skin, _skins.Visualiser, atRest ? null : frame, _skins.Shaded, _visFrame);
        }
        catch (Exception ex) when (!skin.IsBuiltIn)
        {
            _skins.ReportBroken(skin, ex);
            return;
        }

        PixelScaler.Scale(_visFrame, _scale, MemoryMarshal.Cast<byte, uint>(_visPixels.AsSpan()));
        _visPixels.CopyTo(_visBitmap.PixelBuffer);
        _visBitmap.Invalidate();
    }

    private void OnLiveChanged(object? sender, EventArgs e)
    {
        if (!_services.Visualiser.IsLive)
        {
            _lastFrame = null;
        }

        UpdateVisualiser();
    }

    // Timers: nothing ticks while stopped or out of sight, and only the blink while paused

    private void UpdateTimers()
    {
        if (_clock is null || _marquee is null)
        {
            return;
        }

        _clock.Stop();
        TimeSpan? wait = _play switch
        {
            ClassicPlayState.Playing => UntilNextSecond(_shown.PositionAt(DateTimeOffset.UtcNow)),
            ClassicPlayState.Paused => UntilNextSecond(Stopwatch.GetElapsedTime(_pausedAt)),
            _ => null,
        };
        if (_windowShown && wait is { } interval)
        {
            _clock.Interval = interval;
            _clock.Start();
        }

        var scroll = _windowShown && _play == ClassicPlayState.Playing && Marquee.Scrolls(_songLine);
        if (scroll && !_marquee.IsRunning)
        {
            _marquee.Start();
        }
        else if (!scroll && _marquee.IsRunning)
        {
            _marquee.Stop();
        }
    }

    private static TimeSpan UntilNextSecond(TimeSpan time) =>
        TimeSpan.FromTicks(TimeSpan.TicksPerSecond - (time.Ticks % TimeSpan.TicksPerSecond)) + TickSlack;

    private void OnClockTick(DispatcherQueueTimer sender, object args)
    {
        UpdateTimers();
        Invalidate();
    }

    private void OnMarqueeTick(DispatcherQueueTimer sender, object args)
    {
        // A held slider's text stands still; the song carries on where it was afterwards.
        if (MarqueeText().Line != _songLine)
        {
            return;
        }

        _marqueeStep = (_marqueeStep + 1) % (_songLine.Length + Marquee.Gap.Length);
        Invalidate();
    }

    // The cover and the heart

    private void ShowCover(PlayerState state)
    {
        // The cover's address or bytes, or, without one, the album's tile.
        var size = (int)Math.Ceiling(CoverFrame.Width);
        object key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? "tile:" + (state.Album ?? state.Title);
        if ((ReferenceEquals(key, _coverKey) || Equals(key, _coverKey)) && size <= _coverDecodeSize)
        {
            return;
        }

        _coverKey = key;
        _coverDecodeSize = size;
        CoverImage.Opacity = 0;

        // Until (or unless) a cover arrives, a colour tile for the album.
        var name = state.Album ?? state.Title;
        CoverFrame.Background = name is null
            ? _services.Theme.GetBrush("ResonateSurfaceHoverBrush")
            : Artwork.PlaceholderBrush(name);
        CoverGlyph.Visibility = name is null ? Visibility.Visible : Visibility.Collapsed;

        if (state.ArtworkUrl is { } url)
        {
            CoverImage.Source = Artwork.FromUrl(url, size);
        }
        else if (state.ArtworkBytes is { } bytes)
        {
            _ = LoadCoverBytesAsync(bytes, size);
        }
        else
        {
            CoverImage.Source = null;
        }
    }

    private async Task LoadCoverBytesAsync(byte[] bytes, int size)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = size, DecodePixelType = DecodePixelType.Logical };
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        catch (Exception)
        {
            return;
        }

        if (ReferenceEquals(_coverKey, bytes))
        {
            CoverImage.Source = bitmap;
        }
    }

    private void OnCoverOpened(object sender, RoutedEventArgs e)
    {
        CoverImage.Opacity = 1;
        CoverGlyph.Visibility = Visibility.Collapsed;
    }

    /// <summary>With spinning covers on, the cover is a round record that turns while the song plays and the window is shown.</summary>
    private void ApplyCoverLook()
    {
        var theme = _services.Theme;
        var size = CoverFrame.Width;
        var round = theme.SpinningCover;
        CoverFrame.CornerRadius = new CornerRadius(round ? size / 2 : CoverCorner);
        CoverHole.Visibility = round ? Visibility.Visible : Visibility.Collapsed;
        _coverSpin.Update(round && theme.AnimationsEnabled && _loaded, _shown.IsPlaying && _windowShown, (float)size);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyCoverLook();

    /// <summary>The heart for the playing song (Spotify songs only).</summary>
    private void ShowLike(PlayerState state)
    {
        var canLike = state.TrackUri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true;
        LikeButton.Visibility = canLike ? Visibility.Visible : Visibility.Collapsed;
        if (!canLike)
        {
            return;
        }

        var liked = _services.Likes.IsLiked(state.TrackUri);
        LikeButton.Content = liked ? HeartFilledGlyph : HeartGlyph;
        LikeButton.Foreground = _services.Theme.GetBrush(liked ? "ResonateAccentBrush" : "ResonateTextSecondaryBrush");
        var label = liked ? "Remove from Liked Songs" : "Save to Liked Songs";
        AutomationPropertiesHelper.SetName(LikeButton, label);
        ToolTipService.SetToolTip(LikeButton, label);
    }

    private void OnLikesChanged(object? sender, LikeChange change)
    {
        // Raised on any thread.
        if (change.Uri is null || change.Uri == _shown.TrackUri)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_loaded)
                {
                    ShowLike(_shown);
                }
            });
        }
    }

    private void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (_shown.TrackUri is not { } uri)
        {
            return;
        }

        // The player knows the song by its address; that is all liking needs.
        var track = new TrackInfo(
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
        _ = Pages.Lists.TrackActions.SetLikedAsync(track, !_services.Likes.IsLiked(uri));
    }

    // Other changes

    private void OnSkinsChanged(object? sender, EventArgs e)
    {
        _drawn = null;
        _visSequence = -1;
        UpdateVisualiser();
        Invalidate();
    }

    private void OnOptionsChanged(object? sender, EventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        // Size or shade mode may have changed; the bitmaps are only made again when their size does.
        RebuildSurface();
        ShowCover(_shown);
        ApplyCoverLook();
        UpdateVisualiser();
        UpdateTimers();
        Invalidate();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        // Moved to a display with another scale: the skin is drawn again for its pixels.
        if (Math.Abs(sender.RasterizationScale - _raster) > 0.001)
        {
            RebuildSurface();
            ShowCover(_shown);
            ApplyCoverLook();
            UpdateVisualiser();
            Invalidate();
        }
    }

    private void OnEqualizerChanged(object? sender, EventArgs e) => Invalidate();

    private void OnQueueOpenChanged(object? sender, EventArgs e) => Invalidate();

    // The pointer: press draws the control pressed, release over it acts, sliders follow the drag

    private (double X, double Y) SkinPoint(Point position) => (position.X * _raster / _scale, position.Y * _raster / _scale);

    private ClassicControl HitTest(double x, double y)
    {
        var height = _skins.Shaded ? ClassicRenderer.ShadeHeight : ClassicRenderer.Height;
        if (!_skins.IsReady || x < 0 || y < 0 || x >= ClassicRenderer.Width || y >= height)
        {
            return ClassicControl.None;
        }

        return ClassicLayout.HitTest((int)Math.Floor(x), (int)Math.Floor(y), _skins.Shaded);
    }

    private static bool IsSlider(ClassicControl control) =>
        control is ClassicControl.Volume or ClassicControl.Balance or ClassicControl.Seek;

    private void OnSkinPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(SkinView);
        var (x, y) = SkinPoint(point.Position);
        var control = HitTest(x, y);
        if (point.Properties.IsRightButtonPressed)
        {
            if (_skins.IsReady)
            {
                e.Handled = true;
                ShowOptionsMenu(e.GetCurrentPoint(SkinHost).Position);
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed
            || _pressed != ClassicControl.None
            || control is ClassicControl.None or ClassicControl.TitleBar or ClassicControl.Marquee or ClassicControl.About
            || (control == ClassicControl.Seek && !CanSeek(_shown, _play)))
        {
            return;
        }

        e.Handled = true;
        _pressed = control;
        _pointerId = e.Pointer.PointerId;
        _pressedOver = true;
        SkinHost.CapturePointer(e.Pointer);
        if (IsSlider(control))
        {
            BeginDrag(control, x);
        }

        Invalidate();
    }

    /// <summary>Grabbing the thumb keeps it under the pointer where it was taken; pressing the track brings the thumb's middle there.</summary>
    private void BeginDrag(ClassicControl slider, double x)
    {
        if (slider == ClassicControl.Balance)
        {
            // Spotify has no balance; the slider stays in the middle.
            _dragValue = 0;
            return;
        }

        var shaded = _skins.Shaded;
        var current = slider == ClassicControl.Volume
            ? _shown.Volume
            : _shown.Duration > TimeSpan.Zero ? Math.Clamp(_shown.PositionAt(DateTimeOffset.UtcNow) / _shown.Duration, 0, 1) : 0;
        var (left, width) = ClassicLayout.SliderThumb(slider, current, shaded);
        _grabOffset = x >= left && x < left + width ? x - left : width / 2;
        Drag(x);
    }

    private void Drag(double x)
    {
        if (_pressed is not (ClassicControl.Volume or ClassicControl.Seek))
        {
            return;
        }

        var value = ClassicLayout.SliderValue(_pressed, x - _grabOffset, _skins.Shaded);
        _dragValue = value;
        if (_pressed == ClassicControl.Volume)
        {
            // Volume follows the drag (the player sends only the newest value); seeking waits for the release.
            var percent = (int)Math.Round(value * 100);
            if (percent != _sentVolume)
            {
                _sentVolume = percent;
                _ = _player.SetVolumeAsync(value);
            }
        }
    }

    private void OnSkinPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = SkinPoint(e.GetCurrentPoint(SkinView).Position);
        if (_pressed != ClassicControl.None && e.Pointer.PointerId == _pointerId)
        {
            e.Handled = true;
            if (IsSlider(_pressed))
            {
                Drag(x);
            }
            else
            {
                _pressedOver = HitTest(x, y) == _pressed;
            }

            Invalidate();
            return;
        }

        var hovered = HitTest(x, y);
        if (hovered != _hovered)
        {
            _hovered = hovered;
            ToolTipService.SetToolTip(SkinHost, TipFor(hovered));
        }
    }

    private void OnSkinPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed == ClassicControl.None || e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        e.Handled = true;
        var (x, y) = SkinPoint(e.GetCurrentPoint(SkinView).Position);
        var control = _pressed;
        Drag(x);
        var value = _dragValue;
        var over = IsSlider(control) || HitTest(x, y) == control;

        // Cleared before letting go, so the capture-lost event that follows has nothing to undo.
        _pressed = ClassicControl.None;
        _dragValue = null;
        _sentVolume = -1;
        SkinHost.ReleasePointerCapture(e.Pointer);

        if (control == ClassicControl.Seek && value is { } seek && _shown.Duration > TimeSpan.Zero)
        {
            _ = _player.SeekAsync(seek * _shown.Duration);
            Show(_player.State);
        }
        else if (over)
        {
            Act(control, e.GetCurrentPoint(SkinHost).Position);
        }

        Invalidate();
    }

    private void OnSkinPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed != ClassicControl.None && e.Pointer.PointerId == _pointerId)
        {
            _pressed = ClassicControl.None;
            _dragValue = null;
            _sentVolume = -1;
            Invalidate();
        }
    }

    private void OnSkinPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_hovered != ClassicControl.None)
        {
            _hovered = ClassicControl.None;
            ToolTipService.SetToolTip(SkinHost, null);
        }
    }

    private void OnSkinDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Double-clicking the title bar rolls the window up into shade mode, and back.
        var (x, y) = SkinPoint(e.GetPosition(SkinView));
        if (HitTest(x, y) == ClassicControl.TitleBar)
        {
            e.Handled = true;
            _skins.Shaded = !_skins.Shaded;
        }
    }

    private void OnSkinPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Five per cent a notch, louder upwards (a sideways wheel does nothing).
        var wheel = e.GetCurrentPoint(SkinHost).Properties;
        var delta = wheel.IsHorizontalMouseWheel ? 0 : wheel.MouseWheelDelta;
        if (delta == 0 || !_skins.IsReady)
        {
            return;
        }

        e.Handled = true;
        _ = _player.SetVolumeAsync(Math.Clamp(_player.State.Volume + (delta / 120.0 * 0.05), 0, 1));
        Show(_player.State);
    }

    private void Act(ClassicControl control, Point at)
    {
        switch (control)
        {
            case ClassicControl.Previous:
                _ = _player.PreviousAsync();
                break;
            case ClassicControl.Play:
                // As in Winamp, Play while playing starts the song again.
                _ = _shown.IsPlaying ? _player.SeekAsync(TimeSpan.Zero) : _player.PlayAsync();
                break;
            case ClassicControl.Pause when _play != ClassicPlayState.Stopped:
                // Pauses, or carries on; when stopped there is nothing to pause, as in Winamp.
                _ = _player.TogglePlayPauseAsync();
                break;
            case ClassicControl.Stop:
                // Spotify has no stop: pause, and back to the start.
                _ = _player.PauseAsync();
                _ = _player.SeekAsync(TimeSpan.Zero);
                break;
            case ClassicControl.Next:
                _ = _player.NextAsync();
                break;
            case ClassicControl.Shuffle when _shown.CanShuffle:
                _ = _player.SetShuffleAsync(!_shown.Shuffle);
                break;
            case ClassicControl.Repeat when _shown.CanRepeat:
                _ = _player.SetRepeatAsync(MainWindow.NextRepeat(_shown.Repeat));
                break;
            case ClassicControl.Eject:
                App.MainWindow?.FocusSearch();
                return;
            case ClassicControl.Equalizer:
                App.MainWindow?.OpenSettings(SettingsSection.Equalizer);
                return;
            case ClassicControl.Playlist:
                App.MainWindow?.ToggleQueue();
                return;
            case ClassicControl.Time:
                _skins.ShowRemaining = !_skins.ShowRemaining;
                return;
            case ClassicControl.Visualiser:
                _skins.Visualiser = NextVisualiser(_skins.Visualiser);
                return;
            case ClassicControl.Options or ClassicControl.ClutterOptions:
                ShowOptionsMenu(at);
                return;
            case ClassicControl.ClutterVisualiser:
                ShowVisualiserMenu(at);
                return;
            case ClassicControl.ClutterDoubleSize:
                _skins.DoubleSize = !_skins.DoubleSize;
                return;
            case ClassicControl.Minimize:
                App.MainWindow?.Minimize();
                return;
            case ClassicControl.Shade:
                _skins.Shaded = !_skins.Shaded;
                return;
            case ClassicControl.Close:
                _skins.UsesClassicPlayer = false;
                return;
            default:
                return;
        }

        // The player changed its state already; show it now rather than on its event.
        Show(_player.State);
    }

    /// <summary>Spectrum, then oscilloscope, then off, as Winamp cycled them.</summary>
    private static VisualiserMode NextVisualiser(VisualiserMode mode) => mode switch
    {
        VisualiserMode.Spectrum => VisualiserMode.Oscilloscope,
        VisualiserMode.Oscilloscope => VisualiserMode.Off,
        _ => VisualiserMode.Spectrum,
    };

    private string? TipFor(ClassicControl control) => control switch
    {
        ClassicControl.Options or ClassicControl.ClutterOptions => "Skins and options",
        ClassicControl.Minimize => "Minimise",
        ClassicControl.Shade => _skins.Shaded ? "Full size" : "Shade: just the title strip",
        ClassicControl.Close => "Back to the modern player",
        ClassicControl.ClutterDoubleSize => _skins.DoubleSize ? "Normal size" : "Double size",
        ClassicControl.ClutterVisualiser => "Visualiser",
        ClassicControl.Time => "Time played or time left",
        ClassicControl.Visualiser => "Spectrum, oscilloscope or off. It moves for your own music files.",
        ClassicControl.Previous => "Previous",
        ClassicControl.Play => "Play",
        ClassicControl.Pause => "Pause",
        ClassicControl.Stop => "Stop",
        ClassicControl.Next => "Next",
        ClassicControl.Eject => "Search",
        ClassicControl.Shuffle => "Shuffle",
        ClassicControl.Repeat => "Repeat",
        ClassicControl.Equalizer => "Equalizer",
        ClassicControl.Playlist => "Queue",
        ClassicControl.Volume => "Volume",
        ClassicControl.Balance => "Balance (always in the middle)",
        ClassicControl.Seek => "Position",
        _ => null,
    };

    // Menus

    private void ShowOptionsMenu(Point at)
    {
        var menu = new MenuFlyout();
        foreach (var choice in _skins.Choices)
        {
            var file = choice.FileName;
            var item = new RadioMenuFlyoutItem { Text = choice.Label, GroupName = "classic-skins", IsChecked = file == _skins.CurrentFile };
            item.Click += (_, _) => _skins.Select(file);
            menu.Items.Add(item);
        }

        var add = new MenuFlyoutItem { Text = "Add a skin…", IsEnabled = !_services.IsDemo };
        add.Click += (_, _) => _ = _skins.PickAndImportAsync();
        menu.Items.Add(add);
        menu.Items.Add(new MenuFlyoutSeparator());

        var doubled = new ToggleMenuFlyoutItem { Text = "Double size", IsChecked = _skins.DoubleSize };
        doubled.Click += (_, _) => _skins.DoubleSize = !_skins.DoubleSize;
        menu.Items.Add(doubled);
        var remaining = new ToggleMenuFlyoutItem { Text = "Show time left", IsChecked = _skins.ShowRemaining };
        remaining.Click += (_, _) => _skins.ShowRemaining = !_skins.ShowRemaining;
        menu.Items.Add(remaining);
        var visualiser = new MenuFlyoutSubItem { Text = "Visualiser" };
        AddVisualiserItems(visualiser.Items);
        menu.Items.Add(visualiser);
        menu.Items.Add(new MenuFlyoutSeparator());

        var modern = new MenuFlyoutItem { Text = "Modern player" };
        modern.Click += (_, _) => _skins.UsesClassicPlayer = false;
        menu.Items.Add(modern);
        menu.ShowAt(SkinHost, new FlyoutShowOptions { Position = at });
    }

    private void ShowVisualiserMenu(Point at)
    {
        var menu = new MenuFlyout();
        AddVisualiserItems(menu.Items);
        menu.ShowAt(SkinHost, new FlyoutShowOptions { Position = at });
    }

    private void AddVisualiserItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (var (mode, name) in new[]
        {
            (VisualiserMode.Spectrum, "Spectrum"),
            (VisualiserMode.Oscilloscope, "Oscilloscope"),
            (VisualiserMode.Off, "Off"),
        })
        {
            var item = new RadioMenuFlyoutItem { Text = name, GroupName = "classic-visualiser", IsChecked = mode == _skins.Visualiser };
            item.Click += (_, _) => _skins.Visualiser = mode;
            items.Add(item);
        }
    }
}

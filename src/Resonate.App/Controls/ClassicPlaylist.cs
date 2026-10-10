using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Themes.Skins;
using Windows.Foundation;
using Windows.UI;

namespace Resonate.App.Controls;

/// <summary>
/// The mini player's playlist window, framed by the skin as Winamp 2's: the
/// song playing and what plays after it (the queue), in the skin's playlist
/// font and colours, with the list's running time and the song's time.
/// Right-click a song for its menu. It can be made taller by its bottom
/// right corner, or rolled up. Built in code; it reads the queue only while
/// it is in the mini player.
/// </summary>
internal sealed partial class ClassicPlaylist : Grid
{
    /// <summary>Spotify shares about 20 songs ahead; local files may have many more.</summary>
    private const int MaxRows = 200;

    /// <summary>Winamp's playlist font size, in skin pixels (rows are 13 tall).</summary>
    private const double RowFontSize = 9;

    /// <summary>The wheel scrolls three songs a notch.</summary>
    private const int WheelRows = 3;

    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(600);

    private readonly MiniPlayerWindow _mini;
    private readonly AppServices _services = App.Services;
    private readonly SkinLibrary _skins;
    private readonly PlayerRouter _player;
    private readonly Image _view = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _rowsHost = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly List<(TextBlock Name, TextBlock Time)> _rowViews = [];
    private readonly SkinSurface _surface;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _clock;

    private bool _loaded;
    private bool _windowShown = true;
    private bool _windowActive = true;
    private bool _renderQueued;
    private int _updateQueued;
    private PlaylistView? _drawn;

    // What it shows: the song playing first, then what follows
    private PlayerState _shown = PlayerState.Empty;
    private TrackInfo? _nowTrack;
    private IReadOnlyList<TrackInfo> _upcoming = [];
    private string? _queueKey;
    private int _firstRow;
    private int? _liveHeight;
    private CancellationTokenSource? _loading;
    private int _version;
    private Skin? _brushesFor;
    private SolidColorBrush? _normalBrush;
    private SolidColorBrush? _currentBrush;
    private FontFamily? _font;

    // The pointer
    private PlaylistControl _pressed;
    private bool _pressedOver;
    private uint _pointerId;
    private bool _moving;
    private double _grabOffset;
    private double _gripStart;
    private PlaylistControl _hovered;

    public ClassicPlaylist(MiniPlayerWindow mini)
    {
        _mini = mini;
        _skins = _services.Skins;
        _player = _services.Player;
        _surface = new SkinSurface(_view);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        AutomationPropertiesHelper.SetName(this, "Playlist");
        Children.Add(_view);
        Children.Add(_rowsHost);

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = RefreshDelay;
        _refreshTimer.IsRepeating = false;
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.IsRepeating = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCanceled;
        PointerExited += OnPointerExited;
        DoubleTapped += OnDoubleTapped;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>The height the window has now (while its corner is dragged, before it is kept), in skin pixels.</summary>
    public int ShownHeight => _liveHeight ?? _skins.MiniPlaylistHeight;

    private bool Shaded => _skins.MiniPlaylistShaded;

    private int WindowHeight => Shaded ? PlaylistLayout.ShadeHeight : PlaylistLayout.SnapHeight(ShownHeight);

    private int RowCount => Math.Min((_shown.HasTrack ? 1 : 0) + _upcoming.Count, MaxRows);

    private int VisibleRows => Shaded ? 0 : PlaylistLayout.VisibleRows(WindowHeight);

    /// <summary>Sizes the window for the mini player's scale (the mini player calls it whenever its size, the display or this window's height changes).</summary>
    public void SetScale(int scale, double raster)
    {
        if (_surface.Resize(PlaylistLayout.Width, WindowHeight, scale, raster))
        {
            _drawn = null;
            LayOutRows();
            Invalidate();
        }
    }

    public void SetWindowActive(bool active)
    {
        if (_windowActive != active)
        {
            _windowActive = active;
            Invalidate();
        }
    }

    public void SetWindowShown(bool shown)
    {
        if (_windowShown != shown)
        {
            _windowShown = shown;
            UpdateClock();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        // Only while loaded: a timer's handler that holds this window would keep it (and the whole mini player) for good.
        _refreshTimer.Tick += OnRefreshTick;
        _clock.Tick += OnClockTick;
        _skins.Changed += OnSkinsChanged;
        _skins.MiniOptionsChanged += OnMiniOptionsChanged;
        _player.StateChanged += OnStateChanged;
        _player.QueueChanged += OnQueueChanged;
        _queueKey = null;
        Show(_player.State);
        _ = RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        _skins.Changed -= OnSkinsChanged;
        _skins.MiniOptionsChanged -= OnMiniOptionsChanged;
        _player.StateChanged -= OnStateChanged;
        _player.QueueChanged -= OnQueueChanged;
        _refreshTimer.Stop();
        _clock.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        _clock.Tick -= OnClockTick;
        CancelLoading();
        _version++;
        _pressed = PlaylistControl.None;
        _moving = false;
        _liveHeight = null;
    }

    private void OnRefreshTick(DispatcherQueueTimer sender, object args) => _ = RefreshAsync();

    private void OnClockTick(DispatcherQueueTimer sender, object args) => Invalidate();

    private void OnSkinsChanged(object? sender, EventArgs e)
    {
        _drawn = null;
        ShowRows();
        Invalidate();
    }

    private void OnMiniOptionsChanged(object? sender, EventArgs e)
    {
        // Rolled up or down, or a new height: the rows follow (the mini player resizes the picture).
        LayOutRows();
        UpdateClock();
        Invalidate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; show the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            if (_loaded)
            {
                Show(_player.State);
            }
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_loaded)
            {
                RefreshSoon();
            }
        });

    private void Show(PlayerState state)
    {
        _shown = state;

        // What plays next changes with the song, the player, shuffle and repeat.
        var key = string.Join('|', _player.ActiveSource, state.TrackUri ?? state.Title, state.ContextUri, state.Shuffle, state.Repeat);
        if (key != _queueKey)
        {
            var first = _queueKey is null;
            _queueKey = key;
            if (!first)
            {
                RefreshSoon();
            }
        }

        ShowRows();
        UpdateClock();
        Invalidate();
    }

    private void RefreshSoon()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>Reads what plays next from the player that plays now (as the queue pane does).</summary>
    private async Task RefreshAsync()
    {
        _refreshTimer.Stop();
        CancelLoading();
        var version = ++_version;
        if (!_loaded)
        {
            return;
        }

        if (_player.ActiveSource == PlaybackSource.LocalFiles)
        {
            _nowTrack = null;
            ShowUpcoming(_player.Local.Upcoming);
            return;
        }

        var loading = new CancellationTokenSource();
        _loading = loading;
        try
        {
            var token = loading.Token;
            var queue = await _services.Player.Spotify.GetQueueAsync().WaitAsync(token);
            if (version == _version)
            {
                var now = TrackInfo.From(queue.CurrentlyPlaying);
                _nowTrack = now is not null && now.Uri == _shown.TrackUri ? now : null;
                ShowUpcoming(QueuePreview.Upcoming(queue));
            }
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Closed, or a newer read started.
        }
        catch (Exception)
        {
            // Not signed in, offline or out of quota: the song playing still shows.
            if (version == _version)
            {
                ShowUpcoming([]);
            }
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                _loading = null;
            }

            loading.Dispose();
        }
    }

    private void CancelLoading()
    {
        if (_loading is { } loading)
        {
            _loading = null;
            loading.Cancel();
        }
    }

    private void ShowUpcoming(IReadOnlyList<TrackInfo> upcoming)
    {
        _upcoming = upcoming;
        ShowRows();
        Invalidate();
    }

    /// <summary>The song at a row of the list (0 is the song playing, when there is one).</summary>
    private TrackInfo? TrackAt(int index)
    {
        if (_shown.HasTrack)
        {
            if (index == 0)
            {
                return NowTrack();
            }

            index--;
        }

        return index >= 0 && index < _upcoming.Count ? _upcoming[index] : null;
    }

    /// <summary>The song playing: what the queue said about it, or what the player knows.</summary>
    private TrackInfo? NowTrack()
    {
        var state = _shown;
        if (state.Title is null)
        {
            return null;
        }

        var uri = state.TrackUri;
        if (_nowTrack is { } known && known.Uri == uri)
        {
            return known;
        }

        var isLocal = uri?.StartsWith("spotify:local:", StringComparison.Ordinal) == true;
        return new TrackInfo(
            uri,
            state.Title,
            state.Artists ?? string.Empty,
            state.Album ?? string.Empty,
            null,
            state.Duration,
            state.ArtworkUrl,
            state.ArtworkUrl,
            false,
            state.Source == PlaybackSource.Spotify && uri is not null && !isLocal)
        {
            IsLocal = isLocal,
        };
    }

    // The rows: the app's own text over the skin's frame, in the skin's playlist font and colours

    /// <summary>Makes as many rows as fit, sized for the scale, over the list's place in the frame.</summary>
    private void LayOutRows()
    {
        var scale = _surface.Scale / _surface.Raster;
        var (x, y, width, height) = PlaylistLayout.ListArea(WindowHeight);
        var rows = VisibleRows;
        _rowsHost.Visibility = rows > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rowsHost.Margin = new Thickness(x * scale, y * scale, 0, 0);
        _rowsHost.Width = width * scale;
        _rowsHost.Height = height * scale;
        _rowsHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width * scale, height * scale) };

        while (_rowViews.Count > rows)
        {
            var last = _rowViews[^1];
            _rowsHost.Children.Remove(last.Name);
            _rowsHost.Children.Remove(last.Time);
            _rowViews.RemoveAt(_rowViews.Count - 1);
        }

        while (_rowViews.Count < rows)
        {
            var name = RowText();
            var time = RowText();
            time.TextAlignment = TextAlignment.Right;
            _rowsHost.Children.Add(name);
            _rowsHost.Children.Add(time);
            _rowViews.Add((name, time));
        }

        var rowHeight = PlaylistLayout.RowHeight * scale;
        var timeWidth = 40 * scale;
        for (var i = 0; i < _rowViews.Count; i++)
        {
            var (name, time) = _rowViews[i];
            foreach (var text in new[] { name, time })
            {
                text.FontSize = RowFontSize * scale;
                text.LineHeight = rowHeight;
                text.Height = rowHeight;
                Canvas.SetTop(text, i * rowHeight);
            }

            Canvas.SetLeft(name, 2 * scale);
            name.Width = Math.Max((width - 4) * scale - timeWidth, 0);
            Canvas.SetLeft(time, (width - 2) * scale - timeWidth);
            time.Width = timeWidth;
        }

        ShowRows();
    }

    private static TextBlock RowText() => new()
    {
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        IsTextScaleFactorEnabled = false,
        FontWeight = FontWeights.Normal,
    };

    /// <summary>Writes the songs into the rows from the first one scrolled to.</summary>
    private void ShowRows()
    {
        if (!_skins.IsReady)
        {
            return;
        }

        UpdateBrushes();
        var count = RowCount;
        var visible = VisibleRows;
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(count - visible, 0));
        for (var i = 0; i < _rowViews.Count; i++)
        {
            var (name, time) = _rowViews[i];
            var index = _firstRow + i;
            var track = index < count ? TrackAt(index) : null;
            name.Text = track is null ? string.Empty : $"{index + 1}. {Line(track)}";
            time.Text = track is null || track.Duration <= TimeSpan.Zero ? string.Empty : PlaylistLayout.FormatTime(track.Duration);
            var brush = index == 0 && _shown.HasTrack ? _currentBrush : _normalBrush;
            name.Foreground = brush;
            time.Foreground = brush;
            name.FontFamily = _font;
            time.FontFamily = _font;
        }
    }

    private static string Line(TrackInfo track) =>
        string.IsNullOrEmpty(track.Artists) ? track.Title : $"{track.Artists} - {track.Title}";

    private void UpdateBrushes()
    {
        var skin = _skins.Current;
        if (ReferenceEquals(skin, _brushesFor))
        {
            return;
        }

        _brushesFor = skin;
        _normalBrush = new SolidColorBrush(Opaque(skin.Playlist.Normal));
        _currentBrush = new SolidColorBrush(Opaque(skin.Playlist.Current));

        // The skin names a font (pledit.txt); one Windows lacks falls back to its usual font.
        _font = new FontFamily(string.IsNullOrWhiteSpace(skin.Playlist.Font) ? "Arial" : skin.Playlist.Font);
    }

    private static Color Opaque(uint rgb) =>
        Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // The frame

    private void UpdateClock()
    {
        var tick = _loaded && _windowShown && !Shaded && _shown.IsPlaying;
        if (tick && !_clock.IsRunning)
        {
            _clock.Start();
        }
        else if (!tick && _clock.IsRunning)
        {
            _clock.Stop();
        }
    }

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
        if (!_loaded || !_skins.IsReady || _surface.Frame is not { } frame)
        {
            return;
        }

        var view = BuildView();
        if (view == _drawn || frame.Height != (view.Shaded ? PlaylistLayout.ShadeHeight : PlaylistLayout.SnapHeight(view.Height)))
        {
            return;
        }

        var skin = _skins.Current;
        try
        {
            PlaylistRenderer.Render(skin, view, frame);
        }
        catch (Exception ex) when (!skin.IsBuiltIn)
        {
            // A skin the user added is untrusted: back to the built-in one rather than fail.
            _skins.ReportBroken(skin, ex);
            return;
        }

        _surface.Present();
        _drawn = view;
    }

    private PlaylistView BuildView()
    {
        var state = _shown;
        var count = RowCount;
        var total = TimeSpan.Zero;
        for (var i = 0; i < count; i++)
        {
            total += TrackAt(i)?.Duration ?? TimeSpan.Zero;
        }

        var duration = state.Duration > TimeSpan.Zero ? state.Duration : TimeSpan.Zero;
        var elapsed = TimeSpan.FromSeconds(Math.Floor(state.PositionAt(DateTimeOffset.UtcNow).TotalSeconds));
        return new PlaylistView
        {
            Height = ShownHeight,
            Shaded = Shaded,
            WindowActive = _windowActive,
            // The handle and the corner stay pressed wherever the pointer goes; buttons only while it is over them.
            Pressed = _pressed is PlaylistControl.Scroll or PlaylistControl.Grip || _pressedOver ? _pressed : PlaylistControl.None,
            Scroll = PlaylistLayout.ScrollOf(_firstRow, count, VisibleRows),

            // Winamp: the selected songs' length over the whole list's; here the song playing over everything listed.
            RunningTime = count == 0 ? string.Empty : $"{PlaylistLayout.FormatTime(duration)}/{PlaylistLayout.FormatTime(total)}",
            TrackTime = state.HasTrack ? PlaylistLayout.FormatTime(elapsed) : string.Empty,
            ShadeTitle = state.HasTrack ? $"1. {(string.IsNullOrEmpty(state.Artists) ? state.Title : $"{state.Artists} - {state.Title}")}" : string.Empty,
            ShadeTime = duration > TimeSpan.Zero ? PlaylistLayout.FormatTime(duration) : string.Empty,
        };
    }

    // The pointer

    private PlaylistControl HitTest(Point position)
    {
        var (x, y) = _surface.SkinPoint(position);
        return _skins.IsReady ? PlaylistLayout.HitTest((int)Math.Floor(x), (int)Math.Floor(y), ShownHeight, Shaded) : PlaylistControl.None;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view);
        var control = HitTest(point.Position);
        var (x, y) = _surface.SkinPoint(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            var at = e.GetCurrentPoint(this).Position;
            if (control == PlaylistControl.List && TrackAtPoint(y) is { } track)
            {
                TrackActions.BuildMenu(track).ShowAt(this, new FlyoutShowOptions { Position = at });
            }
            else
            {
                ShowListMenu(at);
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed || _pressed != PlaylistControl.None || _moving)
        {
            return;
        }

        e.Handled = true;
        _pointerId = e.Pointer.PointerId;
        CapturePointer(e.Pointer);
        if (control is PlaylistControl.TitleBar or PlaylistControl.None or PlaylistControl.List)
        {
            // The title bar, the list and whatever does nothing move the mini player, as Winamp's frame did.
            _moving = true;
            _mini.BeginMove();
            return;
        }

        _pressed = control;
        _pressedOver = true;
        if (control == PlaylistControl.Scroll)
        {
            // Grabbing the handle keeps it under the pointer; pressing the bar brings the handle's middle there.
            var count = RowCount;
            var top = PlaylistLayout.ScrollHandleTop(WindowHeight, PlaylistLayout.ScrollOf(_firstRow, count, VisibleRows));
            _grabOffset = y >= top && y < top + PlaylistLayout.ScrollHandleHeight ? y - top : PlaylistLayout.ScrollHandleHeight / 2;
            DragScroll(y);
        }
        else if (control == PlaylistControl.Grip)
        {
            _gripStart = y - WindowHeight;
            _liveHeight = WindowHeight;
        }

        Invalidate();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_view).Position;
        if (_moving && e.Pointer.PointerId == _pointerId)
        {
            e.Handled = true;
            _mini.Move();
            return;
        }

        if (_pressed != PlaylistControl.None && e.Pointer.PointerId == _pointerId)
        {
            e.Handled = true;
            var (_, y) = _surface.SkinPoint(position);
            switch (_pressed)
            {
                case PlaylistControl.Scroll:
                    DragScroll(y);
                    break;
                case PlaylistControl.Grip:
                    DragGrip(y);
                    break;
                default:
                    _pressedOver = HitTest(position) == _pressed;
                    break;
            }

            Invalidate();
            return;
        }

        var hovered = HitTest(position);
        if (hovered != _hovered)
        {
            _hovered = hovered;
            ToolTipService.SetToolTip(this, TipFor(hovered));
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        if (_moving)
        {
            e.Handled = true;
            _moving = false;
            ReleasePointerCapture(e.Pointer);
            _mini.EndMove();
            return;
        }

        if (_pressed == PlaylistControl.None)
        {
            return;
        }

        e.Handled = true;
        var control = _pressed;
        var over = HitTest(e.GetCurrentPoint(_view).Position) == control;
        var at = e.GetCurrentPoint(this).Position;
        _pressed = PlaylistControl.None;
        ReleasePointerCapture(e.Pointer);
        if (control == PlaylistControl.Grip)
        {
            KeepHeight();
        }
        else if (over)
        {
            Act(control, at);
        }

        Invalidate();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        if (_moving)
        {
            _moving = false;
            _mini.EndMove();
        }

        if (_pressed == PlaylistControl.Grip)
        {
            KeepHeight();
        }

        if (_pressed != PlaylistControl.None)
        {
            _pressed = PlaylistControl.None;
            Invalidate();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_hovered != PlaylistControl.None)
        {
            _hovered = PlaylistControl.None;
            ToolTipService.SetToolTip(this, null);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var position = e.GetPosition(_view);
        var control = HitTest(position);
        if (control == PlaylistControl.TitleBar)
        {
            // Double-clicking the title bar rolls the window up, and back.
            e.Handled = true;
            _skins.MiniPlaylistShaded = !_skins.MiniPlaylistShaded;
        }
        else if (control == PlaylistControl.List && _shown.HasTrack && TrackRowAt(_surface.SkinPoint(position).Y) == 0)
        {
            // The song playing plays from the start, as Winamp's double-click did (Spotify can't jump into its queue).
            e.Handled = true;
            _ = _player.SeekAsync(TimeSpan.Zero);
            if (!_shown.IsPlaying)
            {
                _ = _player.PlayAsync();
            }
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var wheel = e.GetCurrentPoint(this).Properties;
        var delta = wheel.IsHorizontalMouseWheel ? 0 : wheel.MouseWheelDelta;
        if (delta == 0 || Shaded)
        {
            return;
        }

        e.Handled = true;
        ScrollTo(_firstRow - (Math.Sign(delta) * WheelRows));
    }

    /// <summary>The list's row (counted from the top of the list, not of the window) at a height in skin pixels, or -1.</summary>
    private int TrackRowAt(double y)
    {
        var row = PlaylistLayout.RowAt((int)Math.Floor(y), WindowHeight);
        var index = row < 0 ? -1 : _firstRow + row;
        return index < RowCount ? index : -1;
    }

    private TrackInfo? TrackAtPoint(double y) => TrackRowAt(y) is var index and >= 0 ? TrackAt(index) : null;

    private void ScrollTo(int firstRow)
    {
        var clamped = Math.Clamp(firstRow, 0, Math.Max(RowCount - VisibleRows, 0));
        if (clamped != _firstRow)
        {
            _firstRow = clamped;
            ShowRows();
            Invalidate();
        }
    }

    private void DragScroll(double y)
    {
        var scroll = PlaylistLayout.ScrollAt(WindowHeight, y - _grabOffset);
        ScrollTo(PlaylistLayout.FirstRow(scroll, RowCount, VisibleRows));
    }

    private void DragGrip(double y)
    {
        var height = PlaylistLayout.SnapHeight((int)Math.Round(y - _gripStart));
        if (height != _liveHeight)
        {
            _liveHeight = height;
            _mini.Arrange(keepOnScreen: false);
            LayOutRows();
            Invalidate();
        }
    }

    private void KeepHeight()
    {
        if (_liveHeight is { } height)
        {
            _liveHeight = null;
            _skins.MiniPlaylistHeight = height;
            _mini.Arrange(keepOnScreen: true);
        }
    }

    private void Act(PlaylistControl control, Point at)
    {
        var playing = _shown.IsPlaying;
        switch (control)
        {
            case PlaylistControl.Shade:
                _skins.MiniPlaylistShaded = !_skins.MiniPlaylistShaded;
                break;
            case PlaylistControl.Close:
                _skins.MiniPlaylist = false;
                break;
            case PlaylistControl.Add or PlaylistControl.Eject:
                // Songs are found in Search, in the full window.
                _mini.Leave();
                App.MainWindow?.FocusSearch();
                break;
            case PlaylistControl.Misc or PlaylistControl.ListOptions:
                ShowListMenu(at);
                break;
            case PlaylistControl.Previous:
                _ = _player.PreviousAsync();
                break;
            case PlaylistControl.Play:
                _ = playing ? _player.SeekAsync(TimeSpan.Zero) : _player.PlayAsync();
                break;
            case PlaylistControl.Pause when _shown.HasTrack:
                _ = _player.TogglePlayPauseAsync();
                break;
            case PlaylistControl.Stop:
                // Spotify has no stop: pause, and back to the start.
                _ = _player.PauseAsync();
                _ = _player.SeekAsync(TimeSpan.Zero);
                break;
            case PlaylistControl.Next:
                _ = _player.NextAsync();
                break;
            default:
                break;
        }
    }

    private string? TipFor(PlaylistControl control) => control switch
    {
        PlaylistControl.List => "Right-click a song for more",
        PlaylistControl.Add => "Add songs: find them in the full window",
        PlaylistControl.Remove or PlaylistControl.Select => "The queue can only be added to",
        PlaylistControl.Misc or PlaylistControl.ListOptions => "More",
        PlaylistControl.Grip => "Drag to make the list taller or shorter",
        PlaylistControl.Shade => Shaded ? "Full size" : "Shade: just the title strip",
        PlaylistControl.Close => "Close the playlist",
        PlaylistControl.Previous => "Previous",
        PlaylistControl.Play => "Play",
        PlaylistControl.Pause => "Pause",
        PlaylistControl.Stop => "Stop",
        PlaylistControl.Next => "Next",
        PlaylistControl.Eject => "Find songs in the full window",
        _ => null,
    };

    private void ShowListMenu(Point at)
    {
        var menu = new MenuFlyout();
        var refresh = new MenuFlyoutItem { Text = "Read the queue again" };
        refresh.Click += (_, _) => _ = RefreshAsync();
        menu.Items.Add(refresh);
        var queue = new MenuFlyoutItem { Text = "Open the queue in the full window" };
        queue.Click += (_, _) =>
        {
            _mini.Leave();
            if (App.MainWindow is { IsQueueOpen: false } window)
            {
                window.ToggleQueue();
            }
        };
        menu.Items.Add(queue);
        menu.ShowAt(this, new FlyoutShowOptions { Position = at });
    }
}

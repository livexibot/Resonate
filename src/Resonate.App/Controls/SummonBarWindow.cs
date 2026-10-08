using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Windows.Graphics;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>What Enter does with a result of the summon bar.</summary>
internal enum SummonAction
{
    /// <summary>Enter: play it.</summary>
    Play,

    /// <summary>Shift+Enter: add it to the queue.</summary>
    Queue,

    /// <summary>Ctrl+Enter: open it in Resonate.</summary>
    Open,
}

/// <summary>A command of the summon bar as Resonate's command palette, such as "Next song".</summary>
/// <param name="ShowsWindow">It opens something in Resonate's window, which then comes to the front.</param>
internal sealed record SummonCommand(string Name, string Glyph, string Keywords, bool ShowsWindow, Action Run);

/// <summary>
/// The summon bar (a built-in plugin): a slim search box over whatever is
/// on screen, opened by the user's shortcut from any app or by Ctrl+K in
/// Resonate. Your library first, then Local Files, Resonate's commands and,
/// after a pause in typing, Spotify's results. Enter plays, Shift+Enter adds
/// to the queue, Ctrl+Enter opens in Resonate, Esc closes and gives the
/// keyboard back to the app that had it. A second window, made on first use
/// and kept hidden between uses; built in code.
/// </summary>
internal sealed partial class SummonBarWindow : Window
{
    private const double BarWidth = 640;
    private const double BarHeight = 468;
    private const double RowHeight = 52;
    private const int CoverPixels = 40;
    private const int LibraryLimit = 8;
    private const int LocalLimit = 4;
    private const int CommandLimit = 4;
    private const int SpotifyCacheLimit = 64;

    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(250);

    private readonly AppServices _services;
    private readonly MainWindow _main;
    private readonly Grid _root = new();
    private readonly TextBox _box = new();
    private readonly StackPanel _ownPanel = new();
    private readonly StackPanel _spotifyPanel = new();
    private readonly ScrollViewer _scroller = new();
    private readonly List<Row> _rows = [];
    private readonly Dictionary<string, SearchMatches> _spotifyCache = new(StringComparer.Ordinal);
    private QuickSearchIndex _index = QuickSearchIndex.Empty;
    private CancellationTokenSource? _search;
    private nint _previousWindow;
    private int _selected;
    private int _spotifyStart;
    private bool _hiding;
    private bool _focusOnActivate;

    public SummonBarWindow(AppServices services, MainWindow main)
    {
        _services = services;
        _main = main;
        Title = "Resonate";
        ExtendsContentIntoTitleBar = true;

        // A borderless box above other windows, left out of Alt+Tab and the taskbar.
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        var corner = 2; // DWMWCP_ROUND: Windows 11 rounds the box; Windows 10 ignores it.
        _ = DwmSetWindowAttribute(Hwnd, 33, ref corner, sizeof(int));

        Content = BuildContent();
        Activated += OnActivated;
    }

    public bool IsOpen => AppWindow.IsVisible;

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Shows the bar, empty, near the top of the screen with the mouse; <paramref name="previousWindow"/> gets the keyboard back after.</summary>
    public void Summon(nint previousWindow)
    {
        _previousWindow = previousWindow;
        ApplyLook();
        PlaceOnScreen();
        _ = RefreshIndexAsync();
        _box.Text = string.Empty;
        ShowResults();

        _focusOnActivate = true;
        AppWindow.Show(true);
        Activate();

        // Allowed: Resonate had the last input (the shortcut).
        SetForegroundWindow(Hwnd);
        _box.Focus(FocusState.Programmatic);
        Rise();
    }

    /// <summary>Hides the bar; with <paramref name="returnFocus"/>, the app the user was in gets the keyboard back.</summary>
    public void Dismiss(bool returnFocus)
    {
        if (!AppWindow.IsVisible || _hiding)
        {
            return;
        }

        _hiding = true;
        _search?.Cancel();
        AppWindow.Hide();
        _hiding = false;
        if (returnFocus && _previousWindow != 0 && IsWindow(_previousWindow))
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // A click elsewhere: that window has the keyboard already.
            Dismiss(returnFocus: false);
        }
        else if (_focusOnActivate)
        {
            _focusOnActivate = false;
            _box.Focus(FocusState.Programmatic);
        }
    }

    private Grid BuildContent()
    {
        var resources = Application.Current.Resources;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.PreviewKeyDown += OnKeyDown;

        var search = new Grid { Padding = new Thickness(18, 12, 12, 10), ColumnSpacing = 12 };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        search.Children.Add(new FontIcon { Glyph = "", FontSize = 18, Foreground = _services.Theme.GetBrush("ResonateTextSecondaryBrush") });
        _box.PlaceholderText = "Play something…";
        _box.FontSize = 18;
        _box.BorderThickness = new Thickness(0);
        _box.Background = _services.Theme.GetBrush("ResonateTransparentBrush");
        AutomationProperties.SetName(_box, "Search your library and Spotify");
        _box.TextChanged += (_, _) => ShowResults();
        Grid.SetColumn(_box, 1);
        search.Children.Add(_box);
        _root.Children.Add(search);

        var list = new StackPanel { Padding = new Thickness(8, 0, 8, 8) };
        list.Children.Add(_ownPanel);
        list.Children.Add(_spotifyPanel);
        _scroller.Content = list;
        _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Grid.SetRow(_scroller, 1);
        _root.Children.Add(_scroller);

        var foot = new TextBlock
        {
            Margin = new Thickness(18, 8, 18, 10),
            Style = (Style)resources["ResonateCaptionTextStyle"],
            Text = "Enter plays · Shift+Enter adds to the queue · Ctrl+Enter opens · Esc closes · for Spotify",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(foot, 2);
        _root.Children.Add(foot);
        return _root;
    }

    /// <summary>The look's colours, made opaque (the bar has no window behind it), and its light or dark mode.</summary>
    private void ApplyLook()
    {
        var palette = _services.Theme.Palette;
        _root.RequestedTheme = palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;
        _root.Background = palette.Surface.Over(palette.Background).ToBrush();
        _root.BorderBrush = _services.Theme.GetBrush("ResonateBorderBrush");
        _root.BorderThickness = new Thickness(1);
    }

    /// <summary>Centred near the top of the screen the mouse is on.</summary>
    private void PlaceOnScreen()
    {
        GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
        var width = (int)Math.Min(BarWidth * scale, area.Width - 32);
        var height = (int)Math.Min(BarHeight * scale, area.Height - 32);
        var x = area.X + ((area.Width - width) / 2);
        var y = area.Y + (int)Math.Min(area.Height * 0.18, area.Height - height);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>Fades in with a short rise (none while Windows' animations are off).</summary>
    private void Rise()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_root);
        if (!_services.Theme.AnimationsEnabled)
        {
            visual.Opacity = 1;
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(_root, true);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(140);
        visual.StartAnimation("Opacity", fade);
        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 8, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Translation", rise);
    }

    // ---- Results ----

    /// <summary>Builds the library's index again in the background; until it is ready the last one answers.</summary>
    private async Task RefreshIndexAsync()
    {
        var playlists = _main.Playlists.Select(p => p.Playlist).ToList();
        var library = _services.Library;
        var localFiles = _services.LocalFiles;
        var withLocal = localFiles.ShowInSidebar;
        try
        {
            _index = await Task.Run(async () =>
            {
                var liked = library.GetStoredLikedSongs() ?? [];
                var local = withLocal ? await localFiles.GetTracksAsync(CancellationToken.None).ConfigureAwait(false) : [];
                return QuickSearchIndex.Build(playlists, liked, local);
            });
        }
        catch (Exception)
        {
            // The last index (or none) answers; the next summon tries again.
            return;
        }

        if (AppWindow.IsVisible)
        {
            ShowResults();
        }
    }

    /// <summary>What is typed, at once from memory; Spotify's results follow after a pause in typing.</summary>
    private void ShowResults()
    {
        var query = _box.Text;
        _ownPanel.Children.Clear();
        _rows.Clear();

        AddSection(_ownPanel, "YOUR LIBRARY", _index.SearchLibrary(query, LibraryLimit).Select(item => new Row(item, null)));
        AddSection(_ownPanel, "LOCAL FILES", _index.SearchLocal(query, LocalLimit).Select(item => new Row(item, null)));
        var tokens = QuickSearchIndex.Tokens(query);
        if (tokens.Length > 0)
        {
            AddSection(
                _ownPanel,
                "COMMANDS",
                _main.SummonCommands()
                    .Where(c => QuickSearchIndex.Score(QuickSearchIndex.Normalize(c.Name), QuickSearchIndex.Normalize(c.Name + " " + c.Keywords), tokens) > 0)
                    .Take(CommandLimit)
                    .Select(c => new Row(null, c)));
        }

        _spotifyStart = _rows.Count;
        _selected = 0;
        ShowSpotify(query);
        Select(0);
    }

    private void ShowSpotify(string query)
    {
        _spotifyPanel.Children.Clear();
        _rows.RemoveRange(_spotifyStart, _rows.Count - _spotifyStart);
        _search?.Cancel();
        var key = string.Join(' ', QuickSearchIndex.Tokens(query));
        if (key.Length < 2)
        {
            return;
        }

        if (_spotifyCache.TryGetValue(key, out var cached))
        {
            AddSpotifyRows(cached);
            return;
        }

        AddHeader(_spotifyPanel, "SPOTIFY", "Searching…");
        _search = new CancellationTokenSource();
        _ = SearchSpotifyAsync(query, key, _search.Token);
    }

    private async Task SearchSpotifyAsync(string query, string key, CancellationToken token)
    {
        try
        {
            // Every search counts against Spotify's quota, so only after a pause in typing.
            await Task.Delay(TypingPause, token);
            var library = _services.Library;
            var results = await Task.Run(() => library.SearchAsync(query, token), token);
            if (_spotifyCache.Count >= SpotifyCacheLimit)
            {
                _spotifyCache.Clear();
            }

            _spotifyCache[key] = results;
            if (!token.IsCancellationRequested)
            {
                var selected = _selected;
                _spotifyPanel.Children.Clear();
                AddSpotifyRows(results);
                Select(Math.Min(selected, _rows.Count - 1));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _spotifyPanel.Children.Clear();
                AddHeader(
                    _spotifyPanel,
                    "SPOTIFY",
                    ex is SpotifyApiException { IsQuotaExceeded: true } or SpotifyApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
                        ? "Spotify search is busy; your library still works."
                        : PlayerController.DescribeError(ex));
            }
        }
    }

    private void AddSpotifyRows(SearchMatches results)
    {
        var rows = new List<Row>();
        rows.AddRange(results.Tracks.Where(t => t.IsPlayable).Take(5)
            .Select(t => new Row(new QuickItem(QuickKind.Song, t.Title, t.Artists, t.SmallImageUrl, t.Uri, t.Id) { Track = t }, null)));
        rows.AddRange(results.Artists.Take(3)
            .Select(a => new Row(new QuickItem(QuickKind.Artist, a.Name, "Artist", ImagePicker.Pick(a.Images, 64), a.Uri, a.Id), null)));
        rows.AddRange(results.Albums.Where(a => a.Uri is not null).Take(3)
            .Select(a => new Row(new QuickItem(QuickKind.Album, a.Name, "Album · " + string.Join(", ", a.Artists?.Select(r => r.Name) ?? []), ImagePicker.Pick(a.Images, 64), a.Uri, a.Id), null)));
        rows.AddRange(results.Playlists.Take(3)
            .Select(p => new Row(new QuickItem(QuickKind.Playlist, p.Name, "Playlist · " + (p.Owner?.DisplayName ?? "Spotify"), ImagePicker.Pick(p.Images, 64), p.Uri, p.Id), null)));
        if (rows.Count == 0)
        {
            AddHeader(_spotifyPanel, "SPOTIFY", "Nothing found.");
            return;
        }

        AddSection(_spotifyPanel, "SPOTIFY", rows);
    }

    private void AddSection(StackPanel panel, string name, IEnumerable<Row> rows)
    {
        var header = false;
        foreach (var row in rows)
        {
            if (!header)
            {
                header = true;
                AddHeader(panel, name, null);
            }

            var index = _rows.Count;
            row.Element = BuildRow(row, index);
            _rows.Add(row);
            panel.Children.Add(row.Element);
        }
    }

    private static void AddHeader(StackPanel panel, string name, string? note)
    {
        var resources = Application.Current.Resources;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 12, 10, 4) };
        header.Children.Add(new TextBlock { Text = name, Style = (Style)resources["ResonateEyebrowTextStyle"] });
        if (note is not null)
        {
            header.Children.Add(new TextBlock { Text = note, Style = (Style)resources["ResonateCaptionTextStyle"] });
        }

        panel.Children.Add(header);
    }

    private Grid BuildRow(Row row, int index)
    {
        var resources = Application.Current.Resources;
        var theme = _services.Theme;
        var grid = new Grid
        {
            Height = RowHeight,
            Padding = new Thickness(10, 6, 10, 6),
            ColumnSpacing = 12,
            CornerRadius = new CornerRadius(theme.Palette.CornerSmall),
            Background = theme.GetBrush("ResonateTransparentBrush"),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CoverPixels) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = row.Item?.Title ?? row.Command!.Name;
        var tile = new Grid
        {
            Width = CoverPixels,
            Height = CoverPixels,
            CornerRadius = new CornerRadius(row.Item?.Kind == QuickKind.Artist ? CoverPixels / 2 : 4),
            Background = row.Command is null ? Artwork.PlaceholderBrush(title) : theme.GetBrush("ResonateSurfaceHoverBrush"),
        };
        if (Glyph(row) is { } glyph)
        {
            tile.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = theme.GetBrush("ResonateTextPrimaryBrush") });
        }

        if (Picture(row.Item) is { } picture)
        {
            tile.Children.Add(new Image { Source = picture, Stretch = Stretch.UniformToFill });
        }

        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)resources["ResonateBodyTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = row.Item?.Subtitle ?? "Command",
            Style = (Style)resources["ResonateCaptionTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // "Added to queue": slides in beside the row, which stays still.
        row.Chip = new Border
        {
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Center,
            Background = theme.GetBrush("ResonateAccentSoftBrush"),
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(160) },
            Child = new TextBlock { Style = (Style)resources["ResonateCaptionTextStyle"], Foreground = theme.GetBrush("ResonateTextPrimaryBrush") },
        };
        Grid.SetColumn(row.Chip, 2);
        grid.Children.Add(row.Chip);

        AutomationProperties.SetName(grid, $"{title}, {row.Item?.Subtitle ?? "command"}");
        grid.PointerEntered += (_, _) => Select(index);
        grid.Tapped += (_, _) => Choose(index, SummonAction.Play);
        return grid;
    }

    /// <summary>An icon for rows without a picture of their own.</summary>
    private static string? Glyph(Row row) => row.Command is { } command
        ? command.Glyph
        : row.Item?.Kind switch
        {
            QuickKind.LikedSongs => "",
            QuickKind.LocalSong => "",
            QuickKind.Artist when row.Item.ImageUrl is null => "",
            _ => null,
        };

    private ImageSource? Picture(QuickItem? item) => item switch
    {
        { Kind: QuickKind.LocalSong, Track: { } track } => LocalArtwork.For(track, CoverPixels),
        { ImageUrl: { } url } => _services.Covers.Get(url, CoverPixels),
        _ => null,
    };

    private void Select(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _rows.Count - 1);
        if (_selected < _rows.Count && _rows[_selected].Element is { } old)
        {
            old.Background = _services.Theme.GetBrush("ResonateTransparentBrush");
        }

        _selected = index;
        if (_rows[index].Element is { } now)
        {
            now.Background = _services.Theme.GetBrush("ResonateAccentSoftBrush");
            now.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Dismiss(returnFocus: true);
                break;
            case VirtualKey.Down:
                e.Handled = true;
                Select(_selected + 1);
                break;
            case VirtualKey.Up:
                e.Handled = true;
                Select(_selected - 1);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                Choose(
                    _selected,
                    IsDown(VirtualKey.Control) ? SummonAction.Open : IsDown(VirtualKey.Shift) ? SummonAction.Queue : SummonAction.Play);
                break;
        }
    }

    private void Choose(int index, SummonAction action)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        var row = _rows[index];
        if (row.Command is { } command)
        {
            Dismiss(returnFocus: !command.ShowsWindow);
            command.Run();
            return;
        }

        var item = row.Item!;
        if (action == SummonAction.Queue)
        {
            // The bar stays, so more can be added.
            ShowChip(row, _main.QueueFromSummon(item));
            return;
        }

        Dismiss(returnFocus: action == SummonAction.Play);
        _main.RunFromSummon(item, action, _index.LocalSongs);
    }

    private void ShowChip(Row row, string text)
    {
        if (row.Chip is not { Child: TextBlock label } chip)
        {
            return;
        }

        label.Text = text;
        chip.Opacity = 1;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.6);
        timer.IsRepeating = false;
        global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Dispatching.DispatcherQueueTimer, object>? hide = null;
        hide = (_, _) =>
        {
            // Let go of the handler, or the timer would keep the chip for good.
            timer.Tick -= hide;
            chip.Opacity = 0;
        };
        timer.Tick += hide;
        timer.Start();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private sealed class Row(QuickItem? item, SummonCommand? command)
    {
        public QuickItem? Item { get; } = item;

        public SummonCommand? Command { get; } = command;

        public Grid? Element { get; set; }

        public Border? Chip { get; set; }
    }

    // ---- Windows ----

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

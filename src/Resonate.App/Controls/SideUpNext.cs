using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// What plays next, at the foot of the column beside the page (Placement
/// Left or Right): the songs that fit, from Resonate's own list, the local
/// player or Spotify's queue (read once per song, only while the window
/// shows). Hidden when nothing is known to come next.
/// </summary>
internal sealed partial class SideUpNext : Grid
{
    private const int MaxRows = 12;
    private const int CoverSize = 40;

    private readonly AppServices _services;
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly List<(Grid Cover, Image Image, TextBlock Title, TextBlock Artists, Grid Row)> _rows = [];
    private string? _key;
    private bool _stale;
    private CancellationTokenSource? _loading;
    private int _queued;

    public SideUpNext(AppServices services)
    {
        _services = services;
        var resources = Application.Current.Resources;
        Padding = new Thickness(20, 6, 12, 16);
        RowSpacing = 10;
        Visibility = Visibility.Collapsed;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Children.Add(new TextBlock { Style = (Style)resources["ResonateEyebrowTextStyle"], Text = "UP NEXT" });
        var scroller = new ScrollViewer
        {
            Content = _list,
            Padding = new Thickness(0, 0, 8, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        SetRow(scroller, 1);
        Children.Add(scroller);

        for (var i = 0; i < MaxRows; i++)
        {
            var row = Row(resources);
            _rows.Add(row);
            _list.Children.Add(row.Row);
        }

        Loaded += (_, _) =>
        {
            _services.Player.StateChanged += OnStateChanged;
            if (App.MainWindow is { } window)
            {
                window.ShownChanged += OnShownChanged;
            }

            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _services.Player.StateChanged -= OnStateChanged;
            if (App.MainWindow is { } window)
            {
                window.ShownChanged -= OnShownChanged;
            }

            _loading?.Cancel();
        };
    }

    private void OnShownChanged(object? sender, EventArgs e)
    {
        if (_stale)
        {
            Refresh();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Arrives on any thread; the newest state is read once.
        if (Interlocked.Exchange(ref _queued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _queued, 0);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        var player = _services.Player;
        var state = player.State;
        if (!state.HasTrack)
        {
            _key = null;
            Show([]);
            return;
        }

        // Resonate's own list and the local player are known at once.
        if (player.UpNext is { } list)
        {
            Show(list.Upcoming);
            return;
        }

        if (player.ActiveSource == PlaybackSource.LocalFiles)
        {
            Show(player.Local.Upcoming);
            return;
        }

        var key = string.Join('|', state.TrackUri ?? state.Title, state.ContextUri, state.Shuffle, state.Repeat);
        if (key == _key && !_stale)
        {
            return;
        }

        if (App.MainWindow is { IsShown: false })
        {
            // Read when the window shows again; nothing is asked of Spotify meanwhile.
            _stale = true;
            return;
        }

        _key = key;
        _stale = false;
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        _ = LoadAsync(key, _loading.Token);
    }

    private async Task LoadAsync(string key, CancellationToken token)
    {
        try
        {
            var queue = await Task.Run(() => _services.Api.GetQueueAsync(token), token);
            if (key == _key && !token.IsCancellationRequested)
            {
                Show(QueuePreview.Upcoming(queue));
            }
        }
        catch (Exception)
        {
            // Offline, refused or cancelled: nothing is shown as next.
            if (key == _key && !token.IsCancellationRequested)
            {
                Show([]);
            }
        }
    }

    private void Show(IReadOnlyList<TrackInfo> upcoming)
    {
        Visibility = upcoming.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var size = AppScale.Cover(CoverSize, _services.Settings.CoverSize);
        var corner = new CornerRadius(Math.Min(_services.Theme.Palette.CornerSmall, size / 4.0));
        for (var i = 0; i < _rows.Count; i++)
        {
            var (cover, image, title, artists, row) = _rows[i];
            if (i >= upcoming.Count)
            {
                row.Visibility = Visibility.Collapsed;
                image.Source = null;
                continue;
            }

            var track = upcoming[i];
            row.Visibility = Visibility.Visible;
            title.Text = track.Title;
            artists.Text = track.Artists;
            cover.Width = cover.Height = size;
            cover.CornerRadius = corner;

            // Nothing while the cover loads; the album's colour tile when there is none.
            var tile = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
            Task<bool> missing;
            var source = track.FilePath is not null
                ? LocalArtwork.For(track, size, out missing)
                : _services.Covers.Get(CoverImages.UrlFor(track, size), size, out missing);
            image.Source = source;
            cover.Background = source is null ? tile : null;
            if (source is not null)
            {
                _ = ShowTileIfMissingAsync(new WeakReference<Grid>(cover), new WeakReference<Image>(image), source, missing, tile);
            }
        }
    }

    // Holds the row only weakly: a cover that never answers must not keep the column in memory.
    private static async Task ShowTileIfMissingAsync(WeakReference<Grid> cover, WeakReference<Image> image, ImageSource source, Task<bool> missing, Brush tile)
    {
        if (await missing && image.TryGetTarget(out var shown) && ReferenceEquals(shown.Source, source) && cover.TryGetTarget(out var box))
        {
            box.Background = tile;
        }
    }

    private static (Grid Cover, Image Image, TextBlock Title, TextBlock Artists, Grid Row) Row(ResourceDictionary resources)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var cover = new Grid { Width = CoverSize, Height = CoverSize, Children = { image } };
        var title = new TextBlock { Style = (Style)resources["ResonateBodyTextStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var artists = new TextBlock { Style = (Style)resources["ResonateCaptionTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, artists } };
        SetColumn(words, 1);
        var row = new Grid
        {
            ColumnSpacing = 12,
            Visibility = Visibility.Collapsed,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { cover, words },
        };
        return (cover, image, title, artists, row);
    }
}

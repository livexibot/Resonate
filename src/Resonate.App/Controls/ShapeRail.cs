using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.ViewModels;

namespace Resonate.App.Controls;

/// <summary>
/// The sidebar folded into a rail, for the Compact window shape (Window
/// shapes plugin): the links as icons, then every playlist as its cover
/// (click to open, double-click to play), and Settings at the foot. Built in
/// code; it follows the playlists only while it is shown.
/// </summary>
internal sealed partial class ShapeRail : Grid
{
    private const double CoverSize = 44;

    private readonly IReadOnlyList<NavItem> _links;
    private readonly ObservableCollection<PlaylistNavItem> _playlists;
    private readonly Action<string> _open;
    private readonly Action<PlaylistNavItem> _play;
    private readonly StackPanel _linkPanel = new() { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _playlistPanel = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private string? _selected;
    private bool _rebuildQueued;

    public ShapeRail(
        IReadOnlyList<NavItem> links,
        ObservableCollection<PlaylistNavItem> playlists,
        Action<string> open,
        Action<PlaylistNavItem> play,
        Action openSettings)
    {
        _links = links;
        _playlists = playlists;
        _open = open;
        _play = play;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Children.Add(_linkPanel);
        var scroller = new ScrollViewer
        {
            Content = _playlistPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        SetRow(scroller, 1);
        Children.Add(scroller);

        var settings = IconButton("", "Settings");
        settings.Click += (_, _) => openSettings();
        SetRow(settings, 2);
        Children.Add(settings);

        Loaded += (_, _) =>
        {
            _playlists.CollectionChanged += OnPlaylistsChanged;
            Build();
        };
        Unloaded += (_, _) => _playlists.CollectionChanged -= OnPlaylistsChanged;
    }

    /// <summary>Marks the page on show (a link's or a playlist's key).</summary>
    public void Select(string? key)
    {
        if (_selected is { } old && _buttons.TryGetValue(old, out var before))
        {
            before.ClearValue(Control.BackgroundProperty);
        }

        _selected = key;
        if (key is not null && _buttons.TryGetValue(key, out var now))
        {
            now.Background = App.Services.Theme.GetBrush("ResonateAccentSoftBrush");
        }
    }

    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The sidebar refills its list one playlist at a time: build once, after.
        if (!_rebuildQueued)
        {
            _rebuildQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _rebuildQueued = false;
                Build();
            });
        }
    }

    private void Build()
    {
        _buttons.Clear();
        _linkPanel.Children.Clear();
        foreach (var link in _links)
        {
            var button = IconButton(link.Glyph, link.Label);
            var key = link.Key;
            button.Click += (_, _) => _open(key);
            _buttons[key] = button;
            _linkPanel.Children.Add(button);
        }

        _playlistPanel.Children.Clear();
        foreach (var playlist in _playlists)
        {
            var item = playlist;
            var cover = new Grid
            {
                Width = CoverSize,
                Height = CoverSize,
                Background = item.PlaceholderBrush,
                CornerRadius = new CornerRadius(App.Services.Theme.Palette.CornerSmall),
            };
            cover.Children.Add(new Image { Source = item.Image, Stretch = Stretch.UniformToFill });
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
                Width = CoverSize + 8,
                Height = CoverSize + 8,
                Content = cover,
            };
            AutomationProperties.SetName(button, item.Name);
            ToolTipService.SetToolTip(button, item.Name);
            button.Click += (_, _) => _open(item.Id);
            button.DoubleTapped += (_, e) =>
            {
                e.Handled = true;
                _play(item);
            };
            _buttons[item.Id] = button;
            _playlistPanel.Children.Add(button);
        }

        Select(_selected);
    }

    private static Button IconButton(string glyph, string name)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
            Width = CoverSize + 8,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = glyph,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }
}

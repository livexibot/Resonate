using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Services;

namespace Resonate.App.Pages;

/// <summary>
/// Resonate's own DJ (<see cref="DjService"/>): Start DJ plays a run of
/// themed sets from the user's music; while it plays, the button switches
/// it up, the set playing shows under the title, and Up next lists every
/// song still to come, by set (the owner's request, 10 October 2026); a
/// click plays the run from that song.
/// </summary>
public sealed partial class DjPage : Page
{
    private const int CoverSize = 40;

    private readonly AppServices _services = App.Services;
    private readonly DjService _dj;
    private readonly List<(int Index, FrameworkElement Row)> _songRows = [];
    private readonly List<(int Last, FrameworkElement Header)> _setHeaders = [];
    private IReadOnlyList<Resonate.Spotify.Library.TrackInfo>? _shownRun;
    private bool _loading;

    public DjPage()
    {
        InitializeComponent();
        _dj = _services.Dj;

        // The look's accent, like Liked Songs and Local Files (the owner's choice, 10 October 2026).
        CoverFrame.Background = Artwork.AccentCoverBrush;
        CoverGlyph.Foreground = _services.Theme.GetBrush("ResonateOnAccentBrush");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _dj.Changed -= OnDjChanged;
        _dj.Changed += OnDjChanged;
        _loading = true;
        VoiceSwitch.IsOn = _services.Settings.DjVoice;
        _loading = false;
        ShowState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _dj.Changed -= OnDjChanged;

    private void OnDjChanged(object? sender, EventArgs e) => ShowState();

    private void ShowState()
    {
        var on = _dj.IsOn;
        StartText.Text = on ? "Switch it up" : "Start DJ";
        StartGlyph.Glyph = on ? "" : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StartButton, StartText.Text);
        StartButton.IsEnabled = !_dj.IsStarting;
        WaitingRing.IsActive = _dj.IsStarting;

        var sets = _dj.Sets;
        var current = _dj.CurrentSet;
        NowText.Text = on && current < sets.Count ? sets[current].Title : string.Empty;
        NowText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        ShowUpNext(on);
    }

    /// <summary>
    /// Every song after the one playing, under its set's title. The rows are
    /// built once per run; as songs play, the ones heard fold away.
    /// </summary>
    private void ShowUpNext(bool on)
    {
        var run = on ? _dj.Run : [];
        if (!ReferenceEquals(run, _shownRun))
        {
            BuildUpNext(run);
        }

        var current = _dj.CurrentIndex;
        var any = false;
        foreach (var (index, row) in _songRows)
        {
            var show = index > current;
            row.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            any |= show;
        }

        foreach (var (last, header) in _setHeaders)
        {
            header.Visibility = last > current ? Visibility.Visible : Visibility.Collapsed;
        }

        SetsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildUpNext(IReadOnlyList<Resonate.Spotify.Library.TrackInfo> run)
    {
        _shownRun = run;
        SetsList.Children.Clear();
        _songRows.Clear();
        _setHeaders.Clear();
        var resources = Application.Current.Resources;
        var size = Resonate.Themes.AppScale.Cover(CoverSize, _services.Settings.CoverSize);
        var index = 0;
        foreach (var set in _dj.Sets)
        {
            var header = new TextBlock
            {
                Text = set.Title,
                Style = (Style)resources["ResonateEyebrowTextStyle"],
                Margin = new Thickness(0, _setHeaders.Count == 0 ? 0 : 14, 0, 2),
            };
            SetsList.Children.Add(header);
            _setHeaders.Add((index + set.Tracks.Count - 1, header));
            foreach (var track in set.Tracks)
            {
                var row = SongRow(track, index, size, resources);
                SetsList.Children.Add(row);
                _songRows.Add((index, row));
                index++;
            }
        }
    }

    /// <summary>A song: its cover, title and artists; a click plays the run from it.</summary>
    private Grid SongRow(Resonate.Spotify.Library.TrackInfo track, int index, int size, ResourceDictionary resources)
    {
        var tile = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
        var image = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Source = _services.Covers.Get(CoverImages.UrlFor(track, size), size, out var missing) };
        var cover = new Grid
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(_services.Theme.Palette.CornerSmall),
            Background = image.Source is null ? tile : null,
            Children = { image },
        };
        if (image.Source is { } source)
        {
            _ = ShowTileIfMissingAsync(new WeakReference<Grid>(cover), new WeakReference<Image>(image), source, missing, tile);
        }

        var words = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = track.Title, Style = (Style)resources["ResonateBodyTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = track.Artists, Style = (Style)resources["ResonateSecondaryTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        Grid.SetColumn(words, 1);
        var length = new TextBlock
        {
            Text = Format.Duration(track.Duration),
            Style = (Style)resources["ResonateSecondaryTextStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(length, 2);
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(8, 4, 12, 4),
            CornerRadius = new CornerRadius(_services.Theme.Palette.CornerMedium),
            Background = _services.Theme.GetBrush("ResonateTransparentBrush"),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { cover, words, length },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{track.Title}, {track.Artists}");
        row.PointerEntered += (_, _) => row.Background = _services.Theme.GetBrush("ResonateSurfaceHoverBrush");
        row.PointerExited += (_, _) => row.Background = _services.Theme.GetBrush("ResonateTransparentBrush");
        row.Tapped += (_, _) => _ = _dj.PlayFromAsync(index);
        return row;
    }

    // Holds the row only weakly: a cover that never answers must not keep the page in memory.
    private static async Task ShowTileIfMissingAsync(WeakReference<Grid> cover, WeakReference<Image> image, Microsoft.UI.Xaml.Media.ImageSource source, Task<bool> missing, Microsoft.UI.Xaml.Media.Brush tile)
    {
        if (await missing && image.TryGetTarget(out var shown) && ReferenceEquals(shown.Source, source) && cover.TryGetTarget(out var box))
        {
            box.Background = tile;
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _ = StartAsync();

    private async Task StartAsync()
    {
        StatusText.Visibility = Visibility.Collapsed;
        var started = _dj.IsOn ? await _dj.SwitchUpAsync() : await _dj.StartAsync();
        if (!started)
        {
            // Nothing to make sets from (no Liked Songs yet), or Spotify could not be reached.
            StatusText.Text = "The DJ needs some Liked Songs to play.";
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void OnVoiceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.Settings.DjVoice = VoiceSwitch.IsOn;
        _services.SaveSettings();
        if (!VoiceSwitch.IsOn)
        {
            _dj.StopVoice();
        }
    }
}

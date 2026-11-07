using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.History;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

/// <summary>
/// The listening calendar (the owner's pick from the feature concepts,
/// 7 November 2026): a month of days, each showing the cover of the song
/// played most that day and brighter the longer it was listened to. A day
/// opens its sessions on a strip of the 24 hours; each plays again, as does
/// the whole day. Everything comes from Resonate's own history
/// (<see cref="ListeningCalendar"/>); the only request is the history's
/// own catch-up, at most once in five minutes as on Home.
/// </summary>
public sealed partial class CalendarPage : Page
{
    private const int CoverDecode = 256;
    private const int ChipSize = 24;
    private const double Gap = 8;
    private const int MinutesPerDay = 24 * 60;

    // A little listening shows faint, the month's busiest days at full strength.
    private static readonly double[] LevelOpacity = [0.42, 0.62, 0.82, 1.0];

    // Resonate speaks English; the week starts where the user's region starts it.
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    private readonly AppServices _services = App.Services;
    private readonly ListeningCalendar _calendar;
    private readonly DayOfWeek _firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
    private DateOnly _month;
    private DateOnly? _selected;
    private double _tileHeight = 84;

    public CalendarPage()
    {
        InitializeComponent();
        _calendar = _services.Calendar;
        _month = FirstOfMonth(_calendar.Today);
        for (var i = 0; i < 7; i++)
        {
            WeekdayRow.ColumnDefinitions.Add(new ColumnDefinition());
            MonthGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new TextBlock
            {
                Text = English.DateTimeFormat.AbbreviatedDayNames[((int)_firstDayOfWeek + i) % 7],
                Style = (Style)Application.Current.Resources["ResonateCaptionTextStyle"],
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(name, i);
            WeekdayRow.Children.Add(name);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _calendar.Changed -= OnCalendarChanged;
        _calendar.Changed += OnCalendarChanged;
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _calendar.Changed -= OnCalendarChanged;

    private async Task LoadAsync()
    {
        if (!_calendar.IsLoaded)
        {
            await Task.Run(_calendar.Load);
        }

        Show();

        // The newest plays, at most once in five minutes (the history's own limit); new days show as they come.
        try
        {
            await _services.Home.History.SyncAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Spotify could not be reached: the days already known still show.
        }
    }

    private void OnCalendarChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Show);

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        _selected = null;
        Show();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        _selected = null;
        Show();
    }

    private void Show()
    {
        var days = _calendar.Days;
        var today = _calendar.Today;
        MonthText.Text = _month.ToString("MMMM yyyy", English);
        NextButton.IsEnabled = _month < FirstOfMonth(today);
        PreviousButton.IsEnabled = days.Count == 0 || days.Keys.Min() < _month;

        var dates = CalendarMonth.Dates(_month.Year, _month.Month, _firstDayOfWeek);
        var inMonth = dates.Where(d => d.Month == _month.Month).ToList();
        var busiest = inMonth.Select(d => days.TryGetValue(d, out var day) ? day.Minutes : 0).DefaultIfEmpty(0).Max();
        if (_selected is not { } chosen || chosen.Month != _month.Month || !days.ContainsKey(chosen))
        {
            // Today when it has music, else the month's latest day that does.
            _selected = inMonth.Where(d => d <= today && days.ContainsKey(d)).Cast<DateOnly?>().LastOrDefault();
        }

        MonthGrid.Children.Clear();
        MonthGrid.RowDefinitions.Clear();
        for (var row = 0; row < dates.Count / 7; row++)
        {
            MonthGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_tileHeight) });
        }

        for (var i = 0; i < dates.Count; i++)
        {
            var date = dates[i];
            var day = date.Month == _month.Month && days.TryGetValue(date, out var known) ? known : null;
            var tile = Tile(date, day, date.Month == _month.Month, CalendarMonth.Level(day?.Minutes ?? 0, busiest), date == today);
            Grid.SetRow(tile, i / 7);
            Grid.SetColumn(tile, i % 7);
            MonthGrid.Children.Add(tile);
        }

        ShowDay(days);
    }

    /// <summary>A day: the cover of its most played song, its number, and a ring when it is chosen or today.</summary>
    private Button Tile(DateOnly date, CalendarDay? day, bool inMonth, int level, bool isToday)
    {
        var corner = new CornerRadius(_services.Theme.Palette.CornerMedium);
        var content = new Grid { CornerRadius = corner };
        var number = new TextBlock
        {
            Text = date.Day.ToString(English),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var label = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(6, 1, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(6),
            Child = number,
        };

        if (day?.Songs.FirstOrDefault() is { } top)
        {
            var track = top.ToTrack();
            var placeholder = Artwork.PlaceholderBrush(top.Album.Length > 0 ? top.Album : top.Title);
            var image = new Image { Stretch = Stretch.UniformToFill, Source = _services.Covers.Get(CoverImages.UrlFor(track, CoverDecode), CoverDecode, out var missing) };
            var cover = new Grid
            {
                Opacity = LevelOpacity[Math.Clamp(level, 0, LevelOpacity.Length - 1)],
                Background = image.Source is null ? placeholder : null,
                Children = { image },
            };
            if (image.Source is { } source)
            {
                _ = ShowTileIfMissingAsync(new WeakReference<Grid>(cover), new WeakReference<Image>(image), source, missing, placeholder);
            }

            content.Children.Add(cover);

            // The number stays readable over any cover.
            label.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x99, 0x0F, 0x10, 0x14));
            number.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        else
        {
            content.BorderBrush = _services.Theme.GetBrush("ResonateBorderBrush");
            content.BorderThickness = new Thickness(1);
            content.Opacity = inMonth ? 1 : 0.45;
            number.Foreground = _services.Theme.GetBrush("ResonateTextTertiaryBrush");
        }

        content.Children.Add(label);
        var selected = day is not null && _selected == date;
        var ringBrush = selected ? _services.Theme.GetBrush("ResonateTextPrimaryBrush")
            : isToday && inMonth ? _services.Theme.GetBrush("ResonateAccentBrush")
            : null;
        var ring = new Border
        {
            CornerRadius = corner,
            BorderThickness = new Thickness(2),
            BorderBrush = ringBrush,
            IsHitTestVisible = false,
        };
        content.Children.Add(ring);

        var button = new Button
        {
            Style = (Style)Resources["CalendarDayStyle"],
            Content = content,
            IsEnabled = day is not null,
        };
        var name = date.ToString("dddd d MMMM", English);
        if (day is not null)
        {
            name += $", {Listened(day.Minutes)}";
            ToolTipService.SetToolTip(button, $"{name}, {day.Songs[0].Title}");
            button.Click += (_, _) =>
            {
                _selected = date;
                Show();
            };
            button.PointerEntered += (_, _) => ring.BorderBrush ??= _services.Theme.GetBrush("ResonateTextSecondaryBrush");
            button.PointerExited += (_, _) => ring.BorderBrush = ringBrush;
        }

        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>The chosen day: how long it was, and its sessions where they fell in the 24 hours.</summary>
    private void ShowDay(IReadOnlyDictionary<DateOnly, CalendarDay> days)
    {
        if (_selected is not { } date || !days.TryGetValue(date, out var day))
        {
            DayPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DayPanel.Visibility = Visibility.Visible;
        DayTitle.Text = date.ToString("dddd d", English);
        DaySummary.Text = $"{Listened(day.Minutes)} · {Format.SongCount(day.Plays)}";
        PlayDayButton.IsEnabled = day.InOrder().Any(s => s.CanPlay);
        PlayDayButton.Tag = date;

        // The first child is the line across the day.
        while (Strip.Children.Count > 1)
        {
            Strip.Children.RemoveAt(Strip.Children.Count - 1);
        }

        foreach (var session in day.Sessions)
        {
            Strip.Children.Add(Session(day, session));
        }
    }

    /// <summary>A session where it fell in the day: the covers it played and a button that plays it again.</summary>
    private Grid Session(CalendarDay day, CalendarSession session)
    {
        var start = Math.Clamp(session.Start, 0, MinutesPerDay - 1);
        var end = Math.Clamp(session.End, start + 1, MinutesPerDay);
        var songs = day.SongsOf(session).ToList();

        var covers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var song in songs.DistinctBy(s => s.Uri).Take(40))
        {
            covers.Children.Add(Chip(song));
        }

        var play = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
            Content = "",
            FontSize = 11,
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = _services.Theme.GetBrush("ResonateAccentBrush"),
            Foreground = _services.Theme.GetBrush("ResonateOnAccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = songs.Any(s => s.CanPlay),
        };
        var when = $"{Clock(start)}–{Clock(end)}";
        AutomationProperties.SetName(play, $"Play {when}");
        ToolTipService.SetToolTip(play, $"{when} · {Format.SongCount(songs.Count)}");
        play.Click += (_, _) => Play(songs, DayTitle.Text);
        Grid.SetColumn(play, 1);

        var capsule = new Border
        {
            Height = 40,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(20),
            Background = _services.Theme.GetBrush("ResonateAccentSoftBrush"),
            BorderBrush = _services.Theme.GetBrush("ResonateAccentBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid
            {
                ColumnSpacing = 4,
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
                Children = { covers, play },
            },
        };
        ToolTipService.SetToolTip(capsule, when);
        Grid.SetColumn(capsule, 1);

        // Placed by the minute: the time before, the session (wide enough for its button), the time after.
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(start, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(end - start, GridUnitType.Star), MinWidth = 44 },
                new ColumnDefinition { Width = new GridLength(MinutesPerDay - end, GridUnitType.Star) },
            },
            Children = { capsule },
        };
    }

    private Grid Chip(CalendarSong song)
    {
        var placeholder = Artwork.PlaceholderBrush(song.Album.Length > 0 ? song.Album : song.Title);
        var image = new Image { Stretch = Stretch.UniformToFill, Source = _services.Covers.Get(CoverImages.UrlFor(song.ToTrack(), ChipSize), ChipSize, out var missing) };
        var chip = new Grid
        {
            Width = ChipSize,
            Height = ChipSize,
            CornerRadius = new CornerRadius(_services.Theme.Palette.CornerSmall),
            Background = image.Source is null ? placeholder : null,
            Children = { image },
        };
        if (image.Source is { } source)
        {
            _ = ShowTileIfMissingAsync(new WeakReference<Grid>(chip), new WeakReference<Image>(image), source, missing, placeholder);
        }

        ToolTipService.SetToolTip(chip, $"{song.Title} · {string.Join(", ", song.Artists.Select(a => a.Name))}");
        return chip;
    }

    private void OnPlayDayClick(object sender, RoutedEventArgs e)
    {
        if (_selected is { } date && _calendar.Days.TryGetValue(date, out var day))
        {
            Play(day.InOrder(), DayTitle.Text);
        }
    }

    /// <summary>Plays the songs in the order they played, each once (local files left out: Spotify can not start them).</summary>
    private void Play(IEnumerable<CalendarSong> songs, string name)
    {
        var tracks = songs.Where(s => s.CanPlay).DistinctBy(s => s.Uri).Select(s => s.ToTrack()).ToList();
        if (tracks.Count > 0)
        {
            _ = _services.Player.PlayAsync(new PlayRequest(tracks, 0, null, name));
        }
    }

    // The days keep a shape near a cover's: wide windows make them taller, up to a point.
    private void OnMonthGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var column = (e.NewSize.Width - (6 * Gap)) / 7;
        var height = Math.Clamp(Math.Round(column * 0.62), 64, 150);
        if (Math.Abs(height - _tileHeight) < 1)
        {
            return;
        }

        _tileHeight = height;
        foreach (var row in MonthGrid.RowDefinitions)
        {
            row.Height = new GridLength(height);
        }
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private static string Listened(int minutes) => minutes switch
    {
        < 60 => $"{minutes} min",
        _ when minutes % 60 == 0 => $"{minutes / 60} h",
        _ => $"{minutes / 60} h {minutes % 60} min",
    };

    // Holds the tile only weakly: a cover that never answers must not keep the page in memory.
    private static async Task ShowTileIfMissingAsync(WeakReference<Grid> cover, WeakReference<Image> image, ImageSource source, Task<bool> missing, Brush tile)
    {
        if (await missing && image.TryGetTarget(out var shown) && ReferenceEquals(shown.Source, source) && cover.TryGetTarget(out var box))
        {
            box.Background = tile;
        }
    }
}

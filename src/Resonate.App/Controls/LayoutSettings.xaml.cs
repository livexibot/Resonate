using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.App.ViewModels;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The Layout tab of Settings: sizes, the sidebar, how pages open and
/// Home's background, Home's visualizer, and song lists. All of it belongs
/// to the user and is kept whatever look is in use, except the Home
/// visualizer's style, which is the look's (Off is the user's).
/// </summary>
public sealed partial class LayoutSettings : UserControl
{
    private readonly AppServices _services = App.Services;
    private readonly ThemeService _theme = App.Services.Theme;

    // True while controls are being set (and while the panel is built), so that is not taken as a change.
    private bool _loading = true;

    public LayoutSettings()
    {
        InitializeComponent();

        // Home's background, under the page animation.
        PagesRows.Children.Add(StageSettings.HomeStage(_services));

        // Home lists every visualizer style; Off hides it.
        StageVisualizerChoice.Items.Add(new ComboBoxItem { Content = "Off", Tag = nameof(VisualizerStyle.Off) });
        foreach (var style in VisualizerShapes.Offered)
        {
            StageVisualizerChoice.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style.ToString() });
        }

        foreach (var row in StageSettings.HomeVisualizer(_services))
        {
            HomeVisualizerRows.Children.Add(row);
        }

        foreach (var size in AppScale.AppSizes)
        {
            AppSizeChoice.Items.Add(AppScale.Label(size));
        }

        foreach (var size in AppScale.TextSizes)
        {
            TextSizeChoice.Items.Add(AppScale.Label(size));
        }

        foreach (var size in AppScale.CoverSizes)
        {
            CoverSizeChoice.Items.Add(AppScale.Label(size));
        }

        // Only while shown, so the theme never keeps a closed Settings page alive.
        Loaded += (_, _) =>
        {
            // Loaded can come twice in a row; each handler is held once.
            _theme.Changed -= OnThemeChanged;
            _theme.SizeChanged -= OnThemeChanged;
            _theme.Changed += OnThemeChanged;
            _theme.SizeChanged += OnThemeChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _theme.Changed -= OnThemeChanged;
            _theme.SizeChanged -= OnThemeChanged;
        };
        _loading = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _loading = true;
        try
        {
            Select(PageAnimationChoice, _services.Settings.PageAnimation);
            Select(StageVisualizerChoice, _services.Settings.HomeStageVisualizer ? _theme.Current.StageVisualizer.ToString() : nameof(VisualizerStyle.Off));
            PlayerSettings.ShowRowsAfterFirst(HomeVisualizerRows, _services.Settings.HomeStageVisualizer);
            SidebarFullHeightSwitch.IsOn = _theme.SidebarFullHeight;

            var hidden = _services.Settings.HiddenSidebarLinks;
            SearchLinkSwitch.IsOn = !hidden.Contains(MainWindow.SearchKey);
            LikedLinkSwitch.IsOn = !hidden.Contains(MainWindow.LikedSongsKey);
            LocalFilesLinkSwitch.IsOn = _services.LocalFiles.ShowInSidebar;
            DjLinkSwitch.IsOn = !hidden.Contains(MainWindow.DjKey);
            PlaylistCoversSwitch.IsOn = _services.Settings.ShowPlaylistCovers;
            SongCoversSwitch.IsOn = _services.Settings.ShowSongCovers;
            ColumnNamesSwitch.IsOn = _services.Settings.ShowColumnNames;
            NumberColumnSwitch.IsOn = _services.Settings.ShowNumberColumn;
            LikeColumnSwitch.IsOn = _services.Settings.ShowLikeColumn;
            DurationColumnSwitch.IsOn = _services.Settings.ShowDurationColumn;
            AlbumColumnSwitch.IsOn = _services.Settings.ShowAlbumColumn;
            YearColumnSwitch.IsOn = _services.Settings.ShowYearColumn;
            AddedColumnSwitch.IsOn = _services.Settings.ShowAddedColumn;
            SongStatsSwitch.IsOn = _services.Settings.SongStats;
            BpmColumnSwitch.IsOn = _services.Settings.ShowBpmColumn;
            KeyColumnSwitch.IsOn = _services.Settings.ShowKeyColumn;
            LoudnessColumnSwitch.IsOn = _services.Settings.ShowLoudnessColumn;
            EnergyColumnSwitch.IsOn = _services.Settings.ShowEnergyColumn;
            ShowStatColumns();

            // The keys as the user has them (Settings, General, Keyboard shortcuts).
            var keys = _services.Settings.KeyShortcuts;
            AppSizeRow.Description = string.Join(", ", new[] { AppCommand.AppSizeUp, AppCommand.AppSizeDown, AppCommand.AppSizeReset }
                .Select(command => AppKeys.For(command, keys))
                .Where(combos => combos.Count > 0)
                .Select(AppKeys.Display));

            AppSizeChoice.SelectedIndex = IndexOf(AppScale.AppSizes, _theme.AppSize);
            TextSizeChoice.SelectedIndex = IndexOf(AppScale.TextSizes, _theme.TextSize);
            CoverSizeChoice.SelectedIndex = IndexOf(AppScale.CoverSizes, AppScale.Nearest(_services.Settings.CoverSize, AppScale.CoverSizes));
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Select(ComboBox choice, string tag) =>
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == tag);

    /// <summary>Off hides Home's visualizer (the user's choice); a style shows it, drawn as the look says.</summary>
    private void OnStageVisualizerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StageVisualizerChoice.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var style = Enum.Parse<VisualizerStyle>(tag);
        var on = style != VisualizerStyle.Off;
        PlayerSettings.ShowRowsAfterFirst(HomeVisualizerRows, on);
        if (on != _services.Settings.HomeStageVisualizer)
        {
            _services.Settings.HomeStageVisualizer = on;
            _services.SaveSettings();
            NowPlayingStage.NotifyOptionsChanged();
        }

        if (on && style != _theme.Current.StageVisualizer)
        {
            _theme.Edit(look => look with { StageVisualizer = style }, smooth: true);
        }
    }

    // The page animation belongs to the user, not to a look.
    private void OnPageAnimationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PageAnimationChoice.SelectedItem is ComboBoxItem { Tag: string page })
        {
            _services.Settings.PageAnimation = page;
            _services.SaveSettings();
        }
    }

    // Like the sizes, the layout switch belongs to the user, not to a look.
    private void OnSidebarFullHeightToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _theme.SidebarFullHeight = SidebarFullHeightSwitch.IsOn;
        }
    }

    private void OnLinkToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (ReferenceEquals(sender, LocalFilesLinkSwitch))
        {
            // Local Files keeps its own switch (the sidebar follows it).
            _services.LocalFiles.ShowInSidebar = LocalFilesLinkSwitch.IsOn;
            return;
        }

        var (key, on) = ReferenceEquals(sender, SearchLinkSwitch) ? (MainWindow.SearchKey, SearchLinkSwitch.IsOn)
            : ReferenceEquals(sender, LikedLinkSwitch) ? (MainWindow.LikedSongsKey, LikedLinkSwitch.IsOn)
            : (MainWindow.DjKey, DjLinkSwitch.IsOn);
        var hidden = _services.Settings.HiddenSidebarLinks;
        hidden.RemoveAll(k => k == key);
        if (!on)
        {
            hidden.Add(key);
        }

        _services.SaveSettings();
        App.MainWindow?.ShowSidebarLinks();
    }

    // Covers and columns belong to the user; song lists read them when they open.
    private void OnSongListToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = _services.Settings;
        settings.ShowSongCovers = SongCoversSwitch.IsOn;
        settings.ShowColumnNames = ColumnNamesSwitch.IsOn;
        settings.ShowNumberColumn = NumberColumnSwitch.IsOn;
        settings.ShowLikeColumn = LikeColumnSwitch.IsOn;
        settings.ShowDurationColumn = DurationColumnSwitch.IsOn;
        settings.ShowAlbumColumn = AlbumColumnSwitch.IsOn;
        settings.ShowYearColumn = YearColumnSwitch.IsOn;
        settings.ShowAddedColumn = AddedColumnSwitch.IsOn;
        settings.ShowPlaylistCovers = PlaylistCoversSwitch.IsOn;
        settings.SongStats = SongStatsSwitch.IsOn;
        settings.ShowBpmColumn = BpmColumnSwitch.IsOn;
        settings.ShowKeyColumn = KeyColumnSwitch.IsOn;
        settings.ShowLoudnessColumn = LoudnessColumnSwitch.IsOn;
        settings.ShowEnergyColumn = EnergyColumnSwitch.IsOn;
        ShowStatColumns();
        _services.SaveSettings();
        TrackColumns.NotifyOptionsChanged();
        App.MainWindow?.ShowPlaylistCovers(settings.ShowPlaylistCovers);
    }

    /// <summary>The stat columns show only while song stats are on.</summary>
    private void ShowStatColumns() => PlayerSettings.ShowRowsAfterFirst(SongStatsRows, SongStatsSwitch.IsOn);

    // The sizes belong to the user, not to a look.
    private void OnAppSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AppSizeChoice.SelectedIndex >= 0)
        {
            _theme.AppSize = AppScale.AppSizes[AppSizeChoice.SelectedIndex];
        }
    }

    // Song lists and the sidebar take a new Cover size at once.
    private void OnCoverSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CoverSizeChoice.SelectedIndex < 0)
        {
            return;
        }

        _services.Settings.CoverSize = AppScale.CoverSizes[CoverSizeChoice.SelectedIndex];
        _services.SaveSettings();
        TrackColumns.NotifyOptionsChanged();
        App.MainWindow?.ShowPlaylistCovers(_services.Settings.ShowPlaylistCovers);
    }

    private void OnTextSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TextSizeChoice.SelectedIndex >= 0)
        {
            _theme.TextSize = AppScale.TextSizes[TextSizeChoice.SelectedIndex];
        }
    }

    private static int IndexOf(IReadOnlyList<int> steps, int step)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i] == step)
            {
                return i;
            }
        }

        return -1;
    }
}

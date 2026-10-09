using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.App.ViewModels;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The Layout tab of Settings: how pages open and Home's background, the
/// sidebar's links and the title bar's buttons, song lists, and sizes. All
/// of it belongs to the user and is kept whatever look is in use.
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

        foreach (var size in AppScale.AppSizes)
        {
            AppSizeChoice.Items.Add(AppScale.Label(size));
        }

        foreach (var size in AppScale.TextSizes)
        {
            TextSizeChoice.Items.Add(AppScale.Label(size));
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
            SidebarFullHeightSwitch.IsOn = _theme.SidebarFullHeight;

            var hidden = _services.Settings.HiddenSidebarLinks;
            SearchLinkSwitch.IsOn = !hidden.Contains(MainWindow.SearchKey);
            LikedLinkSwitch.IsOn = !hidden.Contains(MainWindow.LikedSongsKey);
            LocalFilesLinkSwitch.IsOn = _services.LocalFiles.ShowInSidebar;
            DjLinkSwitch.IsOn = !hidden.Contains(MainWindow.DjKey);
            MiniPlayerButtonSwitch.IsOn = _services.Settings.ShowMiniPlayerButton;
            PlaylistCoversSwitch.IsOn = _services.Settings.ShowPlaylistCovers;
            SongCoversSwitch.IsOn = _services.Settings.ShowSongCovers;
            AlbumColumnSwitch.IsOn = _services.Settings.ShowAlbumColumn;
            YearColumnSwitch.IsOn = _services.Settings.ShowYearColumn;
            AddedColumnSwitch.IsOn = _services.Settings.ShowAddedColumn;
            SongStatsSwitch.IsOn = _services.Settings.SongStats;
            BpmColumnSwitch.IsOn = _services.Settings.ShowBpmColumn;
            KeyColumnSwitch.IsOn = _services.Settings.ShowKeyColumn;
            LoudnessColumnSwitch.IsOn = _services.Settings.ShowLoudnessColumn;
            EnergyColumnSwitch.IsOn = _services.Settings.ShowEnergyColumn;
            ShowStatColumns();

            AppSizeChoice.SelectedIndex = IndexOf(AppScale.AppSizes, _theme.AppSize);
            TextSizeChoice.SelectedIndex = IndexOf(AppScale.TextSizes, _theme.TextSize);
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Select(ComboBox choice, string tag) =>
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == tag);

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

    /// <summary>The stat columns can be picked only while song stats are on.</summary>
    private void ShowStatColumns()
    {
        var on = SongStatsSwitch.IsOn;
        BpmColumnSwitch.IsEnabled = on;
        KeyColumnSwitch.IsEnabled = on;
        LoudnessColumnSwitch.IsEnabled = on;
        EnergyColumnSwitch.IsEnabled = on;
    }

    private void OnMiniPlayerButtonToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.Settings.ShowMiniPlayerButton = MiniPlayerButtonSwitch.IsOn;
        _services.SaveSettings();
        App.MainWindow?.ShowTitleBarButtons();
    }

    // The sizes belong to the user, not to a look.
    private void OnAppSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AppSizeChoice.SelectedIndex >= 0)
        {
            _theme.AppSize = AppScale.AppSizes[AppSizeChoice.SelectedIndex];
        }
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

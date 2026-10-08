using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The Layout tab of Settings. Where the player sits is part of the look
/// (a preset is copied first, as under Customize); everything else belongs
/// to the user and is kept whatever look is in use.
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
            var position = _theme.Current.PlayerLayout.ToString();
            PlayerPositionChoice.SelectedItem = PlayerPositionChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == position);
            SidebarFullHeightSwitch.IsOn = _theme.SidebarFullHeight;

            var hidden = _services.Settings.HiddenSidebarLinks;
            SearchLinkSwitch.IsOn = !hidden.Contains(MainWindow.SearchKey);
            LikedLinkSwitch.IsOn = !hidden.Contains(MainWindow.LikedSongsKey);
            LocalFilesLinkSwitch.IsOn = _services.LocalFiles.ShowInSidebar;
            DjLinkSwitch.IsOn = !hidden.Contains(MainWindow.DjKey);
            MiniPlayerButtonSwitch.IsOn = _services.Settings.ShowMiniPlayerButton;

            AppSizeChoice.SelectedIndex = IndexOf(AppScale.AppSizes, _theme.AppSize);
            TextSizeChoice.SelectedIndex = IndexOf(AppScale.TextSizes, _theme.TextSize);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnPlayerPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PlayerPositionChoice.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<PlayerLayout>(tag, out var layout))
        {
            _theme.Edit(look => look with { PlayerLayout = layout }, smooth: true);
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

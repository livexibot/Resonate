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
            var look = _theme.Current;
            var (edge, type) = Split(look.PlayerLayout);
            Select(PlayerEdgeChoice, edge);
            Select(PlayerTypeChoice, type);
            PlayerWidthBox.Value = look.PlayerWidth ?? double.NaN;
            PlayerHeightBox.Value = look.PlayerHeight ?? double.NaN;
            PlayerXBox.Value = look.PlayerOffsetX ?? double.NaN;
            PlayerYBox.Value = look.PlayerOffsetY ?? double.NaN;
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

    private void OnPlayerLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading
            && PlayerEdgeChoice.SelectedItem is ComboBoxItem { Tag: string edge }
            && PlayerTypeChoice.SelectedItem is ComboBoxItem { Tag: string type })
        {
            var layout = Join(edge, type);
            _theme.Edit(look => look with { PlayerLayout = layout }, smooth: true);
        }
    }

    private void OnPlayerAdvancedChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        _theme.Edit(look => look with
        {
            PlayerWidth = Value(PlayerWidthBox),
            PlayerHeight = Value(PlayerHeightBox),
            PlayerOffsetX = Value(PlayerXBox),
            PlayerOffsetY = Value(PlayerYBox),
        });

        static double? Value(NumberBox box) => double.IsNaN(box.Value) ? null : Math.Round(box.Value);
    }

    private void OnPlayerAdvancedResetClick(object sender, RoutedEventArgs e) =>
        _theme.Edit(look => look with { PlayerWidth = null, PlayerHeight = null, PlayerOffsetX = null, PlayerOffsetY = null });

    /// <summary>Where the player sits (Top, Bottom, Left, Right) and what kind it is (Docked, Inset, Floating).</summary>
    private static (string Edge, string Type) Split(PlayerLayout layout) => layout switch
    {
        PlayerLayout.Top => ("Top", "Docked"),
        PlayerLayout.FloatingTop => ("Top", "Inset"),
        PlayerLayout.HoveringTop => ("Top", "Floating"),
        PlayerLayout.Floating => ("Bottom", "Inset"),
        PlayerLayout.Hovering => ("Bottom", "Floating"),
        PlayerLayout.Left => ("Left", "Docked"),
        PlayerLayout.InsetLeft => ("Left", "Inset"),
        PlayerLayout.CornerLeft => ("Left", "Floating"),
        PlayerLayout.Right => ("Right", "Docked"),
        PlayerLayout.InsetRight => ("Right", "Inset"),
        PlayerLayout.Corner => ("Right", "Floating"),
        _ => ("Bottom", "Docked"),
    };

    private static PlayerLayout Join(string edge, string type) => (edge, type) switch
    {
        ("Top", "Docked") => PlayerLayout.Top,
        ("Top", "Inset") => PlayerLayout.FloatingTop,
        ("Top", "Floating") => PlayerLayout.HoveringTop,
        ("Bottom", "Inset") => PlayerLayout.Floating,
        ("Bottom", "Floating") => PlayerLayout.Hovering,
        ("Left", "Docked") => PlayerLayout.Left,
        ("Left", "Inset") => PlayerLayout.InsetLeft,
        ("Left", "Floating") => PlayerLayout.CornerLeft,
        ("Right", "Docked") => PlayerLayout.Right,
        ("Right", "Inset") => PlayerLayout.InsetRight,
        ("Right", "Floating") => PlayerLayout.Corner,
        _ => PlayerLayout.Docked,
    };

    private static void Select(ComboBox choice, string tag) =>
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == tag);

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

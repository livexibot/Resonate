using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The Plugins tab of Settings: every plugin of Resonate, all built in
/// (see <see cref="BuiltInPlugins"/>; nothing is downloaded since
/// 10 October 2026), grouped by what it is for, each with a switch and,
/// while it is on, a gear that opens its settings. Built in code, like the
/// theme cards, so Native AOT never has to look up a XAML-created type.
/// </summary>
internal sealed partial class PluginsPanel : StackPanel
{
    private readonly BuiltInPlugins _builtIns;
    private readonly Dictionary<string, BuiltInCard> _builtInCards = new(StringComparer.Ordinal);
    private bool _updating;

    public PluginsPanel(AppServices services)
    {
        _builtIns = services.BuiltIns;
        Spacing = 20;

        // Every plugin under what it is for (see Category), by name within it.
        var items = new List<(string Category, string Name, FrameworkElement Element)>();
        foreach (var plugin in BuiltInPlugins.All)
        {
            var id = plugin.Id;
            var card = new BuiltInCard(plugin, BuiltInPluginSettings.Has(id) ? () => BuiltInPluginSettings.Create(id, services) : null);
            _builtInCards[plugin.Id] = card;
            items.Add((Category(id), plugin.Name, Build(card)));
            RefreshBuiltIn(card);
        }

        foreach (var category in Categories)
        {
            var rows = new StackPanel { Spacing = 12 };
            foreach (var (_, _, element) in items.Where(item => item.Category == category).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                rows.Children.Add(element);
            }

            if (rows.Children.Count > 0)
            {
                Children.Add(new SettingsGroup { Header = category, Content = rows });
            }
        }

        Loaded += (_, _) =>
        {
            // Loaded can come twice in a row; each handler is held once.
            _builtIns.Changed -= OnBuiltInChanged;
            _builtIns.Changed += OnBuiltInChanged;
            foreach (var card in _builtInCards.Values)
            {
                RefreshBuiltIn(card);
            }
        };
        Unloaded += (_, _) => _builtIns.Changed -= OnBuiltInChanged;
    }

    /// <summary>The plugins' groups, in the order they show. Their names differ from every other Settings heading, since a folded heading is kept by name.</summary>
    private static readonly string[] Categories =
    [
        "Lyrics",
        "Look and window",
        "Discover",
        "Volume and pausing",
        "Timers",
        "Skipping",
        "Shortcuts",
        "Desktop and files",
        "More plugins",
    ];

    /// <summary>The group a plugin shows under, by its ID; a new plugin lands under "More plugins" until it is given one.</summary>
    private static string Category(string id) => id switch
    {
        BuiltInPlugins.PlayerLyrics or BuiltInPlugins.DesktopLyrics => "Lyrics",
        BuiltInPlugins.AwayScreen or BuiltInPlugins.WindowShapes or BuiltInPlugins.BeatGlow or BuiltInPlugins.SignalPath => "Look and window",
        BuiltInPlugins.Rediscover or BuiltInPlugins.ArtistOrbit or BuiltInPlugins.SmartPlaylists => "Discover",
        BuiltInPlugins.QuietHours or BuiltInPlugins.DeviceVolume or BuiltInPlugins.PauseOnLock or BuiltInPlugins.PauseOnUnplug or BuiltInPlugins.PauseForSounds => "Volume and pausing",
        BuiltInPlugins.SleepTimer or BuiltInPlugins.Alarm or BuiltInPlugins.FocusTimer => "Timers",
        BuiltInPlugins.SkipSongs or BuiltInPlugins.SkipIntros => "Skipping",
        BuiltInPlugins.SummonBar or BuiltInPlugins.MediaShortcuts => "Shortcuts",
        BuiltInPlugins.SongNotifications or BuiltInPlugins.KeepAwake or BuiltInPlugins.NowPlayingFile or BuiltInPlugins.HistoryExport => "Desktop and files",
        _ => "More plugins",
    };

    private StackPanel Build(BuiltInCard card)
    {
        var panel = new StackPanel { Spacing = 8 };
        card.Switch.OnContent = string.Empty;
        card.Switch.OffContent = string.Empty;
        card.Switch.MinWidth = 0;
        AutomationProperties.SetName(card.Switch, card.Plugin.Name);
        card.Switch.Toggled += (_, _) =>
        {
            if (!_updating)
            {
                _builtIns.Set(card.Plugin.Id, card.Switch.IsOn);
            }
        };
        card.Gear.Click += (_, _) =>
        {
            if (card.MakeSettings?.Invoke() is { } settings)
            {
                _ = ShowSettingsAsync(card.Plugin.Name, settings);
            }
        };
        panel.Children.Add(new SettingRow { Header = card.Plugin.Name, Description = card.Plugin.Description, Content = Controls(card.Gear, card.Switch, card.Plugin.Name) });
        return panel;
    }

    /// <summary>The plugin's switch, and before it the button that opens its settings while it is on.</summary>
    private static StackPanel Controls(Button gear, ToggleSwitch toggle, string name)
    {
        gear.Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"];
        gear.Content = "\uE713";
        gear.VerticalAlignment = VerticalAlignment.Center;
        gear.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(gear, name + " settings");
        ToolTipService.SetToolTip(gear, "Settings");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { gear, toggle } };
    }

    /// <summary>
    /// A plugin's settings in a popup over Settings (the owner's request,
    /// 9 October 2026): opened by the gear beside its switch while it is on.
    /// </summary>
    private async Task ShowSettingsAsync(string name, FrameworkElement settings)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = name,
            Content = new ScrollViewer { Content = settings, MaxHeight = 560, Padding = new Thickness(0, 0, 12, 0) },
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog is open: this one waits for the next click.
        }
        finally
        {
            // The settings may be shown again in a new popup.
            if (dialog.Content is ScrollViewer viewer)
            {
                viewer.Content = null;
            }
        }
    }

    private void OnBuiltInChanged(object? sender, string id)
    {
        if (_builtInCards.TryGetValue(id, out var card))
        {
            RefreshBuiltIn(card);
        }
    }

    private void RefreshBuiltIn(BuiltInCard card)
    {
        var on = _builtIns.IsOn(card.Plugin.Id);
        _updating = true;
        try
        {
            card.Switch.IsOn = on;
        }
        finally
        {
            _updating = false;
        }

        card.Gear.Visibility = on && card.MakeSettings is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class BuiltInCard(BuiltInPlugin plugin, Func<FrameworkElement?>? makeSettings)
    {
        public BuiltInPlugin Plugin { get; } = plugin;

        public ToggleSwitch Switch { get; } = new();

        public Button Gear { get; } = new();

        public Func<FrameworkElement?>? MakeSettings { get; } = makeSettings;
    }
}

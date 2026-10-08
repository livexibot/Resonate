using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.Plugins;

namespace Resonate.App.Controls;

/// <summary>
/// The plugin button of the player bar and of the classic player: shown while
/// a plugin that is on offers commands (the sleep timer), lit while one
/// reports something (its countdown), and opening a menu of the commands.
/// </summary>
internal static class PluginMenu
{
    public static void UpdateButton(Button button, PluginManager plugins)
    {
        var active = plugins.WithCommands();
        button.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // What plugins report shows in the tooltip, and lights the button.
        var notes = active.Where(p => !string.IsNullOrEmpty(p.StatusText)).Select(p => $"{p.Manifest.Name}: {p.StatusText}").ToList();
        ToolTipService.SetToolTip(button, notes.Count == 0 ? "Plugins" : string.Join(Environment.NewLine, notes));
        if (notes.Count == 0)
        {
            button.ClearValue(Control.ForegroundProperty);
        }
        else
        {
            button.Foreground = App.Services.Theme.GetBrush("ResonateAccentBrush");
        }
    }

    /// <summary>Built when opened, so it always shows the plugins' latest commands.</summary>
    public static MenuFlyout Build(PluginManager plugins)
    {
        var menu = new MenuFlyout();
        var active = plugins.WithCommands();
        foreach (var plugin in active)
        {
            var items = active.Count == 1 ? menu.Items : AddGroup(menu, plugin.Manifest.Name);
            if (!string.IsNullOrEmpty(plugin.StatusText))
            {
                items.Add(new MenuFlyoutItem { Text = plugin.StatusText, IsEnabled = false });
                items.Add(new MenuFlyoutSeparator());
            }

            foreach (var command in plugin.Commands)
            {
                var item = new MenuFlyoutItem { Text = command.Title };
                var (id, commandId) = (plugin.Manifest.Id, command.Id);
                item.Click += (_, _) => plugins.Invoke(id, commandId);
                items.Add(item);
            }
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var settings = new MenuFlyoutItem { Text = "Plugin settings" };
        settings.Click += (_, _) => App.MainWindow?.OpenSettings(Pages.SettingsSection.Plugins);
        menu.Items.Add(settings);
        return menu;
    }

    private static IList<MenuFlyoutItemBase> AddGroup(MenuFlyout menu, string name)
    {
        var group = new MenuFlyoutSubItem { Text = name };
        menu.Items.Add(group);
        return group.Items;
    }
}

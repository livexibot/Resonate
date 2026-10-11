using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The plugin button of the player bar and of the classic player: shown while
/// a plugin that is on offers commands (the sleep timer, Skip rules), lit
/// while one reports something (its countdown), and opening a menu of the
/// commands (see <see cref="BuiltInPlugins.AddMenu"/>).
/// </summary>
internal static class PluginMenu
{
    public static void UpdateButton(Button button, BuiltInPlugins plugins)
    {
        var active = plugins.Menus();
        button.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // What plugins report shows in the tooltip, and lights the button.
        var notes = active.Where(p => !string.IsNullOrEmpty(p.Status)).Select(p => $"{p.Plugin.Name}: {p.Status}").ToList();
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
    public static MenuFlyout Build(BuiltInPlugins plugins)
    {
        var menu = new MenuFlyout();
        var active = plugins.Menus();
        foreach (var (plugin, commands, status) in active)
        {
            var items = active.Count == 1 ? menu.Items : AddGroup(menu, plugin.Name);
            if (!string.IsNullOrEmpty(status))
            {
                items.Add(new MenuFlyoutItem { Text = status, IsEnabled = false });
                items.Add(new MenuFlyoutSeparator());
            }

            foreach (var command in commands)
            {
                var item = new MenuFlyoutItem { Text = command.Title };
                var run = command.Run;
                item.Click += (_, _) => run();
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

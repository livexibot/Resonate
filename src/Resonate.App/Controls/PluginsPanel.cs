using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.Plugins;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The Plugins tab of Settings: the plugins built into Resonate (see
/// <see cref="BuiltInPlugins"/>), then every plugin this release offers to
/// download, with a switch that downloads and starts it (or stops it and
/// deletes its files), and its own settings, drawn from its plugin.json. Built in code, like the theme cards, so Native AOT never has
/// to look up a XAML-created type.
/// </summary>
internal sealed partial class PluginsPanel : StackPanel
{
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(700);

    private readonly PluginManager _plugins;
    private readonly BuiltInPlugins _builtIns;
    private readonly Dictionary<string, Card> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuiltInCard> _builtInCards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private bool _updating;

    public PluginsPanel(PluginManager plugins, AppServices services)
    {
        _plugins = plugins;
        _builtIns = services.BuiltIns;
        Spacing = 12;

        // Every plugin in one list by name; each says whether it is built in or downloaded.
        var items = new List<(string Name, FrameworkElement Element)>();
        foreach (var plugin in BuiltInPlugins.All)
        {
            var id = plugin.Id;
            var card = new BuiltInCard(plugin, BuiltInPluginSettings.Has(id) ? () => BuiltInPluginSettings.Create(id, services) : null);
            _builtInCards[plugin.Id] = card;
            items.Add((plugin.Name, Build(card)));
            RefreshBuiltIn(card);
        }

        foreach (var manifest in plugins.Available)
        {
            var card = new Card(manifest);
            _cards[manifest.Id] = card;
            items.Add((manifest.Name, Build(card)));
            Refresh(card);
        }

        foreach (var (_, element) in items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Children.Add(element);
        }

        Loaded += (_, _) =>
        {
            // Loaded can come twice in a row; each handler is held once.
            _plugins.Changed -= OnChanged;
            _builtIns.Changed -= OnBuiltInChanged;
            _plugins.Changed += OnChanged;
            _builtIns.Changed += OnBuiltInChanged;
            foreach (var card in _cards.Values)
            {
                Refresh(card);
            }

            foreach (var card in _builtInCards.Values)
            {
                RefreshBuiltIn(card);
            }
        };
        Unloaded += (_, _) =>
        {
            _plugins.Changed -= OnChanged;
            _builtIns.Changed -= OnBuiltInChanged;
            foreach (var card in _cards.Values)
            {
                card.SaveTyping?.Invoke();
            }
        };
    }

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

    private StackPanel Build(Card card)
    {
        var manifest = card.Manifest;
        var resources = Application.Current.Resources;
        var panel = new StackPanel { Spacing = 8 };

        card.Switch.OnContent = string.Empty;
        card.Switch.OffContent = string.Empty;
        card.Switch.MinWidth = 0;
        AutomationProperties.SetName(card.Switch, manifest.Name);
        card.Switch.Toggled += (_, _) => OnToggled(card);
        card.Gear.Click += (_, _) => _ = ShowSettingsAsync(manifest.Name, card.Settings);
        panel.Children.Add(new SettingRow { Header = manifest.Name, Description = manifest.Description, Content = Controls(card.Gear, card.Switch, manifest.Name) });

        var details = new StackPanel { Spacing = 6, Padding = new Thickness(16, 0, 16, 0) };
        card.Progress.Maximum = 1;
        details.Children.Add(card.Progress);
        card.Status.Style = (Style)resources["ResonateCaptionTextStyle"];
        card.Status.FontWeight = FontWeights.SemiBold;
        card.Status.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(card.Status);
        card.Error.IsClosable = false;
        details.Children.Add(card.Error);
        panel.Children.Add(details);

        card.Settings.Spacing = 8;
        foreach (var setting in manifest.Settings)
        {
            card.Settings.Children.Add(new SettingRow
            {
                Header = setting.Title,
                Description = setting.Description ?? string.Empty,
                Content = SettingControl(card, setting),

                // Text boxes go under the name, so both fit a narrow Settings pane.
                ContentBelow = setting.Type is PluginSettingTypes.Text or PluginSettingTypes.List,
            });
        }

        // The settings open in a popup (the gear), not under the switch.
        return panel;
    }

    /// <summary>The control for one setting; it saves through the manager, which keeps values within the setting's limits.</summary>
    private FrameworkElement SettingControl(Card card, PluginSetting setting)
    {
        var id = card.Manifest.Id;
        void Save(JsonNode? value)
        {
            if (!_updating)
            {
                _plugins.SetSetting(id, setting.Key, value);
            }
        }

        FrameworkElement control = setting.Type switch
        {
            PluginSettingTypes.Toggle => ToggleControl(card, setting, Save),
            PluginSettingTypes.Number => NumberControl(card, setting, Save),
            PluginSettingTypes.Choice => ChoiceControl(card, setting, Save),
            _ => TextControl(card, setting, Save),
        };
        AutomationProperties.SetName(control, setting.Title);
        return control;
    }

    private static ToggleSwitch ToggleControl(Card card, PluginSetting setting, Action<JsonNode?> save)
    {
        var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.Toggled += (_, _) => save(JsonValue.Create(toggle.IsOn));
        card.Show[setting.Key] = value => toggle.IsOn = value?.GetValueKind() == System.Text.Json.JsonValueKind.True;
        return toggle;
    }

    private static FrameworkElement NumberControl(Card card, PluginSetting setting, Action<JsonNode?> save)
    {
        var box = new NumberBox
        {
            Width = 140,
            Minimum = setting.Min ?? double.MinValue,
            Maximum = setting.Max ?? double.MaxValue,
            SmallChange = setting.Step ?? 1,
            LargeChange = (setting.Step ?? 1) * 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        box.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue))
            {
                save(JsonValue.Create(args.NewValue));
            }
        };
        card.Show[setting.Key] = value =>
        {
            var number = value is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;
            if (box.Value != number)
            {
                box.Value = number;
            }
        };

        // A reset to the plugin's own default, shown while the number is another.
        var fallback = setting.Default is JsonValue dv && dv.TryGetValue(out double dn) ? dn : double.NaN;
        var withUnit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        withUnit.Children.Add(box);
        if (!double.IsNaN(fallback))
        {
            ResetButton.Attach(box, () => fallback, setting.Title);
        }

        if (string.IsNullOrEmpty(setting.Unit))
        {
            return withUnit;
        }

        withUnit.Children.Add(new TextBlock
        {
            Text = setting.Unit,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"],
        });
        return withUnit;
    }

    private static ComboBox ChoiceControl(Card card, PluginSetting setting, Action<JsonNode?> save)
    {
        var options = setting.Options ?? [];
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var option in options)
        {
            combo.Items.Add(option.Title);
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Count)
            {
                save(JsonValue.Create(options[combo.SelectedIndex].Value));
            }
        };
        card.Show[setting.Key] = value =>
        {
            var chosen = value is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            combo.SelectedIndex = options.FindIndex(o => o.Value == chosen);
        };
        return combo;
    }

    /// <summary>Text, or a list with one item per line; saved after a pause in typing, and when leaving the box.</summary>
    private TextBox TextControl(Card card, PluginSetting setting, Action<JsonNode?> save)
    {
        var isList = setting.Type == PluginSettingTypes.List;
        var box = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = setting.Placeholder ?? (isList ? "One per line" : string.Empty),
            AcceptsReturn = isList,
            TextWrapping = isList ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = isList ? 88 : 0,
            MaxHeight = isList ? 180 : double.PositiveInfinity,
        };

        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TypingPause;
        timer.IsRepeating = false;

        // The timer holds its handler only while typing waits to be saved: a
        // handler kept for good would keep the whole Settings page in memory.
        TypedEventHandler<DispatcherQueueTimer, object>? tick = null;
        void Commit()
        {
            timer.Stop();
            timer.Tick -= tick;
            card.SaveTyping = null;
            save(JsonValue.Create(box.Text));
        }

        tick = (_, _) => Commit();
        box.TextChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            card.SaveTyping = Commit;
            timer.Stop();
            timer.Tick -= tick;
            timer.Tick += tick;
            timer.Start();
        };
        box.LostFocus += (_, _) =>
        {
            if (timer.IsRunning)
            {
                Commit();
            }
        };
        card.Show[setting.Key] = value =>
        {
            if (box.FocusState != FocusState.Unfocused || timer.IsRunning)
            {
                // Never replace what is being typed.
                return;
            }

            var text = value switch
            {
                JsonArray items => string.Join('\n', items.Select(i => i?.ToString())),
                JsonValue v when v.TryGetValue(out string? s) => s,
                _ => string.Empty,
            };
            if (box.Text != text)
            {
                box.Text = text;
            }
        };
        return box;
    }

    private async void OnToggled(Card card)
    {
        if (_updating)
        {
            return;
        }

        var id = card.Manifest.Id;
        try
        {
            if (card.Switch.IsOn)
            {
                // Downloads and starts in the background; the card follows along through Changed.
                await Task.Run(() => _plugins.EnableAsync(id, CancellationToken.None));
            }
            else
            {
                card.SaveTyping?.Invoke();
                await Task.Run(() => _plugins.DisableAsync(id));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.MainWindow?.ShowMessage($"{card.Manifest.Name}: {ex.Message}", InfoBarSeverity.Error);
        }

        Refresh(card);
    }

    private void OnChanged(object? sender, string id)
    {
        bool schedule;
        lock (_dirty)
        {
            schedule = _dirty.Count == 0;
            _dirty.Add(id);
        }

        if (schedule)
        {
            // Downloads report often; draw the newest state once per frame at most.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                string[] ids;
                lock (_dirty)
                {
                    ids = [.. _dirty];
                    _dirty.Clear();
                }

                foreach (var changed in ids)
                {
                    if (_cards.TryGetValue(changed, out var card))
                    {
                        Refresh(card);
                    }
                }
            });
        }
    }

    private void Refresh(Card card)
    {
        if (_plugins.Get(card.Manifest.Id) is not { } view)
        {
            return;
        }

        _updating = true;
        try
        {
            card.Switch.IsOn = view.IsOn;

            card.Progress.Visibility = view.Status == PluginStatus.Downloading ? Visibility.Visible : Visibility.Collapsed;
            card.Progress.Value = view.Progress;

            var status = view.Status switch
            {
                PluginStatus.Downloading => $"Downloading… {view.Progress:P0}",
                PluginStatus.Starting => "Starting…",
                PluginStatus.Running when _plugins.IsPreview => "On (demo mode: not downloaded)",
                PluginStatus.Running => string.IsNullOrEmpty(view.StatusText) ? "Downloaded" : "Downloaded. " + view.StatusText,
                PluginStatus.Failed => "Downloaded, not running",
                _ when !view.IsOn => "Not downloaded",
                _ => null,
            };
            card.Status.Text = status ?? string.Empty;
            card.Status.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;

            // While it runs, this is the last thing that went wrong; otherwise why it is not running.
            card.Error.Severity = view.Status == PluginStatus.Running ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
            card.Error.Message = view.Error ?? string.Empty;
            card.Error.IsOpen = view.Error is not null;

            card.Gear.Visibility = view.IsOn && view.Manifest.Settings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (key, show) in card.Show)
            {
                show(view.Settings[key]);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>The switch of one built-in plugin, and what makes its settings (opened in a popup while it is on), if it has any.</summary>
    private sealed class BuiltInCard(BuiltInPlugin plugin, Func<FrameworkElement?>? makeSettings)
    {
        public BuiltInPlugin Plugin { get; } = plugin;

        public ToggleSwitch Switch { get; } = new();

        public Button Gear { get; } = new();

        public Func<FrameworkElement?>? MakeSettings { get; } = makeSettings;
    }

    /// <summary>The controls of one plugin.</summary>
    private sealed class Card(PluginManifest manifest)
    {
        public PluginManifest Manifest { get; } = manifest;

        public ToggleSwitch Switch { get; } = new();

        public Button Gear { get; } = new();

        public ProgressBar Progress { get; } = new();

        public TextBlock Status { get; } = new();

        public InfoBar Error { get; } = new();

        public StackPanel Settings { get; } = new();

        /// <summary>Puts a setting's current value into its control.</summary>
        public Dictionary<string, Action<JsonNode?>> Show { get; } = new(StringComparer.Ordinal);

        /// <summary>Saves what is being typed in a text box now, if anything.</summary>
        public Action? SaveTyping { get; set; }
    }
}

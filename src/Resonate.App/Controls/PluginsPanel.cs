using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Resonate.Plugins;

namespace Resonate.App.Controls;

/// <summary>
/// The Plugins section of Settings: every plugin this release offers, a
/// switch that downloads and starts it (or stops it and deletes its files),
/// what it is allowed to do, and its own settings, drawn from its
/// plugin.json. Built in code, like the theme cards, so Native AOT never has
/// to look up a XAML-created type.
/// </summary>
internal sealed partial class PluginsPanel : StackPanel
{
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(700);

    private readonly PluginManager _plugins;
    private readonly Dictionary<string, Card> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private bool _updating;

    public PluginsPanel(PluginManager plugins)
    {
        _plugins = plugins;
        Spacing = 12;

        var resources = Application.Current.Resources;
        Children.Add(new TextBlock { Text = "Plugins", Style = (Style)resources["ResonateTitleTextStyle"] });
        Children.Add(new TextBlock
        {
            Text = Intro(plugins),
            Style = (Style)resources["ResonateSecondaryTextStyle"],
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var manifest in plugins.Available)
        {
            var card = new Card(manifest);
            _cards[manifest.Id] = card;
            Children.Add(Build(card));
            Refresh(card);
        }

        Loaded += (_, _) =>
        {
            _plugins.Changed += OnChanged;
            foreach (var card in _cards.Values)
            {
                Refresh(card);
            }
        };
        Unloaded += (_, _) =>
        {
            _plugins.Changed -= OnChanged;
            foreach (var card in _cards.Values)
            {
                card.SaveTyping?.Invoke();
            }
        };
    }

    private static string Intro(PluginManager plugins)
    {
        if (plugins.Available.Count == 0)
        {
            return "This copy of Resonate was built without plugins. Copies installed from a release get them.";
        }

        return plugins.IsPreview
            ? "Demo mode: turning a plugin on shows its settings, but nothing is downloaded or run."
            : "Optional extras. A plugin downloads only when you turn it on, and its files are deleted when you turn it off. "
                + "Plugins come from Resonate's own releases, run in a separate helper program, and can only do what each one lists.";
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024
        ? (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
        : Math.Max(1, (int)Math.Round(bytes / 1024.0)).ToString(CultureInfo.CurrentCulture) + " KB";

    private StackPanel Build(Card card)
    {
        var manifest = card.Manifest;
        var resources = Application.Current.Resources;
        var panel = new StackPanel { Spacing = 8 };

        card.Switch.OnContent = "On";
        card.Switch.OffContent = "Off";
        AutomationProperties.SetName(card.Switch, manifest.Name);
        card.Switch.Toggled += (_, _) => OnToggled(card);
        panel.Children.Add(new SettingRow { Header = manifest.Name, Description = manifest.Description, Content = card.Switch });

        var details = new StackPanel { Spacing = 6, Padding = new Thickness(16, 0, 16, 0) };
        card.Details.Style = (Style)resources["ResonateCaptionTextStyle"];
        card.Details.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(card.Details);
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
            });
        }

        panel.Children.Add(card.Settings);
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
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off" };
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
        if (string.IsNullOrEmpty(setting.Unit))
        {
            return box;
        }

        var withUnit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        withUnit.Children.Add(box);
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
            Width = 280,
            PlaceholderText = setting.Placeholder ?? (isList ? "One per line" : string.Empty),
            AcceptsReturn = isList,
            TextWrapping = isList ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = isList ? 88 : 0,
            MaxHeight = isList ? 180 : double.PositiveInfinity,
        };

        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TypingPause;
        timer.IsRepeating = false;
        void Commit()
        {
            timer.Stop();
            card.SaveTyping = null;
            save(JsonValue.Create(box.Text));
        }

        timer.Tick += (_, _) => Commit();
        box.TextChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            card.SaveTyping = Commit;
            timer.Stop();
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

            var can = "It can " + PluginPermissions.Describe(view.Manifest.Permissions) + ".";
            var size = view.IsOn || _plugins.IsPreview ? 0 : _plugins.DownloadSize(view.Manifest.Id);
            card.Details.Text = size > 0 ? $"{can} Turning it on downloads {Size(size)}." : can;

            card.Progress.Visibility = view.Status == PluginStatus.Downloading ? Visibility.Visible : Visibility.Collapsed;
            card.Progress.Value = view.Progress;

            var status = view.Status switch
            {
                PluginStatus.Downloading => $"Downloading… {view.Progress:P0}",
                PluginStatus.Starting => "Starting…",
                PluginStatus.Running when _plugins.IsPreview => "On (demo mode: not downloaded)",
                PluginStatus.Running => string.IsNullOrEmpty(view.StatusText) ? "On" : "On. " + view.StatusText,
                PluginStatus.Failed => "Not running",
                _ => null,
            };
            card.Status.Text = status ?? string.Empty;
            card.Status.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;

            // While it runs, this is the last thing that went wrong; otherwise why it is not running.
            card.Error.Severity = view.Status == PluginStatus.Running ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
            card.Error.Message = view.Error ?? string.Empty;
            card.Error.IsOpen = view.Error is not null;

            card.Settings.Visibility = view.IsOn && view.Manifest.Settings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>The controls of one plugin.</summary>
    private sealed class Card(PluginManifest manifest)
    {
        public PluginManifest Manifest { get; } = manifest;

        public ToggleSwitch Switch { get; } = new();

        public TextBlock Details { get; } = new();

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

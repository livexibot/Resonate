using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The rows the built-in plugins' settings are made of (opened from the gear
/// beside a plugin's switch, <see cref="BuiltInPluginSettings"/>): each one
/// saves the settings file as soon as it changes and then calls
/// <c>changed</c>, so the plugin follows at once.
/// </summary>
internal static class PluginRows
{
    public static SettingRow Switch(string header, Func<bool> read, Action<bool> write, AppServices services, Action? changed = null)
    {
        var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, IsOn = read() };
        AutomationProperties.SetName(toggle, header);
        toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn != read())
            {
                write(toggle.IsOn);
                services.SaveSettings();
                changed?.Invoke();
            }
        };
        return new SettingRow { Header = header, Content = toggle };
    }

    public static SettingRow Choice(string header, IReadOnlyList<string> labels, Func<int> read, Action<int> write, AppServices services, Action? changed = null)
    {
        var combo = new ComboBox { MinWidth = 140 };
        foreach (var label in labels)
        {
            combo.Items.Add(label);
        }

        combo.SelectedIndex = Math.Clamp(read(), 0, labels.Count - 1);
        AutomationProperties.SetName(combo, header);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex != read())
            {
                write(combo.SelectedIndex);
                services.SaveSettings();
                changed?.Invoke();
            }
        };
        return new SettingRow { Header = header, Content = combo };
    }

    /// <summary>Whole numbers on a slider, with a reset to <paramref name="fallback"/> once it differs.</summary>
    public static SettingRow Slider(string header, int minimum, int maximum, Func<int> read, Action<int> write, int fallback, string unit, AppServices services, Action? changed = null)
    {
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = 1,
            Width = 180,
            Value = Math.Clamp(read(), minimum, maximum),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, header);
        var value = new TextBlock
        {
            MinWidth = 40,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"],
        };
        void ShowValue(int v) => value.Text = $"{v}{(unit.Length > 0 ? " " + unit : string.Empty)}";
        ShowValue(read());
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            ShowValue(v);
            if (v != read())
            {
                write(v);
                services.SaveSettings();
                changed?.Invoke();
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, value } };
        ResetButton.Attach(slider, () => fallback, header);
        return new SettingRow { Header = header, Content = row };
    }

    /// <summary>A line of text, saved when the box loses focus or Enter is pressed.</summary>
    public static SettingRow Text(string header, string placeholder, Func<string?> read, Action<string?> write, AppServices services, Action? changed = null)
    {
        var box = new TextBox { MinWidth = 260, Text = read() ?? string.Empty, PlaceholderText = placeholder };
        AutomationProperties.SetName(box, header);
        void Commit()
        {
            var text = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            if (text != read())
            {
                write(text);
                services.SaveSettings();
                changed?.Invoke();
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter)
            {
                Commit();
            }
        };
        return new SettingRow { Header = header, Content = box, ContentBelow = true };
    }

    public static SettingRow Button(string header, string label, Action click)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return new SettingRow { Header = header, Content = button };
    }

    /// <summary>A colour, picked in a flyout like the look's colours.</summary>
    public static SettingRow Colour(string header, Func<ThemeColor> read, Action<ThemeColor> write, AppServices services, Action? changed = null)
    {
        var dot = new Ellipse
        {
            Width = 24,
            Height = 24,
            Stroke = services.Theme.GetBrush("ResonateBorderBrush"),
            StrokeThickness = 1,
            Fill = read().Opaque.ToBrush(),
        };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            Color = read().ToColor(),
        };
        picker.ColorChanged += (_, args) =>
        {
            var color = args.NewColor.ToThemeColor().Opaque;
            dot.Fill = color.ToBrush();
            write(color);
            services.SaveSettings();
            changed?.Invoke();
        };
        var button = new Button
        {
            Content = dot,
            Padding = new Thickness(8),
            Flyout = new Flyout { Content = picker, Placement = FlyoutPlacementMode.Bottom },
        };
        AutomationProperties.SetName(button, header);
        return new SettingRow { Header = header, Content = button };
    }
}

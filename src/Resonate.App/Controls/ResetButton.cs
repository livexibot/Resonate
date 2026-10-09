using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Resonate.App.Controls;

/// <summary>
/// A small reset button beside a slider, number box or colour in Settings,
/// shown only while its value differs from the default (the owner's
/// requests, 9 October 2026; a look's colours and sliders go back to the
/// preset the look came from); a click puts the default back, through the
/// control's own change handler. Drop-downs and switches have none.
/// </summary>
internal static class ResetButton
{
    private const string ResetGlyph = "";

    /// <summary>A reset button named after the setting, hidden until <see cref="Attach(Slider, Func{double}, string)"/> shows it.</summary>
    public static Button Create(string name, Action reset)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
            Content = ResetGlyph,
            FontSize = 13,
            Width = 30,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(button, "Reset " + name);
        ToolTipService.SetToolTip(button, "Reset");
        button.Click += (_, _) => reset();
        return button;
    }

    /// <summary>
    /// Puts a reset button just before <paramref name="slider"/> in its row
    /// (a panel), shown while the value is not <paramref name="defaultValue"/>.
    /// Returns the check, for rows whose default changes (a look's sliders
    /// follow the preset the look came from).
    /// </summary>
    public static Action Attach(Slider slider, Func<double> defaultValue, string name)
    {
        var button = Create(name, () => slider.Value = defaultValue());
        if (slider.Parent is Panel panel)
        {
            panel.Children.Insert(panel.Children.IndexOf(slider), button);
        }

        void Show() => button.Visibility = Math.Abs(slider.Value - defaultValue()) > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        slider.ValueChanged += (_, _) => Show();
        Show();
        return Show;
    }

    /// <summary>The same for a number box with a default number (a plugin's setting).</summary>
    public static Action Attach(NumberBox box, Func<double> defaultValue, string name)
    {
        var button = Create(name, () => box.Value = defaultValue());
        if (box.Parent is Panel panel)
        {
            panel.Children.Insert(panel.Children.IndexOf(box), button);
        }

        void Show() => button.Visibility = double.IsNaN(box.Value) || Math.Abs(box.Value - defaultValue()) <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
        box.ValueChanged += (_, _) => Show();
        Show();
        return Show;
    }

    /// <summary>The same for a number box whose default is empty (automatic): shown while it holds a number.</summary>
    public static Action Attach(NumberBox box, string name)
    {
        var button = Create(name, () => box.Value = double.NaN);
        if (box.Parent is Panel panel)
        {
            panel.Children.Insert(panel.Children.IndexOf(box), button);
        }

        void Show() => button.Visibility = double.IsNaN(box.Value) ? Visibility.Collapsed : Visibility.Visible;
        box.ValueChanged += (_, _) => Show();
        Show();
        return Show;
    }
}

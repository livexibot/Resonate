using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Resonate.App.Controls;

/// <summary>
/// One setting: its name and a short explanation on the left, the control
/// on the right (or under them, for a text box), on a card shaped by the
/// look (see Themes/Controls.xaml).
/// </summary>
public sealed partial class SettingRow : ContentControl
{
    public static readonly DependencyProperty ContentBelowProperty = DependencyProperty.Register(
        nameof(ContentBelow),
        typeof(bool),
        typeof(SettingRow),
        new PropertyMetadata(false, (row, _) => ((SettingRow)row).PlaceContent()));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(SettingRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingRow),
        new PropertyMetadata(string.Empty));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Puts the control under the name, across the whole card.</summary>
    public bool ContentBelow
    {
        get => (bool)GetValue(ContentBelowProperty);
        set => SetValue(ContentBelowProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        PlaceContent();
    }

    private void PlaceContent() => VisualStateManager.GoToState(this, ContentBelow ? "Below" : "Beside", false);
}

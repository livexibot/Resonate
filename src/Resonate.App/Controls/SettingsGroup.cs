using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Resonate.App.Controls;

/// <summary>
/// A section of Settings: its heading, which folds the section away and back
/// when clicked (a chevron shows which), and its settings under it (see
/// Themes/Controls.xaml). Which sections are folded is kept between launches,
/// by heading. Folding only hides the settings: their own visibility, which
/// their pages switch, is left as it is.
/// </summary>
public sealed partial class SettingsGroup : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(SettingsGroup),
        new PropertyMetadata(string.Empty, (group, _) => ((SettingsGroup)group).OnHeaderChanged()));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(SettingsGroup),
        new PropertyMetadata(true, (group, _) => ((SettingsGroup)group).OnExpandedChanged()));

    /// <summary>Folded until the user opens it (remembered by heading, like folding).</summary>
    public bool StartsCollapsed
    {
        get => _startsCollapsed;
        set
        {
            _startsCollapsed = value;
            OnHeaderChanged();
        }
    }

    private bool _startsCollapsed;

    private Button? _toggle;

    // True while the folded state is read from the settings, so that is not saved back.
    private bool _restoring;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Whether the settings show; false folds them away under the heading.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        if (_toggle is not null)
        {
            _toggle.Click -= OnToggleClick;
        }

        base.OnApplyTemplate();
        _toggle = GetTemplateChild("HeaderButton") as Button;
        if (_toggle is not null)
        {
            _toggle.Click += OnToggleClick;
        }

        ShowState();
    }

    private void OnToggleClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    /// <summary>A section the user folded before starts folded.</summary>
    private void OnHeaderChanged()
    {
        if (App.Services?.Settings is not { } settings)
        {
            return;
        }

        _restoring = true;
        try
        {
            IsExpanded = StartsCollapsed
                ? settings.ExpandedSettingsSections.Contains(Header)
                : !settings.CollapsedSettingsSections.Contains(Header);
        }
        finally
        {
            _restoring = false;
        }

        ShowState();
    }

    private void OnExpandedChanged()
    {
        ShowState();
        if (_restoring || App.Services is not { } services || string.IsNullOrEmpty(Header))
        {
            return;
        }

        // A section that starts folded remembers being opened; the others remember being folded.
        var list = StartsCollapsed ? services.Settings.ExpandedSettingsSections : services.Settings.CollapsedSettingsSections;
        list.RemoveAll(header => header == Header);
        if (IsExpanded == StartsCollapsed)
        {
            list.Add(Header);
        }

        services.SaveSettings();
    }

    private void ShowState()
    {
        VisualStateManager.GoToState(this, IsExpanded ? "Expanded" : "Collapsed", false);
        if (_toggle is not null)
        {
            AutomationProperties.SetName(_toggle, Header);
            AutomationProperties.SetHelpText(_toggle, IsExpanded ? "Expanded" : "Collapsed");
        }
    }
}

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>
/// The quick search's setting (Settings, Plugins): the keys that open it from
/// any app. Nothing is taken until the user presses the keys they want in
/// the box; it says so when Windows refuses them because another app has them.
/// </summary>
internal static partial class SummonBarSettings
{
    public static FrameworkElement Create(AppServices services) => new ShortcutBox(
        "Shortcut",
        "Opens the bar from any app. Ctrl+K opens it in Resonate.",
        () => services.Settings.SummonBarShortcut,
        shortcut => App.MainWindow?.SetSummonShortcut(shortcut),
        () => App.MainWindow?.PauseSummonShortcut(),
        () => App.MainWindow?.ResumeSummonShortcut(),
        services);

    /// <summary>Keys are being recorded for a shortcut, so the window's own keys (Space) leave them alone.</summary>
    public static bool IsRecording => ShortcutBox.IsRecording;
}

/// <summary>
/// A box that records a global shortcut: click it, press the keys, and it
/// asks <c>apply</c> to take them (which says why when Windows refuses them,
/// because another app has them). While it records, the keys in use pause, so
/// pressing them again records them. Used by Quick search and Media shortcuts.
/// </summary>
internal sealed partial class ShortcutBox : StackPanel
{
    private readonly Func<string?> _read;
    private readonly Func<Shortcut?, string?> _apply;
    private readonly Action _pause;
    private readonly Action _resume;
    private readonly ContentControl _box = new() { IsTabStop = true, UseSystemFocusVisuals = true };
    private readonly TextBlock _keys = new();
    private readonly Button _remove = new() { Content = "Remove" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private bool _recording;

    public ShortcutBox(string header, string? description, Func<string?> read, Func<Shortcut?, string?> apply, Action pause, Action resume, AppServices services)
    {
        _read = read;
        _apply = apply;
        _pause = pause;
        _resume = resume;
        Spacing = 6;
        var resources = Application.Current.Resources;

        _keys.Style = (Style)resources["ResonateBodyTextStyle"];
        _keys.VerticalAlignment = VerticalAlignment.Center;
        _box.Content = new Border
        {
            MinWidth = 168,
            Padding = new Thickness(12, 6, 12, 6),
            Background = services.Theme.GetBrush("ResonateControlBrush"),
            BorderBrush = services.Theme.GetBrush("ResonateBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(services.Theme.Palette.CornerSmall),
            Child = _keys,
        };
        AutomationProperties.SetName(_box, header + ": click, then press the keys");
        _box.Tapped += (_, _) => StartRecording();
        _box.PreviewKeyDown += OnBoxKeyDown;
        _box.LostFocus += (_, _) => StopRecording(keep: false);
        _remove.Click += (_, _) => Apply(null);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_box);
        row.Children.Add(_remove);
        Children.Add(new SettingRow
        {
            Header = header,
            Description = description ?? string.Empty,
            Content = row,
            ContentBelow = description is not null,
        });

        _status.Style = (Style)resources["ResonateCaptionTextStyle"];
        _status.Margin = new Thickness(16, 0, 16, 0);
        _status.Visibility = Visibility.Collapsed;
        Children.Add(_status);
        ShowKeys();
    }

    /// <summary>Keys are being recorded, so the window's own keys (Space) leave them alone.</summary>
    public static bool IsRecording { get; private set; }

    private Shortcut? Current => Shortcut.Parse(_read());

    private void ShowKeys()
    {
        _keys.Text = _recording ? "Press the keys…" : Current?.ToString() ?? "Click to choose keys";
        _remove.Visibility = Current is null || _recording ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string? text)
    {
        _status.Text = text ?? string.Empty;
        _status.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartRecording()
    {
        if (_recording)
        {
            return;
        }

        _recording = true;
        IsRecording = true;
        _pause();
        _box.Focus(FocusState.Programmatic);
        ShowStatus("Esc to cancel.");
        ShowKeys();
    }

    private void StopRecording(bool keep)
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        IsRecording = false;
        if (!keep)
        {
            _resume();
            ShowStatus(null);
        }

        ShowKeys();
    }

    private void OnBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording)
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space)
            {
                e.Handled = true;
                StartRecording();
            }

            return;
        }

        e.Handled = true;
        var key = e.Key;
        if (Shortcut.IsModifier(key))
        {
            return;
        }

        var shortcut = new Shortcut(IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift), IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows), key);
        if (key == VirtualKey.Escape && shortcut.Modifiers == 0)
        {
            StopRecording(keep: false);
            return;
        }

        if (shortcut.Problem is { } problem)
        {
            ShowStatus(problem);
            return;
        }

        StopRecording(keep: true);
        Apply(shortcut);
    }

    private void Apply(Shortcut? shortcut)
    {
        var problem = _apply(shortcut);
        ShowStatus(problem);
        ShowKeys();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}

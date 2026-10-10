using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;
using Resonate.Themes;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>
/// The keyboard shortcuts under Settings, General, Keyboard shortcuts, each one changeable
/// (the owner's request, 9 October 2026): click a command's keys, press the
/// new ones, and the window uses them at once. Keys another command had are
/// taken from it; Esc stops recording, Backspace leaves the command without
/// keys, and the reset button puts its usual keys back.
/// </summary>
internal sealed partial class KeyShortcutsList : StackPanel
{
    private readonly AppServices _services;
    private readonly List<(AppCommand Command, Button Keys, Button Reset)> _rows = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private Button? _recording;
    private AppCommand _recordingCommand;

    public KeyShortcutsList(AppServices services)
    {
        _services = services;
        Spacing = 8;
        var resources = Application.Current.Resources;
        var nameStyle = (Style)resources["ResonateSecondaryTextStyle"];
        var keysStyle = (Style)resources["ResonateSubtleButtonStyle"];

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (command, name) in AppKeys.Commands)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = name, Style = nameStyle, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var keys = new Button { Style = keysStyle, MinWidth = 132, HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(keys, name + " keys");
            keys.Click += (_, _) => StartRecording(keys, command);
            keys.PreviewKeyDown += OnKeysPreviewKeyDown;
            keys.LostFocus += (_, _) => StopRecording();
            var reset = ResetButton.Create(name, () => Reset(command));

            Grid.SetRow(label, row);
            Grid.SetRow(reset, row);
            Grid.SetColumn(reset, 1);
            Grid.SetRow(keys, row);
            Grid.SetColumn(keys, 2);
            grid.Children.Add(label);
            grid.Children.Add(reset);
            grid.Children.Add(keys);
            _rows.Add((command, keys, reset));
        }

        Children.Add(grid);
        _status.Style = (Style)resources["ResonateCaptionTextStyle"];
        Children.Add(_status);
        ShowKeys();
        Unloaded += (_, _) => StopRecording();
    }

    /// <summary>Keys are being recorded, so the window's shortcuts leave them alone.</summary>
    public static bool IsRecording { get; private set; }

    private Dictionary<string, string> Own => _services.Settings.KeyShortcuts ??= [];

    private void ShowKeys()
    {
        foreach (var (command, keys, reset) in _rows)
        {
            keys.Content = ReferenceEquals(keys, _recording) ? "Press the keys…" : AppKeys.Display(AppKeys.For(command, Own));
            reset.Visibility = AppKeys.IsChanged(command, Own) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowStatus(string? text)
    {
        _status.Text = text ?? string.Empty;
        _status.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartRecording(Button keys, AppCommand command)
    {
        _recording = keys;
        _recordingCommand = command;
        IsRecording = true;
        keys.Focus(FocusState.Programmatic);
        ShowStatus("Esc to cancel, Backspace for none.");
        ShowKeys();
    }

    private void StopRecording()
    {
        if (_recording is null)
        {
            return;
        }

        _recording = null;
        IsRecording = false;
        ShowKeys();
    }

    private void OnKeysPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_recording is null || !ReferenceEquals(sender, _recording))
        {
            return;
        }

        e.Handled = true;
        var key = e.Key;
        if (Shortcut.IsModifier(key))
        {
            return;
        }

        var control = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        var shift = IsDown(VirtualKey.Shift);
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            ShowStatus("Windows keeps the Windows key. Pick other keys.");
            return;
        }

        var plain = !control && !alt && !shift;
        if (plain && key == VirtualKey.Escape)
        {
            StopRecording();
            ShowStatus(null);
            return;
        }

        if (plain && key == VirtualKey.Back)
        {
            Save(_recordingCommand, null);
            return;
        }

        var combo = new KeyCombo(control, alt, shift, Shortcut.NameOf(key));
        if (AppKeys.Problem(combo) is { } problem)
        {
            ShowStatus(problem);
            return;
        }

        Save(_recordingCommand, combo);
    }

    private void Save(AppCommand command, KeyCombo? combo)
    {
        var taken = AppKeys.Assign(Own, command, combo);
        StopRecording();
        Apply(taken);
    }

    private void Reset(AppCommand command) => Apply(AppKeys.Reset(Own, command));

    /// <summary>Saves the keys, has the window use them, and says which command lost its keys to this one.</summary>
    private void Apply(AppCommand? taken)
    {
        // Commands back on their usual keys need no entry of their own.
        foreach (var (command, _) in AppKeys.Commands)
        {
            if (!AppKeys.IsChanged(command, Own))
            {
                Own.Remove(command.ToString());
            }
        }

        _services.SaveSettings();
        App.MainWindow?.ApplyShortcuts();
        ShowKeys();
        ShowStatus(taken is { } other ? $"Taken from {AppKeys.NameOf(other)}." : null);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}

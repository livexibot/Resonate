using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Themes;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Resonate.App;

/// <summary>
/// The window's keyboard shortcuts, which the user can change under
/// Settings, About, Help (<see cref="AppKeys"/>; Space plays and pauses at
/// first). Keys with Ctrl or Alt are keyboard accelerators, so they work
/// wherever the keyboard is in the window; keys without them (Space,
/// Shift+Right) are caught on their way down to the control that has the
/// keyboard. A text field keeps the keys it types and edits with, and keys
/// being recorded for a shortcut are left alone.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(10);

    private readonly List<KeyboardAccelerator> _shortcutAccelerators = [];
    private Dictionary<KeyCombo, AppCommand> _shortcuts = [];

    /// <summary>Takes the shortcuts from the settings: at start, and whenever the user changes one.</summary>
    internal void ApplyShortcuts()
    {
        _shortcuts = AppKeys.Table(_services.Settings.KeyShortcuts);
        foreach (var old in _shortcutAccelerators)
        {
            RootGrid.KeyboardAccelerators.Remove(old);
        }

        _shortcutAccelerators.Clear();
        foreach (var (combo, command) in _shortcuts)
        {
            if ((!combo.Control && !combo.Alt) || Shortcut.KeyNamed(combo.Key) is not { } key)
            {
                continue;
            }

            var modifiers = (combo.Control ? VirtualKeyModifiers.Control : VirtualKeyModifiers.None)
                | (combo.Alt ? VirtualKeyModifiers.Menu : VirtualKeyModifiers.None)
                | (combo.Shift ? VirtualKeyModifiers.Shift : VirtualKeyModifiers.None);
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) => args.Handled = RunShortcut(command, combo);
            RootGrid.KeyboardAccelerators.Add(accelerator);
            _shortcutAccelerators.Add(accelerator);
        }
    }

    /// <summary>Keys without Ctrl or Alt: Space, Shift+Right and the like.</summary>
    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Shortcut.IsModifier(e.Key) || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)
            || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            return;
        }

        var combo = new KeyCombo(false, false, IsDown(VirtualKey.Shift), Shortcut.NameOf(e.Key));
        if (_shortcuts.TryGetValue(combo, out var command) && RunShortcut(command, combo))
        {
            e.Handled = true;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Runs the shortcut unless the keys belong to something else right now; whether it ran.</summary>
    private bool RunShortcut(AppCommand command, KeyCombo combo)
    {
        if (ShortcutBox.IsRecording || KeyShortcutsList.IsRecording)
        {
            return false;
        }

        // Signing in: only the size keys.
        if (ShellGrid.Visibility != Visibility.Visible && command is not (AppCommand.AppSizeUp or AppCommand.AppSizeDown or AppCommand.AppSizeReset))
        {
            return false;
        }

        if (AppKeys.BelongsToText(combo) && FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
        {
            return false;
        }

        return RunCommand(command);
    }

    private bool RunCommand(AppCommand command)
    {
        var player = _services.Player;
        var state = player.State;
        switch (command)
        {
            case AppCommand.PlayPause:
                _ = player.TogglePlayPauseAsync();
                break;
            case AppCommand.Next:
                _ = player.NextAsync();
                break;
            case AppCommand.Previous:
                _ = player.PreviousAsync();
                break;
            case AppCommand.SeekForward or AppCommand.SeekBack when state.Duration > TimeSpan.Zero:
                var step = command == AppCommand.SeekForward ? SeekStep : -SeekStep;
                var at = state.PositionAt(DateTimeOffset.UtcNow) + step;
                _ = player.SeekAsync(at < TimeSpan.Zero ? TimeSpan.Zero : at > state.Duration ? state.Duration : at);
                break;
            case AppCommand.SeekForward or AppCommand.SeekBack:
                return false;
            case AppCommand.VolumeUp:
                _ = player.SetVolumeAsync(Math.Min(1, state.Volume + 0.05));
                break;
            case AppCommand.VolumeDown:
                _ = player.SetVolumeAsync(Math.Max(0, state.Volume - 0.05));
                break;
            case AppCommand.Mute:
                PlayerBar.ToggleMute();
                break;
            case AppCommand.Shuffle:
                _ = player.SetShuffleAsync(!state.Shuffle);
                break;
            case AppCommand.Repeat:
                _ = player.SetRepeatAsync(NextRepeat(state.Repeat));
                break;
            case AppCommand.Like:
                LikePlaying(state);
                break;
            case AppCommand.Search:
                FocusSearch();
                break;
            case AppCommand.QuickSearch when SummonBarOn:
                ToggleSummonBar(Hwnd);
                break;
            case AppCommand.QuickSearch:
                return false;
            case AppCommand.Home:
                Open(HomeKey);
                break;
            case AppCommand.LikedSongs:
                Open(LikedSongsKey);
                break;
            case AppCommand.Queue:
                ToggleQueue();
                break;
            case AppCommand.Lyrics:
                ToggleLyrics();
                break;
            case AppCommand.Settings:
                ToggleSettings();
                break;
            case AppCommand.NewPlaylist:
                _ = CreatePlaylistAsync();
                break;
            case AppCommand.MiniPlayer:
                ToggleMiniPlayer();
                break;
            case AppCommand.Back:
                GoBack();
                break;
            case AppCommand.Forward:
                GoForward();
                break;
            case AppCommand.AppSizeUp:
                StepAppSize(1);
                break;
            case AppCommand.AppSizeDown:
                StepAppSize(-1);
                break;
            case AppCommand.AppSizeReset:
                StepAppSize(0);
                break;
            default:
                return false;
        }

        return true;
    }
}

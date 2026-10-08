using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Controls;
using Resonate.App.Pages;

namespace Resonate.App;

/// <summary>
/// The classic player in the window: it takes the player bar's place while
/// it is chosen, and its buttons reach into the rest of the window (search,
/// the queue, the equalizer, minimising).
/// </summary>
public sealed partial class MainWindow
{
    private const int SwMinimize = 6;

    // Made when it is chosen and dropped when it is not, so nothing of it runs while the player bar shows.
    private ClassicPlayer? _classicPlayer;
    private bool _windowActive = true;

    /// <summary>The queue pane is open (the classic player's PL button is lit).</summary>
    public bool IsQueueOpen => QueuePane.IsOpen;

    /// <summary>Raised on the interface thread when the queue pane opens or closes.</summary>
    public event EventHandler? QueueOpenChanged;

    /// <summary>Opens Settings at one of its sections.</summary>
    public void OpenSettings(SettingsSection section)
    {
        // Already in Settings: glide there; arriving: jump straight to it.
        var alreadyOpen = CurrentPage is SettingsPage;
        Open(SettingsKey);
        if (CurrentPage is SettingsPage page)
        {
            page.ShowSection(section, animate: alreadyOpen);
        }
    }

    /// <summary>Opens Search with the cursor in its box (Ctrl+F, and the classic player's eject button).</summary>
    public void FocusSearch()
    {
        if (ShellGrid.Visibility == Visibility.Visible)
        {
            OpenSearch();
            SearchPage.FocusSearchBox(ContentFrame);
        }
    }

    /// <summary>Minimises the window, as its title bar button does.</summary>
    public void Minimize() => ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), SwMinimize);

    /// <summary>Called once, from the constructor.</summary>
    private void SetUpClassicPlayer()
    {
        _services.Skins.OptionsChanged += (_, _) => ApplyPlayerStyle();
        Activated += OnWindowActivated;
    }

    /// <summary>The player bar, or the classic player in its place (Settings decides); neither while signing in.</summary>
    private void ApplyPlayerStyle()
    {
        var shell = ShellGrid.Visibility == Visibility.Visible;
        var classic = shell && _services.Skins.UsesClassicPlayer;
        PlayerBar.Visibility = shell && !classic ? Visibility.Visible : Visibility.Collapsed;
        ClassicElevation.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;

        if (classic && _classicPlayer is null)
        {
            _classicPlayer = new ClassicPlayer();
            _classicPlayer.SetWindowActive(_windowActive);
            _classicPlayer.SetWindowShown(IsShown);
            Grid.SetRow(_classicPlayer, 2);

            // Above its shadow, like the player bar above its own.
            RootGrid.Children.Insert(RootGrid.Children.IndexOf(ClassicElevation) + 1, _classicPlayer);
        }
        else if (!classic && _classicPlayer is not null)
        {
            RootGrid.Children.Remove(_classicPlayer);
            _classicPlayer = null;
        }

        TellPlayersShown();
    }

    /// <summary>
    /// Each player hears whether it can be seen: the window is shown and the
    /// player is the one in use. A hidden player rests its clock and its motion.
    /// </summary>
    private void TellPlayersShown()
    {
        PlayerBar.SetWindowShown(IsShown && PlayerBar.Visibility == Visibility.Visible);
        _classicPlayer?.SetWindowShown(IsShown);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // The skin draws its title bar lit while the window has focus, as Winamp did.
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        _classicPlayer?.SetWindowActive(_windowActive);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);
}

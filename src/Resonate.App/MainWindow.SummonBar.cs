using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Controls;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Resonate.App;

/// <summary>
/// The summon bar, a built-in plugin: the user's own shortcut (none until
/// they pick one) opens a search box over any app, and Ctrl+K opens the same
/// bar in Resonate as its command palette. The shortcut is registered with
/// Windows on this window (RegisterHotKey) and heard through
/// <see cref="WindowHook"/>; it works while Resonate runs, minimised too, but
/// not once its window is closed. The bar's window is made on first use.
/// </summary>
public sealed partial class MainWindow
{
    private const int SummonHotkeyId = 0x5253;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>Songs at most added to the queue for a whole list (each one is a request to Spotify).</summary>
    private const int QueueListLimit = 25;

    private bool _summonOn;
    private IDisposable? _summonHook;
    private SummonBarWindow? _summonBar;
    private IReadOnlyList<SummonCommand>? _summonCommands;

    private bool SummonBarOn => _summonOn;

    partial void SetUpSummonBar()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.SummonBar)
            {
                TurnSummonBar(_services.BuiltIns.IsOn(id));
            }
        };

        // The bar's window would keep Resonate running after its window closed.
        Closed += (_, _) => TurnSummonBar(false);

        if (_services.BuiltIns.IsOn(BuiltInPlugins.SummonBar))
        {
            TurnSummonBar(true);
        }
    }

    private void TurnSummonBar(bool on)
    {
        if (on == SummonBarOn)
        {
            return;
        }

        if (on)
        {
            _summonOn = true;
            if (RegisterSummonShortcut() is { } problem)
            {
                ShowMessage("Summon bar: " + problem, InfoBarSeverity.Warning);
            }

            return;
        }

        _summonOn = false;
        UnregisterSummonShortcut();
        _summonBar?.Close();
        _summonBar = null;
    }

    /// <summary>
    /// Uses <paramref name="shortcut"/> (null for none) from now on. Returns
    /// what went wrong, such as another app having those keys; the keys used
    /// before then stay.
    /// </summary>
    internal string? SetSummonShortcut(Shortcut? shortcut)
    {
        var settings = _services.Settings;
        var before = settings.SummonBarShortcut;
        settings.SummonBarShortcut = shortcut?.ToString();
        if (RegisterSummonShortcut() is { } problem)
        {
            settings.SummonBarShortcut = before;
            RegisterSummonShortcut();
            return problem;
        }

        _services.SaveSettings();
        return null;
    }

    /// <summary>While Settings records new keys, the ones in use do nothing.</summary>
    internal void PauseSummonShortcut() => UnregisterSummonShortcut();

    internal void ResumeSummonShortcut() => RegisterSummonShortcut();

    /// <summary>Registers the saved shortcut with Windows; returns why it could not be.</summary>
    private string? RegisterSummonShortcut()
    {
        UnregisterSummonShortcut();
        if (!SummonBarOn || Shortcut.Parse(_services.Settings.SummonBarShortcut) is not { } shortcut)
        {
            return null;
        }

        var hwnd = Hwnd;
        _summonHook = WindowHook.Listen(hwnd, OnSummonMessage);
        if (RegisterHotKey(hwnd, SummonHotkeyId, shortcut.Modifiers | ModNoRepeat, (uint)shortcut.Key))
        {
            return null;
        }

        var error = Marshal.GetLastPInvokeError();
        UnregisterSummonShortcut();
        return error == ErrorHotkeyAlreadyRegistered
            ? $"Another app uses {shortcut}. Pick other keys."
            : $"Windows did not accept {shortcut}. Pick other keys.";
    }

    private void UnregisterSummonShortcut()
    {
        if (_summonHook is null)
        {
            return;
        }

        UnregisterHotKey(Hwnd, SummonHotkeyId);
        _summonHook.Dispose();
        _summonHook = null;
    }

    /// <summary>Inside the window's message handling: the shortcut was pressed, in whichever app.</summary>
    private void OnSummonMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WindowHook.WmHotkey && wParam == SummonHotkeyId)
        {
            // The app in front now gets the keyboard back when the bar closes.
            var previous = GetForegroundWindow();
            DispatcherQueue.TryEnqueue(() => ToggleSummonBar(previous));
        }
    }

    private void ToggleSummonBar(nint previousWindow)
    {
        if (!SummonBarOn)
        {
            return;
        }

        if (_summonBar is { IsOpen: true } open)
        {
            open.Dismiss(returnFocus: true);
            return;
        }

        if (ShellGrid.Visibility != Visibility.Visible)
        {
            // Not signed in: Resonate's own window says what to do.
            BringToFront();
            return;
        }

        _summonBar ??= new SummonBarWindow(_services, this);
        _summonBar.Summon(previousWindow);
    }

    /// <summary>What the bar offers as Resonate's command palette.</summary>
    internal IReadOnlyList<SummonCommand> SummonCommands() => _summonCommands ??=
    [
        new("Play or pause", "", "resume stop", false, () => _ = _services.Player.TogglePlayPauseAsync()),
        new("Next song", "", "skip forward", false, () => _ = _services.Player.NextAsync()),
        new("Previous song", "", "back", false, () => _ = _services.Player.PreviousAsync()),
        new("Shuffle", "", "random", false, () => _ = _services.Player.SetShuffleAsync(!_services.Player.State.Shuffle)),
        new("Repeat", "", "loop", false, () => _ = _services.Player.SetRepeatAsync(NextRepeat(_services.Player.State.Repeat))),
        new("Home", "", "start", true, () => ShowInWindow(() => Open(HomeKey))),
        new("Search", "", "find", true, () => ShowInWindow(FocusSearch)),
        new("Liked Songs", "", "favourites hearts", true, () => ShowInWindow(() => Open(LikedSongsKey))),
        new("Local Files", "", "music folder", true, () => ShowInWindow(() => Open(LocalFilesKey))),
        new("Queue", "", "up next", true, () => ShowInWindow(() => ShowQueue(true))),
        new("Settings", "", "options preferences", true, () => ShowInWindow(() => ShowSettings(true))),
    ];

    /// <summary>Plays a result (Enter) or opens it in Resonate (Ctrl+Enter).</summary>
    internal void RunFromSummon(QuickItem item, SummonAction action, IReadOnlyList<TrackInfo> localSongs)
    {
        if (action == SummonAction.Open)
        {
            ShowInWindow(() => OpenSummoned(item));
            return;
        }

        switch (item)
        {
            case { Kind: QuickKind.LocalSong, Track: { } file }:
                var index = localSongs.ToList().IndexOf(file);
                _ = _services.Player.PlayAsync(index >= 0
                    ? new PlayRequest(localSongs, index, null, "Local Files")
                    : new PlayRequest([file], 0, null, "Local Files"));
                break;
            case { Kind: QuickKind.Song, Track: { IsPlayable: true } song }:
                // In its album, so the album goes on after it (as Search plays songs).
                _ = _services.Player.PlayAsync(new PlayRequest([song], 0, song.AlbumUri, song.Album));
                break;
            case { Kind: QuickKind.LikedSongs }:
                _ = PlayPlaylistAsync(ListNavItem(LikedSongsKey, "Liked Songs"), shuffle: null);
                break;
            case { Kind: QuickKind.Playlist, Id: { } id } when Playlists.FirstOrDefault(p => p.Id == id) is { } playlist:
                _ = PlayPlaylistAsync(playlist, shuffle: null);
                break;
            case { Uri: { } uri } when item.Kind is QuickKind.Playlist or QuickKind.Album or QuickKind.Artist:
                _ = _services.Player.PlayContextAsync(uri);
                break;
        }
    }

    /// <summary>Adds a result to the queue (Shift+Enter): a song, or the first songs of a list. Returns what the bar's chip says.</summary>
    internal string QueueFromSummon(QuickItem item)
    {
        if (item.Track is { } track)
        {
            if (track.FilePath is null && !track.IsPlayable)
            {
                return "Can't be queued";
            }

            _ = _services.Player.AddToQueueAsync(track);
            return "Added to queue";
        }

        var key = item.Kind switch
        {
            QuickKind.LikedSongs => LikedSongsKey,
            QuickKind.Album when item.Id is { } album => AlbumSource.Prefix + album,
            QuickKind.Playlist => item.Id,
            _ => null,
        };
        if (key is null)
        {
            return "Play it with Enter";
        }

        _ = QueueListAsync(key);
        return "Adding to queue";
    }

    private async Task QueueListAsync(string key)
    {
        try
        {
            var source = TrackListSource.For(key, _services);
            var list = await Task.Run(() => source.LoadAllAsync(_lifetime.Token));
            foreach (var track in list.Tracks.Where(t => t.IsPlayable || t.FilePath is not null).Take(QueueListLimit))
            {
                await _services.Player.AddToQueueAsync(track);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
    }

    private void OpenSummoned(QuickItem item)
    {
        var key = item.Kind switch
        {
            QuickKind.LikedSongs => LikedSongsKey,
            QuickKind.LocalSong => LocalFilesKey,
            QuickKind.Playlist => item.Id,
            QuickKind.Album => item.Id is { } album ? AlbumSource.Prefix + album : null,
            QuickKind.Artist => item.Id is { } artist ? ArtistPrefix + artist : null,
            _ => item.Track?.AlbumId is { } album ? AlbumSource.Prefix + album
                : item.Track?.ArtistRefs.FirstOrDefault(a => a.Id is not null)?.Id is { } artist ? ArtistPrefix + artist
                : null,
        };
        if (key is not null)
        {
            Open(key);
        }
    }

    /// <summary>A list that is not a playlist (Liked Songs), played the way the sidebar plays a playlist.</summary>
    private static PlaylistNavItem ListNavItem(string key, string name) => new(new SimplifiedPlaylist { Id = key, Name = name });

    /// <summary>Does something in Resonate's window and brings it to the front, restored if minimised.</summary>
    private void ShowInWindow(Action action)
    {
        // From the mini player too: the full window comes back in its place.
        LeaveMiniPlayer();
        BringToFront();
        action();
    }

    private void BringToFront()
    {
        var hwnd = Hwnd;
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        Activate();
        SetForegroundWindow(hwnd);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}

using Microsoft.UI.Dispatching;
using Resonate.App.Services;
using Resonate.Spotify.Lyrics;

namespace Resonate.App;

/// <summary>
/// Lyrics in the player and Desktop lyrics, built-in plugins: the line being
/// sung and the next one, under "Song · Artist" in the player bar and in a
/// small window on top of every app (<see cref="DesktopLyricsWindow"/>). The
/// song's synced lyrics come from the same library as the lyrics pane
/// (LRCLIB, kept 30 days on this PC), asked once per song while either is
/// on; the lines follow the player's clock four times a second, only while
/// music plays and something shows them.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan LyricTick = TimeSpan.FromMilliseconds(250);

    private bool _playerLyricsOn;
    private DispatcherQueueTimer? _lyricTimer;
    private (string?, string?) _lyricSong;
    private SongLyrics? _playerLyrics;
    private CancellationTokenSource? _lyricLoading;
    private int _lyricQueued;
    private DesktopLyricsWindow? _desktopLyrics;

    private bool InPlayerLyricsOn => _services.BuiltIns.IsOn(BuiltInPlugins.PlayerLyrics);

    private bool DesktopLyricsOn => _services.BuiltIns.IsOn(BuiltInPlugins.DesktopLyrics);

    partial void SetUpPlayerLyrics()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id is BuiltInPlugins.PlayerLyrics or BuiltInPlugins.DesktopLyrics)
            {
                FollowLyricPlugins();
            }
        };
        _services.Player.StateChanged += (_, _) =>
        {
            if (_playerLyricsOn && Interlocked.Exchange(ref _lyricQueued, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref _lyricQueued, 0);
                    FollowPlayerLyrics();
                });
            }
        };
        ShownChanged += (_, _) => FollowPlayerLyrics();
        Closed += (_, _) => CloseDesktopLyrics();
        FollowLyricPlugins();
    }

    /// <summary>Either plugin turned on or off: the player bar's lines, the desktop window, and whether lyrics are followed at all.</summary>
    private void FollowLyricPlugins()
    {
        if (!InPlayerLyricsOn)
        {
            PlayerBar.ShowLyricLine(null);
        }

        if (DesktopLyricsOn)
        {
            if (_desktopLyrics is null)
            {
                _desktopLyrics = new DesktopLyricsWindow(_services);
                _desktopLyrics.Closed += (_, _) => _desktopLyrics = null;
                _desktopLyrics.ShowQuietly();
            }
        }
        else
        {
            CloseDesktopLyrics();
        }

        TurnPlayerLyrics(InPlayerLyricsOn || DesktopLyricsOn);
        ShowLyricLine();
    }

    /// <summary>Settings changed the desktop lyrics' size.</summary>
    internal void RefreshDesktopLyrics() => _desktopLyrics?.ApplySize();

    private void CloseDesktopLyrics()
    {
        var window = _desktopLyrics;
        _desktopLyrics = null;
        window?.Close();
    }

    private void TurnPlayerLyrics(bool on)
    {
        _playerLyricsOn = on;
        if (!on)
        {
            _lyricTimer?.Stop();
            _lyricLoading?.Cancel();
            _lyricSong = default;
            _playerLyrics = null;
            PlayerBar.ShowLyricLine(null);
            return;
        }

        if (_lyricTimer is null)
        {
            _lyricTimer = DispatcherQueue.CreateTimer();
            _lyricTimer.Interval = LyricTick;
            _lyricTimer.Tick += (_, _) => ShowLyricLine();
        }

        FollowPlayerLyrics();
    }

    /// <summary>A new song gets its lyrics looked up; the lines follow only while it plays and something shows them.</summary>
    private void FollowPlayerLyrics()
    {
        if (!_playerLyricsOn || _lyricTimer is null)
        {
            return;
        }

        var state = _services.Player.State;
        var song = (state.Title, state.Artists);
        if (song != _lyricSong)
        {
            _lyricSong = song;
            _playerLyrics = null;
            PlayerBar.ShowLyricLine(null);
            ShowLyricLine();
            _lyricLoading?.Cancel();
            if (LyricsQuery.For(state.Title, state.Artists, state.Album, state.Duration) is { } query)
            {
                _lyricLoading = new CancellationTokenSource();
                _ = LoadPlayerLyricsAsync(query, song, _lyricLoading.Token);
            }
        }

        var shown = (IsShown && InPlayerLyricsOn) || _desktopLyrics is not null;
        if (state.IsPlaying && shown && _playerLyrics is { IsSynced: true })
        {
            _lyricTimer.Start();
            ShowLyricLine();
        }
        else
        {
            _lyricTimer.Stop();
        }
    }

    private async Task LoadPlayerLyricsAsync(LyricsQuery query, (string?, string?) song, CancellationToken cancellationToken)
    {
        try
        {
            var lyrics = await (_lyricsLibrary ??= CreateLyricsLibrary()).GetAsync(query, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && song == _lyricSong)
            {
                _playerLyrics = lyrics;
                FollowPlayerLyrics();
                ShowLyricLine();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or System.Text.Json.JsonException)
        {
            // No line for this song; the next song tries again.
        }
    }

    private void ShowLyricLine()
    {
        var state = _services.Player.State;
        string? line = null;
        string? next = null;
        if (_playerLyrics is { IsSynced: true } lyrics)
        {
            // The line being sung and the next one; before the first, only what comes.
            var index = lyrics.ActiveLine(state.PositionAt(DateTimeOffset.UtcNow));
            next = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Text : null;
            line = index >= 0 ? lyrics.Lines[index].Text : null;
            if (InPlayerLyricsOn)
            {
                // Before the first line, what comes; after it, the next one only if the user keeps it.
                PlayerBar.ShowLyricLine(line, line is null || _services.Settings.PlayerLyricsNextLine ? next : null);
            }
        }

        // Without synced lyrics the desktop window shows the song itself.
        _desktopLyrics?.Show(state.Title is null ? null : (line, next, state.Title, state.Artists));
    }
}

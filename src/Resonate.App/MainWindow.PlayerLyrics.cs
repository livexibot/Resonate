using Microsoft.UI.Dispatching;
using Resonate.App.Services;
using Resonate.Spotify.Lyrics;

namespace Resonate.App;

/// <summary>
/// Lyrics in the player, a built-in plugin: the line being sung shows under
/// the song in the player bar. The song's synced lyrics come from the same
/// library as the lyrics pane (LRCLIB, kept 30 days on this PC), asked once
/// per song while the plugin is on; the line follows the player's clock four
/// times a second, only while music plays and the window shows.
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

    partial void SetUpPlayerLyrics()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.PlayerLyrics)
            {
                TurnPlayerLyrics(_services.BuiltIns.IsOn(id));
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
        TurnPlayerLyrics(_services.BuiltIns.IsOn(BuiltInPlugins.PlayerLyrics));
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

    /// <summary>A new song gets its lyrics looked up; the line follows only while it plays and the window shows.</summary>
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
            _lyricLoading?.Cancel();
            if (LyricsQuery.For(state.Title, state.Artists, state.Album, state.Duration) is { } query)
            {
                _lyricLoading = new CancellationTokenSource();
                _ = LoadPlayerLyricsAsync(query, song, _lyricLoading.Token);
            }
        }

        if (state.IsPlaying && IsShown && _playerLyrics is { IsSynced: true })
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
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or System.Text.Json.JsonException)
        {
            // No line for this song; the next song tries again.
        }
    }

    private void ShowLyricLine()
    {
        if (_playerLyrics is not { IsSynced: true } lyrics)
        {
            return;
        }

        var index = lyrics.ActiveLine(_services.Player.State.PositionAt(DateTimeOffset.UtcNow));
        PlayerBar.ShowLyricLine(index >= 0 ? lyrics.Lines[index].Text : null);
    }
}

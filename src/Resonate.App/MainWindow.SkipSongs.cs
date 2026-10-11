using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App;

/// <summary>
/// Skip rules, a built-in plugin (a downloaded one until 10 October 2026,
/// with the same ID, menu and settings): skips a song as soon as it plays
/// when it matches the artists, versions, songs or lengths in its settings,
/// and offers "Always skip this song" and "Always skip &lt;artist&gt;" in the
/// player's plugin menu. When every song matches it rests for a minute
/// rather than skipping for ever. Rules in <see cref="SkipFilter"/>.
/// </summary>
public sealed partial class MainWindow
{
    private const int MaxArtistCommands = 3;

    private readonly SkipLoopGuard _skipGuard = new();
    private string? _skipShownSong;
    private string? _skipCheckedSong;
    private int _skippedCount;
    private string? _skipStatus;

    partial void SetUpSkipSongs()
    {
        _services.BuiltIns.AddMenu(BuiltInPlugins.SkipSongs, SkipCommands, SkipStatus);
        FollowPlugin(BuiltInPlugins.SkipSongs, () =>
        {
            _skipCheckedSong = null;
            FollowSkipSongs();
        });
        FollowPlayer(FollowSkipSongs);
    }

    /// <summary>A new song changes the menu; a song is checked once, as soon as it plays.</summary>
    private void FollowSkipSongs()
    {
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.SkipSongs))
        {
            return;
        }

        var state = _services.Player.State;
        var song = SleepSongKey(state);
        if (song != _skipShownSong)
        {
            _skipShownSong = song;
            _services.BuiltIns.NotifyMenus();
        }

        if (state.IsPlaying && song is not null && song != _skipCheckedSong)
        {
            _skipCheckedSong = song;
            CheckSkip(state);
        }
    }

    private void CheckSkip(PlayerState state)
    {
        var settings = _services.Settings;
        var rules = new SkipFilterRules(settings.SkipArtists, settings.SkipVersions, settings.SkipSongs, settings.SkipLongerThanMinutes, settings.SkipShorterThanSeconds);
        if (!state.IsPlaying || SkipFilter.ReasonToSkip(state.Title, state.Artists, state.Duration, rules) is not { } reason)
        {
            return;
        }

        switch (_skipGuard.Matched(DateTimeOffset.UtcNow))
        {
            case SkipVerdict.Skip:
                _skippedCount++;
                _skipStatus = $"Skipped {_skippedCount} {(_skippedCount == 1 ? "song" : "songs")}, last: {state.Title} ({reason})";
                _services.BuiltIns.NotifyMenus();
                _ = _services.Player.NextAsync();
                break;
            case SkipVerdict.StartRest:
                _services.BuiltIns.NotifyMenus();
                ShowMessage("Skip rules matched every song in a row, so they rest for a minute.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
                break;
        }
    }

    private IReadOnlyList<PluginCommand> SkipCommands()
    {
        var state = _services.Player.State;
        if (string.IsNullOrWhiteSpace(state.Title))
        {
            return [];
        }

        var title = state.Title;
        var artists = state.Artists;
        List<PluginCommand> commands =
        [
            new("Always skip this song", () => AddSkipRule(_services.Settings.SkipSongs, SkipFilter.SongLine(title, artists))),
        ];
        foreach (var name in SkipFilter.ArtistNames(artists).Take(MaxArtistCommands))
        {
            commands.Add(new($"Always skip {name}", () => AddSkipRule(_services.Settings.SkipArtists, name)));
        }

        return commands;
    }

    private string? SkipStatus() =>
        _skipGuard.IsResting(DateTimeOffset.UtcNow) ? "Resting for a minute: every song matched" : _skipStatus;

    /// <summary>Saves a new rule and checks the song playing against it at once.</summary>
    private void AddSkipRule(List<string> list, string value)
    {
        if (SkipFilter.AddTo(list, value))
        {
            _services.SaveSettings();
        }

        CheckSkip(_services.Player.State);
    }
}

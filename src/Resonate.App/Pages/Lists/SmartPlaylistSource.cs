using Microsoft.UI.Xaml;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages.Lists;

/// <summary>
/// A smart playlist ("smart:&lt;id&gt;"): its songs are worked out on this
/// computer from Liked Songs (or one of the user's playlists) by its rules,
/// and play as Resonate's own list. Its page shows the rules as a sentence
/// of chips above the songs (<see cref="SmartPlaylistPanel"/>); every change
/// shows at once, from the songs loaded on the first visit.
/// </summary>
public sealed class SmartPlaylistSource : TrackListSource
{
    public const string Prefix = "smart:";

    /// <summary>A spark, for smart playlists in the sidebar and on their page.</summary>
    public const string Glyph = "";

    private readonly SmartPlaylistService? _smart;
    private readonly string _id;

    /// <summary>A copy of the definition for the background loads, replaced on every change.</summary>
    private SmartPlaylist? _current;

    /// <summary>The songs the rules look at, loaded once per visit (each change only works the songs out again).</summary>
    private LoadedInputs? _inputs;
    private SmartPlaylistPanel? _panel;
    private Action<bool>? _changed;

    public SmartPlaylistSource(string id)
    {
        _id = id;
        _smart = App.MainWindow?.SmartPlaylists;
        _current = Definition()?.Clone();
    }

    public override string Key => Prefix + _id;

    public override string OwnOrderName => "Smart order";

    public override bool ShowsOwnTotals => _panel is not null;

    public override string EmptyText => _current is null
        ? _smart?.IsOn == true ? "This smart playlist was deleted." : "Smart playlists are off. Turn them on in Settings, Plugins."
        : "No songs match these rules yet.";

    public override ListHeader CachedHeader =>
        new("SMART PLAYLIST", _current?.Name ?? "Smart playlist", null, null, null, _id, Glyph);

    /// <summary>The sentence of chips, the totals and "Keep on Spotify".</summary>
    public override FrameworkElement? CreatePanel()
    {
        if (_smart is null || Definition() is null)
        {
            return null;
        }

        _panel = new SmartPlaylistPanel(_smart, _id, this);
        return _panel.Root;
    }

    public override void Attach(Action<bool> changed)
    {
        _changed = changed;
        if (_smart is not null)
        {
            _smart.PlaylistChanged += OnPlaylistChanged;
        }
    }

    public override void Detach()
    {
        _changed = null;
        if (_smart is not null)
        {
            _smart.PlaylistChanged -= OnPlaylistChanged;
        }

        _panel?.Detach();
    }

    public override async Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _current) is not { } playlist || _smart is null)
        {
            return new FullTrackList([], ItemsHidden: false);
        }

        var key = InputsKey(playlist);
        if (Volatile.Read(ref _inputs) is not { } loaded || loaded.Key != key)
        {
            loaded = new LoadedInputs(key, await _smart.Sync.LoadInputsAsync(playlist, cancellationToken).ConfigureAwait(false));
            Volatile.Write(ref _inputs, loaded);
        }

        var tracks = _smart.Sync.Evaluate(playlist, loaded.Inputs);
        _panel?.ShowTotals(tracks);
        return new FullTrackList(tracks, ItemsHidden: false);
    }

    /// <summary>The artists of the songs the rules look at, for the artist chip's suggestions.</summary>
    public IEnumerable<string> KnownArtists() =>
        Volatile.Read(ref _inputs)?.Inputs.Source.SelectMany(t => t.ArtistRefs.Select(a => a.Name)).Distinct(StringComparer.OrdinalIgnoreCase)
        ?? [];

    private SmartPlaylist? Definition() => _smart is { IsOn: true } smart ? smart.Find(_id) : null;

    /// <summary>On the interface thread.</summary>
    private void OnPlaylistChanged(object? sender, SmartPlaylistChange change)
    {
        if (change.Id != _id)
        {
            return;
        }

        Volatile.Write(ref _current, Definition()?.Clone());
        _panel?.ShowState();

        // A change of rules lists the songs again; a change of how it is kept on Spotify only the header.
        _changed?.Invoke(change.RulesChanged);
    }

    private static string InputsKey(SmartPlaylist playlist) =>
        string.Join(
            '|',
            playlist.SourcePlaylistId ?? "liked",
            SmartPlaylistEvaluator.NeedsReleaseYears(playlist) ? "years" : string.Empty,
            string.Join(',', playlist.Rules.Where(r => r.Kind == SmartRuleKind.NotInPlaylist).Select(r => r.Text).Order(StringComparer.Ordinal)));

    private sealed record LoadedInputs(string Key, SmartInputs Inputs);
}

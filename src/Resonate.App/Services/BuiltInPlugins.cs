namespace Resonate.App.Services;

/// <summary>A plugin built into Resonate, listed in Settings, Plugins.</summary>
public sealed record BuiltInPlugin(string Id, string Name, string Description);

/// <summary>
/// Features built into Resonate that stay off until they are turned on in
/// Settings, Plugins, like the downloaded plugins (the owner asked for them
/// as optional plugins, 8 October 2026). They need Resonate's own windows,
/// pages and the Spotify Web API, which the JavaScript helper cannot reach,
/// so they are compiled in; while one is off, nothing of it runs or shows.
/// Each one watches <see cref="Changed"/> to start and stop at once.
/// </summary>
public sealed class BuiltInPlugins
{
    public const string Lyrics = "lyrics";
    public const string HomeStage = "home-stage";
    public const string AwayScreen = "away-screen";
    public const string Rediscover = "rediscover";
    public const string UpNext = "up-next";
    public const string ArtistOrbit = "artist-orbit";
    public const string SmartPlaylists = "smart-playlists";
    public const string WindowShapes = "window-shapes";
    public const string SummonBar = "summon-bar";
    public const string SignalPath = "signal-path";
    public const string PauseOnLock = "pause-on-lock";
    public const string PauseOnUnplug = "pause-on-unplug";

    private readonly AppSettings _settings;
    private readonly Action _save;

    public BuiltInPlugins(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    /// <summary>Raised on the interface thread with a plugin's ID after it was turned on or off.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Every built-in plugin, in the order Settings lists them.</summary>
    public static IReadOnlyList<BuiltInPlugin> All { get; } =
    [
        new(AwayScreen, "Away screen", "After a few idle minutes, the window shows the song and a clock."),
        new(Rediscover, "Rediscover", "Songs you liked on this day, and ones you have not played in a while, on Home."),
        new(UpNext, "Up next", "Reorder, remove and clear the songs coming up."),
        new(ArtistOrbit, "Artist orbit", "Artist pages show the artists you play alongside them."),
        new(SmartPlaylists, "Smart playlists", "Playlists that fill themselves from your Liked Songs by rules."),
        new(WindowShapes, "Window shapes", "Shrink the window to a compact player or a strip that stays on top."),
        new(SummonBar, "Summon bar", "A shortcut opens a search box over any app."),
        new(SignalPath, "Signal path", "A badge in the player that says whether you hear lossless."),
        new(PauseOnLock, "Pause on lock", "Pauses when you lock your PC and plays on when you unlock it."),
        new(PauseOnUnplug, "Pause on unplug", "Pauses when headphones or a speaker disconnect."),
    ];

    /// <summary>Part of Resonate itself now (the owner's choice, 8 October 2026): always on, not listed.</summary>
    public static bool IsAlwaysOn(string id) => id is Lyrics or HomeStage;

    public bool IsOn(string id) => IsAlwaysOn(id) || _settings.BuiltInPlugins.Contains(id, StringComparer.Ordinal);

    /// <summary>Turns a plugin on or off and saves it. Call on the interface thread.</summary>
    public void Set(string id, bool on)
    {
        if (IsOn(id) == on)
        {
            return;
        }

        if (on)
        {
            _settings.BuiltInPlugins.Add(id);
        }
        else
        {
            _settings.BuiltInPlugins.RemoveAll(i => string.Equals(i, id, StringComparison.Ordinal));
        }

        _save();
        Changed?.Invoke(this, id);
    }
}

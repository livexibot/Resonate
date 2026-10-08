using System.Net;
using Resonate.App.Demo;
using Resonate.App.Themes;
using Resonate.Plugins;
using Resonate.Plugins.Installing;
using Resonate.Spotify.Auth;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Windows;
using Resonate.Windows.LocalAudio;

namespace Resonate.App.Services;

/// <summary>Everything the windows and pages use, created once at start-up.</summary>
public sealed class AppServices : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    private AppServices(
        bool isDemo,
        SettingsStore settingsStore,
        AppSettings settings,
        AccountService account,
        ISpotifyWebApi api,
        LibraryService library,
        HomeFeed home,
        PlayerRouter player,
        ISpotifyAppLauncher launcher,
        ISpotifyAppWindow spotifyWindow,
        HttpClient http,
        CoverStore? covers,
        LocalFilesService localFiles,
        PluginManager plugins)
    {
        IsDemo = isDemo;
        SettingsStore = settingsStore;
        Settings = settings;
        Account = account;
        Api = api;
        Library = library;
        Home = home;
        Likes = new LikedSongs(api, library);
        Player = player;
        Launcher = launcher;
        SpotifyWindow = spotifyWindow;
        Plugins = plugins;
        _owned.Add(plugins);
        Theme = new ThemeService(settings, SaveSettings);
        Covers = new CoverImages(covers);
        if (covers is not null)
        {
            // Before the HTTP client: downloads stop first.
            _owned.Add(covers);
        }

        Artwork = new ArtworkSampler(player, Theme, http, covers);
        _owned.Add(Artwork);

        player.Spotify.Channel = settings.ParsedControlChannel;
        spotifyWindow.KeepHidden = settings.KeepSpotifyHidden;
        spotifyWindow.SaveResources = settings.SaveSpotifyResources;
        LocalFiles = localFiles;
        ConnectLocalPlayer();

        // Demo mode has no Spotify settings to find, and nothing to restart.
        Equalizer = new EqualizerService(this, isDemo ? [] : SpotifyAppLauncher.SettingsFolders(), launcher as ISpotifyAppRestarter);
        _owned.Add(Equalizer);
    }

    public bool IsDemo { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public AccountService Account { get; }

    public ISpotifyWebApi Api { get; }

    public LibraryService Library { get; }

    /// <summary>The listening history, stats and daily mixes on Home.</summary>
    public HomeFeed Home { get; }

    /// <summary>Which songs are in Liked Songs, for every heart in the interface.</summary>
    public LikedSongs Likes { get; }

    /// <summary>The player the interface controls: the Spotify app, or the local files player for Local Files.</summary>
    public PlayerRouter Player { get; }

    public ISpotifyAppLauncher Launcher { get; }

    /// <summary>The Spotify app's window: hidden in the background, shown on request.</summary>
    public ISpotifyAppWindow SpotifyWindow { get; }

    /// <summary>The equalizer: the Spotify app's own for Spotify's songs, and the same setting for local files.</summary>
    public EqualizerService Equalizer { get; }

    /// <summary>The user's own music files (Local Files) and the folders they come from.</summary>
    public LocalFilesService LocalFiles { get; }

    /// <summary>Optional plugins, downloaded only when turned on in Settings.</summary>
    public PluginManager Plugins { get; }

    /// <summary>The look: presets, the user's own looks, and switching between them.</summary>
    public ThemeService Theme { get; }

    /// <summary>The playing song's cover, read for looks that use its colours.</summary>
    public ArtworkSampler Artwork { get; }

    /// <summary>Every cover the interface shows, kept on disk and in memory so lists fill in at once.</summary>
    public CoverImages Covers { get; }

    public UpdateService Updates { get; } = new();

    /// <summary>The real thing: Spotify's media session, the Web API, the Credential Manager.</summary>
    public static AppServices Create()
    {
        var settingsStore = new SettingsStore(AppPaths.SettingsFile);
        var settings = settingsStore.Load();

        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),

            // A connection stays ready between clicks, so the next page or cover does not wait for a new one.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            // Connect quickly or not at all; the interface never waits on this.
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),

            // Requests to the same server share one connection (HTTP/2) where the server allows it.
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Resonate/" + AppInfo.Version);

        var account = new AccountService(http, new CredentialTokenStore(), settings.ClientId);
        var api = new SpotifyWebApi(http, account);
        var library = new LibraryService(
            api,
            new LibraryCache(Path.Combine(AppPaths.CacheFolder, "library.json")),
            lists: new TrackListStore(Path.Combine(AppPaths.CacheFolder, "lists")));
        var home = new HomeFeed(
            api,
            library,
            new ListeningHistory(api, Path.Combine(AppPaths.CacheFolder, "history.json")),
            Path.Combine(AppPaths.CacheFolder, "home.json"));
        var smtc = new SmtcMediaChannel();
        var background = new SpotifyBackground();
        var launcher = new SpotifyAppLauncher(background);
        var spotify = new PlayerController(
            smtc,
            new SpotifyMixerVolume(),
            api,
            new LocalDeviceResolver(api, Environment.MachineName),
            launcher);
        var localControls = new LocalMediaControls();
        var local = new LocalPlayer(new AudioGraphEngine(), localControls);
        var player = new PlayerRouter(spotify, local);
        var localFiles = new LocalFilesService(
            settings,
            () => settingsStore.Save(settings),
            new LocalLibrary(Path.Combine(AppPaths.CacheFolder, "local-files.json")),
            new LocalCoverCache(Path.Combine(AppPaths.CacheFolder, "local-covers"), new WindowsCoverShrinker()),
            localControls);

        // Plugins download from this release's own files on GitHub, checked against the catalog built into the app.
        var catalog = LoadPluginCatalog();
        var pluginPlayer = new PluginPlayer(player);
        var plugins = new PluginManager(
            catalog,
            catalog.Source is { } source ? new PluginInstaller(AppPaths.PluginsFolder, new HttpPluginFeed(http, new Uri(source))) : null,
            new PluginStateStore(AppPaths.PluginsFile),
            new ProcessPluginHostLauncher(),
            pluginPlayer);

        var covers = new CoverStore(http, Path.Combine(AppPaths.CacheFolder, "covers"));
        var services = new AppServices(false, settingsStore, settings, account, api, library, home, player, launcher, background, http, covers, localFiles, plugins);
        services._owned.AddRange([pluginPlayer, player, spotify, local, localFiles, home, library, smtc, launcher, background, account, http]);

        // At once, so a Spotify already on the taskbar (started with Windows) disappears from it.
        background.Start();
        return services;
    }

    /// <summary>Made-up music and a pretend player, for trying the look and for CI screenshots.</summary>
    public static AppServices CreateDemo()
    {
        var settingsStore = new SettingsStore(Path.Combine(Path.GetTempPath(), "resonate-demo-settings.json"));
        var settings = new AppSettings { ClientId = "demo" };
        var demoPlayer = new DemoPlayer();
        var api = new DemoWebApi(demoPlayer);
        var http = new HttpClient();
        var account = new AccountService(http, new InMemoryTokenStore(), clientId: null, alwaysSignedIn: true);
        var library = new LibraryService(api, cache: null);
        var home = new HomeFeed(api, library, new ListeningHistory(api, path: null), path: null);
        var spotify = new PlayerController(demoPlayer, demoPlayer, api, new LocalDeviceResolver(api, Environment.MachineName), demoPlayer);
        var local = new LocalPlayer(new DemoLocalAudio(), readCover: _ => null);
        var player = new PlayerRouter(spotify, local);
        var localFiles = new LocalFilesService(
            settings,
            saveSettings: () => { },
            new LocalLibrary(indexFile: null),
            covers: null,
            controls: null,
            DemoLocalFiles.Tracks(DateTimeOffset.UtcNow));

        // A preview: plugins can be turned on to see their settings, but nothing downloads or runs.
        var pluginPlayer = new PluginPlayer(player);
        var plugins = new PluginManager(LoadPluginCatalog(), installer: null, new PluginStateStore(null), launcher: null, pluginPlayer);

        var services = new AppServices(true, settingsStore, settings, account, api, library, home, player, demoPlayer, demoPlayer, http, covers: null, localFiles, plugins);
        services._owned.AddRange([pluginPlayer, player, spotify, local, localFiles, home, library, account, http]);
        return services;
    }

    /// <summary>
    /// Windows' media controls follow whichever player has the music, and the
    /// local files player keeps its own volume between launches.
    /// </summary>
    private void ConnectLocalPlayer()
    {
        if (Player.Local is not LocalPlayer local)
        {
            return;
        }

        _ = local.SetVolumeAsync(Settings.LocalVolume);
        Player.StateChanged += (_, _) =>
        {
            var active = Player.ActiveSource == PlaybackSource.LocalFiles;
            local.SystemControlsEnabled = active;
            if (active)
            {
                Settings.LocalVolume = local.State.Volume;
            }
        };
    }

    /// <summary>
    /// The plugins this build offers, embedded when it was built (CI and
    /// releases pass -p:PluginCatalog; a local build has none).
    /// </summary>
    public static PluginCatalog LoadPluginCatalog()
    {
        using var stream = typeof(AppServices).Assembly.GetManifestResourceStream("plugin-catalog.json");
        return PluginCatalog.Load(stream);
    }

    public void SaveSettings()
    {
        if (!IsDemo)
        {
            SettingsStore.Save(Settings);
        }
    }

    public void Dispose()
    {
        foreach (var item in _owned)
        {
            item.Dispose();
        }

        SettingsStore.Flush();
    }
}

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}

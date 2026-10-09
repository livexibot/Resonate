using System.Net;
using Microsoft.UI.Dispatching;
using Resonate.App.Demo;
using Resonate.App.Themes;
using Resonate.Plugins;
using Resonate.Plugins.Installing;
using Resonate.Spotify.Audio;
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
    private bool _ownPlayerAllowed;
    private bool _ownPlayerHeld;

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
        PluginManager plugins,
        OwnPlayer? ownPlayer = null,
        IAppSoundCapture? soundCapture = null)
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
        BuiltIns = new BuiltInPlugins(settings, SaveSettings);
        Covers = new CoverImages(covers);
        if (covers is not null)
        {
            // Before the HTTP client: downloads stop first.
            _owned.Add(covers);
        }

        Artwork = new ArtworkSampler(player, Theme, http, covers);
        _owned.Add(Artwork);

        // Song stats ask ReccoBeats only while switched on; the demo makes them up.
        SongStats = new SongStatsService(
            isDemo ? null : new ReccoBeatsClient(http),
            new SongStatsCache(isDemo ? null : Path.Combine(AppPaths.CacheFolder, "song-stats.json")),
            settings);

        // Demo mode (CI's screenshots) never touches the user's own skins.
        Skins = new SkinLibrary(settings, isDemo ? Path.Combine(Path.GetTempPath(), "Resonate demo skins") : AppPaths.SkinsFolder);

        // Real or demo, the local files engine feeds it: AudioGraph with the user's files, or DemoLocalAudio's made-up sound.
        // Only a real run hears Spotify's sound for the Home stage; demo mode (CI) never captures anything.
        Visualiser = new VisualiserFeed(player, soundCapture, FindSpotifySound) { ListensToSpotify = settings.HomeStageListens };
        _owned.Add(Visualiser);

        player.Spotify.Channel = settings.ParsedControlChannel;
        player.Spotify.PreferredDeviceName = settings.WebApiDeviceName;
        spotifyWindow.Enabled = UsesSpotifyApp;
        SpotifyApp = new SpotifyAppKeeper(launcher, spotifyWindow, () => player.Spotify.Channel);
        _owned.Add(SpotifyApp);
        spotifyWindow.KeepHidden = settings.KeepSpotifyHidden;
        spotifyWindow.SaveResources = settings.SaveSpotifyResources;
        LocalFiles = localFiles;
        ConnectLocalPlayer();

        OwnPlayer = ownPlayer;
        if (ownPlayer is not null)
        {
            // Closed before the sign-in it asks for tokens (MainWindow.Quit lets it say goodbye before that).
            _owned.Add(ownPlayer);

            // Its song or play state changed: the interface asks Spotify at once.
            ownPlayer.PlaybackChanged += (_, _) => player.Spotify.RefreshSoon();

            // What it plays, straight from it: shown at once, without asking Spotify.
            ownPlayer.StateReported += (_, state) => player.Spotify.ApplyOwnPlayerState(state);

            // Started or stopped: the Home stage's visualizer hears its page, or the Spotify app again.
            ownPlayer.StatusChanged += (_, _) => Visualiser.LookAgain();
        }

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

    /// <summary>Starts the Spotify app hidden, or closes it with "Spotify Web API only".</summary>
    public SpotifyAppKeeper SpotifyApp { get; }

    /// <summary>
    /// Whether Resonate works with the Spotify app on this computer (Windows'
    /// media controls, the default). False with "Spotify Web API only": then
    /// Resonate closes the Spotify app (see <see cref="SpotifyApp"/>) and
    /// otherwise never starts, hides, reads or restarts it.
    /// </summary>
    public bool UsesSpotifyApp => Player.Spotify.Channel == ControlChannel.Local;

    /// <summary>
    /// Resonate's own player for "Spotify Web API only": Spotify's web player
    /// (the Web Playback SDK) in a page nobody sees, so music plays on this
    /// PC with the Spotify app closed, at the web player's quality. It runs
    /// only in that mode, with "Play on this PC" on and someone signed in
    /// (<see cref="FollowOwnPlayer"/>). Null in demo mode.
    /// </summary>
    public OwnPlayer? OwnPlayer { get; }

    /// <summary>Raised on the interface thread after <see cref="SetControlChannel"/> switched.</summary>
    public event EventHandler? ControlChannelChanged;

    /// <summary>The equalizer: the Spotify app's own for Spotify's songs, and the same setting for local files.</summary>
    public EqualizerService Equalizer { get; }

    /// <summary>The user's own music files (Local Files) and the folders they come from.</summary>
    public LocalFilesService LocalFiles { get; }

    /// <summary>BPM, key, loudness and energy for song lists (Settings, Layout, Song lists).</summary>
    public SongStatsService SongStats { get; }

    /// <summary>Optional plugins, downloaded only when turned on in Settings.</summary>
    public PluginManager Plugins { get; }

    /// <summary>The plugins built into Resonate (lyrics, Home stage and the rest), off until turned on.</summary>
    public BuiltInPlugins BuiltIns { get; }

    /// <summary>The look: presets, the user's own looks, and switching between them.</summary>
    public ThemeService Theme { get; }

    /// <summary>The playing song's cover, read for looks that use its colours.</summary>
    public ArtworkSampler Artwork { get; }

    /// <summary>Every cover the interface shows, kept on disk and in memory so lists fill in at once.</summary>
    public CoverImages Covers { get; }

    /// <summary>The classic player's skins: the built-in one and the ones the user added.</summary>
    public SkinLibrary Skins { get; }

    /// <summary>What the classic player's visualiser shows; only local files feed it.</summary>
    public VisualiserFeed Visualiser { get; }

    public UpdateService Updates { get; } = new();

    /// <summary>The real thing: Spotify's media session, the Web API, the Credential Manager.</summary>
    public static AppServices Create()
    {
        var settingsStore = new SettingsStore(AppPaths.SettingsFile);
        var settings = settingsStore.Load();
        PlaybackLogFile.Start(AppPaths.CacheFolder);

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

        var account = new AccountService(http, new CredentialTokenStore(AppPaths.CredentialTarget), settings.ClientId);
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

        // "Spotify Web API only" plays on this PC through Spotify's web player, hidden (see OwnPlayer).
        var interfaceThread = DispatcherQueue.GetForCurrentThread();
        var ownPlayer = new OwnPlayer(
            () => new WebPlayerPage(interfaceThread, AppPaths.WebPlayerFolder) { Trace = step => PlaybackLog.Note($"web player: {step}") },
            account,
            canPlay: () => !account.MissingScopes.Contains(SpotifyAuthOptions.StreamingScope));
        var spotify = new PlayerController(
            smtc,
            new SpotifyMixerVolume(),
            api,
            new LocalDeviceResolver(api, Environment.MachineName),
            launcher,
            webDevices: new WebDeviceResolver(api, Environment.MachineName, ownPlayer),
            direct: ownPlayer);
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
        var soundCapture = new AppSoundCapture();
        var services = new AppServices(false, settingsStore, settings, account, api, library, home, player, launcher, background, http, covers, localFiles, plugins, ownPlayer, soundCapture);
        services._owned.AddRange([pluginPlayer, player, spotify, local, localFiles, home, library, smtc, launcher, background, account, http, soundCapture]);

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

    /// <summary>
    /// Switches how Resonate talks to Spotify, at once and without a
    /// restart: the player starts or stops listening to Spotify's media
    /// session, and the equalizer follows. The Spotify app is started or
    /// closed by whoever handles <see cref="ControlChannelChanged"/>
    /// (through <see cref="SpotifyApp"/>, so it can say when that failed).
    /// </summary>
    public void SetControlChannel(ControlChannel channel)
    {
        if (Player.Spotify.Channel == channel)
        {
            return;
        }

        Settings.ControlChannel = channel == ControlChannel.WebApi ? "webapi" : "local";
        SaveSettings();
        Player.Spotify.Channel = channel;
        Equalizer.OnChannelChanged();
        FollowOwnPlayer();
        Visualiser.LookAgain();
        ControlChannelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Lets Resonate's own player run from now on (once the window is up and
    /// the player follows Spotify; never in demo or check runs), and starts it
    /// if it should run.
    /// </summary>
    public void AllowOwnPlayer()
    {
        _ownPlayerAllowed = true;

        // Resonate ended while the page was open last time, maybe because of
        // it: its browser gets a moment to go first, and after three such
        // runs in a row it waits for "Play on this PC" to be switched on again.
        var uncleanEnds = WebPlayerPage.UncleanEnds(AppPaths.WebPlayerFolder);
        if (uncleanEnds > 0 && OwnPlayer is not null)
        {
            PlaybackLog.Note($"own player: Resonate ended {uncleanEnds} time(s) in a row while it was open");
        }

        if (uncleanEnds >= MostUncleanEnds)
        {
            _ownPlayerHeld = true;
        }
        else if (uncleanEnds > 0)
        {
            _ = FollowOwnPlayerLaterAsync();
            return;
        }

        FollowOwnPlayer();
    }

    /// <summary>Runs of Resonate in a row that may end while its own player's page is open before it no longer starts by itself.</summary>
    public const int MostUncleanEnds = 3;

    /// <summary>
    /// Whether Resonate's own player waits for "Play on this PC" to be
    /// switched on again, because Resonate ended while it was open
    /// <see cref="MostUncleanEnds"/> runs in a row.
    /// </summary>
    public bool OwnPlayerHeld => _ownPlayerHeld;

    private async Task FollowOwnPlayerLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        FollowOwnPlayer();
    }

    /// <summary>
    /// Starts Resonate's own player when it should run ("Spotify Web API
    /// only", "Play on this PC" and signed in), else stops it. Call it after
    /// any of those changed.
    /// </summary>
    public void FollowOwnPlayer()
    {
        if (OwnPlayer is not { } own)
        {
            return;
        }

        var run = _ownPlayerAllowed
            && !_ownPlayerHeld
            && Player.Spotify.Channel == ControlChannel.WebApi
            && Settings.WebApiPlayHere
            && Account.IsSignedIn;
        _ = run ? own.StartAsync() : own.StopAsync();
    }

    /// <summary>
    /// The process that plays Spotify's sound on this PC, for the Home stage's
    /// visualizer: the Spotify app, or with "Spotify Web API only" Resonate's
    /// own player (its hidden WebView2) unless Spotify says another device
    /// plays, then a Spotify app the user opened again. Null when neither
    /// runs. Called off the interface thread.
    /// </summary>
    private int? FindSpotifySound()
    {
        var page = WebPlayerPage.BrowserProcessId;
        if (UsesSpotifyApp || page <= 0)
        {
            return AppSoundCapture.FindSpotify();
        }

        var device = Player.Spotify.State.DeviceName;
        if (device is null || string.Equals(device, OwnPlayer?.Name ?? OwnPlayer.DefaultName, StringComparison.Ordinal))
        {
            return page;
        }

        return AppSoundCapture.FindSpotify() ?? page;
    }

    /// <summary>"Play on this PC" with "Spotify Web API only": Resonate's own player on or off.</summary>
    public void SetWebApiPlayHere(bool on)
    {
        Settings.WebApiPlayHere = on;
        SaveSettings();
        if (on && _ownPlayerHeld)
        {
            // Asked for again: it starts as if nothing had happened.
            _ownPlayerHeld = false;
            WebPlayerPage.ForgetUncleanEnds(AppPaths.WebPlayerFolder);
        }

        FollowOwnPlayer();
    }

    /// <summary>A sentence about Resonate's own player for Settings, or null when there is nothing to say.</summary>
    public static string? DescribeOwnPlayer(OwnPlayerStatus status) => status switch
    {
        OwnPlayerStatus.Starting => "Connecting to Spotify…",
        OwnPlayerStatus.Ready => $"Ready. Spotify lists this PC as “{OwnPlayer.DefaultName}”.",
        OwnPlayerStatus.NeedsSignIn => "Sign in again to play on this PC: Sign out below, then sign in. Still not working? Tick Web Playback SDK in your Spotify developer app.",
        OwnPlayerStatus.NeedsPremium => "Spotify only plays on this PC with Premium.",
        OwnPlayerStatus.Unsupported => "This PC cannot run Spotify's web player. It needs Microsoft's WebView2 Runtime (and on Windows N the Media Feature Pack), or use Windows media controls.",
        OwnPlayerStatus.Failed => "Spotify's web player stopped (offline?). Trying again…",
        _ => null,
    };

    /// <summary>Moves the music to another Spotify Connect device ("Spotify Web API only") and remembers it.</summary>
    public Task PlayOnDeviceAsync(string deviceId, string deviceName)
    {
        Settings.WebApiDeviceName = deviceName;
        SaveSettings();
        return Player.Spotify.TransferToAsync(deviceId, deviceName);
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

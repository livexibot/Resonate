using System.Net;
using Resonate.App.Demo;
using Resonate.App.Themes;
using Resonate.Spotify.Auth;
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
        PlayerRouter player,
        ISpotifyAppLauncher launcher,
        ISpotifyAppWindow spotifyWindow,
        LocalFilesService localFiles)
    {
        IsDemo = isDemo;
        SettingsStore = settingsStore;
        Settings = settings;
        Account = account;
        Api = api;
        Library = library;
        Likes = new LikedSongs(api, library);
        Player = player;
        Launcher = launcher;
        SpotifyWindow = spotifyWindow;

        player.Spotify.Channel = settings.ParsedControlChannel;
        spotifyWindow.KeepHidden = settings.KeepSpotifyHidden;
        spotifyWindow.SaveResources = settings.SaveSpotifyResources;
        LocalFiles = localFiles;
        ConnectLocalPlayer();
    }

    public bool IsDemo { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public AccountService Account { get; }

    public ISpotifyWebApi Api { get; }

    public LibraryService Library { get; }

    /// <summary>Which songs are in Liked Songs, for every heart in the interface.</summary>
    public LikedSongs Likes { get; }

    /// <summary>The player the interface controls: the Spotify app, or the local files player for Local Files.</summary>
    public PlayerRouter Player { get; }

    public ISpotifyAppLauncher Launcher { get; }

    /// <summary>The Spotify app's window: hidden in the background, shown on request.</summary>
    public ISpotifyAppWindow SpotifyWindow { get; }

    /// <summary>The user's own music files (Local Files) and the folders they come from.</summary>
    public LocalFilesService LocalFiles { get; }

    public ThemeService Theme { get; } = new();

    public UpdateService Updates { get; } = new();

    /// <summary>The real thing: Spotify's media session, the Web API, the Credential Manager.</summary>
    public static AppServices Create()
    {
        var settingsStore = new SettingsStore(AppPaths.SettingsFile);
        var settings = settingsStore.Load();

        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
            // Connect quickly or not at all; the interface never waits on this.
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Resonate/" + AppInfo.Version);

        var account = new AccountService(http, new CredentialTokenStore(), settings.ClientId);
        var api = new SpotifyWebApi(http, account);
        var library = new LibraryService(
            api,
            new LibraryCache(Path.Combine(AppPaths.CacheFolder, "library.json")),
            lists: new TrackListStore(Path.Combine(AppPaths.CacheFolder, "lists")));
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
            new LocalCoverCache(Path.Combine(AppPaths.CacheFolder, "local-covers")),
            localControls);

        var services = new AppServices(false, settingsStore, settings, account, api, library, player, launcher, background, localFiles);
        services._owned.AddRange([player, spotify, local, localFiles, library, smtc, launcher, background, account, http]);

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

        var services = new AppServices(true, settingsStore, settings, account, api, library, player, demoPlayer, demoPlayer, localFiles);
        services._owned.AddRange([player, spotify, local, localFiles, library, account, http]);
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
    }
}

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}

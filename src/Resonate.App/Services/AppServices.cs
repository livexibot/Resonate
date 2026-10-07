using System.Net;
using Resonate.App.Demo;
using Resonate.App.Themes;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Windows;

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
        PlayerController player,
        ISpotifyAppLauncher launcher,
        ISpotifyAppWindow spotifyWindow,
        HttpClient http)
    {
        IsDemo = isDemo;
        SettingsStore = settingsStore;
        Settings = settings;
        Account = account;
        Api = api;
        Library = library;
        Player = player;
        Launcher = launcher;
        SpotifyWindow = spotifyWindow;
        Theme = new ThemeService(settings, SaveSettings);
        Artwork = new ArtworkSampler(player, Theme, http);
        _owned.Add(Artwork);

        player.Channel = settings.ParsedControlChannel;
        spotifyWindow.KeepHidden = settings.KeepSpotifyHidden;
        spotifyWindow.SaveResources = settings.SaveSpotifyResources;
    }

    public bool IsDemo { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public AccountService Account { get; }

    public ISpotifyWebApi Api { get; }

    public LibraryService Library { get; }

    public PlayerController Player { get; }

    public ISpotifyAppLauncher Launcher { get; }

    /// <summary>The Spotify app's window: hidden in the background, shown on request.</summary>
    public ISpotifyAppWindow SpotifyWindow { get; }

    /// <summary>The look: presets, the user's own looks, and switching between them.</summary>
    public ThemeService Theme { get; }

    /// <summary>The playing song's cover, read for looks that use its colours.</summary>
    public ArtworkSampler Artwork { get; }

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
        var library = new LibraryService(api, new LibraryCache(Path.Combine(AppPaths.CacheFolder, "library.json")));
        var smtc = new SmtcMediaChannel();
        var background = new SpotifyBackground();
        var launcher = new SpotifyAppLauncher(background);
        var player = new PlayerController(
            smtc,
            new SpotifyMixerVolume(),
            api,
            new LocalDeviceResolver(api, Environment.MachineName),
            launcher);

        var services = new AppServices(false, settingsStore, settings, account, api, library, player, launcher, background, http);
        services._owned.AddRange([player, smtc, launcher, background, account, http]);

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
        var player = new PlayerController(demoPlayer, demoPlayer, api, new LocalDeviceResolver(api, Environment.MachineName), demoPlayer);

        var services = new AppServices(true, settingsStore, settings, account, api, library, player, demoPlayer, demoPlayer, http);
        services._owned.AddRange([player, account, http]);
        return services;
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

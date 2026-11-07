using System.Net;
using Microsoft.UI.Dispatching;
using Resonate.App.Demo;
using Resonate.Spotify.Library;
using Resonate.Spotify.Lyrics;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>
/// Line Finder, a built-in plugin (Settings, Plugins): type a half-remembered
/// line in Search, under Lyrics, and the song starts at that line. LRCLIB's
/// own search only matches titles and artists, so while the plugin is on
/// Resonate gathers the words of the songs it already knows (what was
/// played, Liked Songs, and the playlists it keeps on this PC) from LRCLIB,
/// one at a time in the background, into an index on this PC
/// (<see cref="LineIndex"/>). That costs no request to Spotify; playing a
/// line found is one. While the plugin is off nothing is looked up and the
/// index is not in memory. The demo makes up its words and keeps nothing.
/// </summary>
public sealed class LineFinderService : IDisposable
{
    /// <summary>At launch the index waits this long, so it never slows the start.</summary>
    private static readonly TimeSpan LaunchDelay = TimeSpan.FromSeconds(30);

    private readonly AppServices _services;
    private readonly DispatcherQueue _dispatcher;
    private readonly LineIndex _index;
    private CancellationTokenSource? _running;
    private Task _run = Task.CompletedTask;
    private LineIndexBuilder? _builder;
    private HttpClient? _http;
    private int _progressQueued;

    /// <summary>Call on the interface thread.</summary>
    public LineFinderService(AppServices services)
    {
        _services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _index = new LineIndex(services.IsDemo ? null : IndexFile);
    }

    /// <summary>The index on this PC (in the cache folder, beside the lyrics the lyrics pane keeps).</summary>
    public static string IndexFile => Path.Combine(AppPaths.CacheFolder, "line-finder.jsonl");

    public bool IsOn => _services.BuiltIns.IsOn(BuiltInPlugins.LineFinder);

    /// <summary>Raised on the interface thread when <see cref="Progress"/> changed.</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>How far the index is, as last told on the interface thread; nothing while the plugin is off.</summary>
    public LineIndexProgress Progress { get; private set; }

    /// <summary>
    /// Starts filling the index in the background (the plugin was turned on).
    /// At launch it waits a while first. Call on the interface thread.
    /// </summary>
    public void Start(bool atLaunch)
    {
        if (_running is not null)
        {
            return;
        }

        var running = new CancellationTokenSource();
        _running = running;
        var builder = new LineIndexBuilder(_index, Source(), _services.IsDemo ? null : new LyricsCache(Path.Combine(AppPaths.CacheFolder, "lyrics")))
        {
            Pace = _services.IsDemo ? TimeSpan.Zero : LineIndexBuilder.DefaultPace,
        };
        _builder = builder;
        builder.ProgressChanged += OnBuilderProgress;
        _services.Library.LikedSongsChanged += OnLibraryChanged;
        _services.Library.PlaylistsChanged += OnLibraryChanged;
        _services.Home.History.Changed += OnLibraryChanged;

        var token = running.Token;
        var previous = _run;
        _run = Task.Run(
            async () =>
            {
                try
                {
                    // A run that was stopped a moment ago finishes first.
                    await previous.ConfigureAwait(false);
                    await Task.Delay(atLaunch && !_services.IsDemo ? LaunchDelay : TimeSpan.Zero, token).ConfigureAwait(false);
                    await builder.RunAsync(_ => Task.FromResult(Songs()), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    PlaybackLog.Note($"line finder stopped: {ex.GetType().Name}: {ex.Message}");
                }
            },
            CancellationToken.None);
    }

    /// <summary>Stops filling the index and lets go of it in memory (the plugin was turned off). Call on the interface thread.</summary>
    public void Stop()
    {
        if (_running is not { } running)
        {
            return;
        }

        _running = null;
        running.Cancel();
        running.Dispose();
        if (_builder is { } builder)
        {
            builder.ProgressChanged -= OnBuilderProgress;
            _builder = null;
        }

        _services.Library.LikedSongsChanged -= OnLibraryChanged;
        _services.Library.PlaylistsChanged -= OnLibraryChanged;
        _services.Home.History.Changed -= OnLibraryChanged;

        // Once the run has noticed, so it does not read the index back in.
        var index = _index;
        _run = _run.ContinueWith(_ => index.Unload(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        Progress = default;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The songs with a line that fits what was typed, best first, from the
    /// index on this PC (no request to anyone). Empty while the plugin is off.
    /// </summary>
    public Task<IReadOnlyList<LineMatch>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsOn)
        {
            return Task.FromResult<IReadOnlyList<LineMatch>>([]);
        }

        var index = _index;
        return Task.Run(() => index.Search(query, LineIndex.DefaultLimit, cancellationToken), cancellationToken);
    }

    /// <summary>Signing out: the words gathered for this account go, and the index starts afresh for the next one.</summary>
    public void Forget()
    {
        var on = _running is not null;
        Stop();
        var index = _index;
        _run = _run.ContinueWith(_ => index.Clear(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        if (on)
        {
            Start(atLaunch: false);
        }
    }

    public void Dispose()
    {
        Stop();
        _http?.Dispose();
    }

    /// <summary>
    /// The songs to cover, from what Resonate already keeps (never a request
    /// to Spotify): the songs played, newest first, then Liked Songs, then
    /// each playlist whose songs are kept on this PC. Runs in the background.
    /// </summary>
    private IReadOnlyList<TrackInfo> Songs()
    {
        var songs = new List<TrackInfo>();
        var history = _services.Home.History;
        history.Load();
        songs.AddRange(history.Plays.Select(play => play.ToTrack()));

        var library = _services.Library;
        if (library.PeekStoredLikedSongs() is { } liked)
        {
            songs.AddRange(liked);
        }

        // The snapshot is replaced, never changed, so it can be read here.
        if (library.Snapshot is { Playlists: var playlists })
        {
            foreach (var playlist in playlists)
            {
                if (playlist.Id is { Length: > 0 } id && library.PeekStoredPlaylistTracks(id) is { } tracks)
                {
                    songs.AddRange(tracks);
                }
            }
        }

        return songs;
    }

    /// <summary>LRCLIB through a client of its own with short timeouts; the demo's made-up words.</summary>
    private ILyricsSource Source()
    {
        if (_services.IsDemo)
        {
            return new DemoLyrics();
        }

        _http ??= new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        return new LrcLibClient(_http, LrcLibClient.UserAgentFor(AppInfo.Version));
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => _builder?.Refresh();

    /// <summary>From the builder, on any thread: told on the interface thread, once for each burst.</summary>
    private void OnBuilderProgress(object? sender, LineIndexProgress progress)
    {
        if (Interlocked.Exchange(ref _progressQueued, 1) != 0)
        {
            return;
        }

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Interlocked.Exchange(ref _progressQueued, 0);
            if (_builder is { } builder && ReferenceEquals(sender, builder))
            {
                Progress = builder.Progress;
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }
}

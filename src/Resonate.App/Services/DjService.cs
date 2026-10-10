using Microsoft.UI.Dispatching;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Resonate.App.Services;

/// <summary>
/// Resonate's own DJ (the owner's choice of 10 October 2026, as Spotify
/// lets no other app start its DJ): a long run of themed sets from Liked
/// Songs, the listening history and new music near the user's taste
/// (<see cref="DjMix"/>, <see cref="DjDiscovery"/>, looked for once a day),
/// played like any list, with a voice from Windows that introduces each set
/// over the music (turned down meanwhile) while the user keeps it on.
/// "Switch it up" starts a new run with another kind of set. The run ends
/// as soon as something else plays. What the DJ played in the last days,
/// and each run's first set, stay out of the next runs
/// (<c>dj-recent.txt</c> in the cache folder), so pressing play again
/// brings other songs (the owner's request, 10 October 2026).
/// </summary>
internal sealed class DjService
{
    /// <summary>About fourteen hours of music at six songs a set.</summary>
    private const int RunLength = 40;

    private const double DuckedVolume = 0.35;

    /// <summary>What the DJ played is kept out of new runs this long, and at most this many songs.</summary>
    private static readonly TimeSpan RecentFor = TimeSpan.FromDays(3);

    private const int RecentMost = 600;

    /// <summary>Looking for new music waits at most this long before the run starts without it.</summary>
    private static readonly TimeSpan FindingWait = TimeSpan.FromSeconds(8);

    private static string RecentFile => Path.Combine(AppPaths.CacheFolder, "dj-recent.txt");

    private readonly AppServices _services;
    private readonly DispatcherQueue _dispatcher;
    private IReadOnlyList<DjSet> _sets = [];
    private Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    private int _announced = -1;
    private int _followQueued;
    private MediaPlayer? _voice;
    private SpeechSynthesizer? _speech;
    private IReadOnlyList<TrackInfo> _run = [];
    private List<(DateTimeOffset At, string Uri)>? _recent;
    private IReadOnlyList<DjFind> _finds = [];
    private DateOnly _findsDay;
    private string? _lastPlayedUri;

    public DjService(AppServices services)
    {
        _services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        services.Player.StateChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref _followQueued, 1) == 0)
            {
                _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    Interlocked.Exchange(ref _followQueued, 0);
                    Follow();
                });
            }
        };
    }

    /// <summary>Raised on the interface thread when the run starts, ends or moves to another set.</summary>
    public event EventHandler? Changed;

    /// <summary>The sets of the run, in the order they play.</summary>
    public IReadOnlyList<DjSet> Sets => _sets;

    /// <summary>Every song of the run, in the order they play.</summary>
    public IReadOnlyList<TrackInfo> Run => _run;

    /// <summary>Where the song playing now is in <see cref="Run"/>, or -1.</summary>
    public int CurrentIndex => _services.Player.State.TrackUri is { } uri && _positions.TryGetValue(uri, out var index) ? index : -1;

    /// <summary>Plays the run from the song at <paramref name="index"/> (the DJ page's Up next).</summary>
    public Task PlayFromAsync(int index) =>
        index >= 0 && index < _run.Count
            ? _services.Player.PlayAsync(new PlayRequest(_run, index, null, "DJ") { Shuffle = false })
            : Task.CompletedTask;

    /// <summary>The set playing now, or -1 while the DJ is not what plays.</summary>
    public int CurrentSet { get; private set; } = -1;

    public bool IsOn => CurrentSet >= 0;

    /// <summary>Making a run (reading Liked Songs the first time can take a moment).</summary>
    public bool IsStarting { get; private set; }

    /// <summary>
    /// Starts a new run, first with <paramref name="first"/> when there are
    /// songs for it, and plays it. False when there is nothing to play.
    /// </summary>
    public async Task<bool> StartAsync(DjSetKind? first = null)
    {
        if (IsStarting)
        {
            return true;
        }

        IsStarting = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var library = _services.Library;
            var liked = library.GetStoredLikedSongs() ?? await library.GetAllLikedSongsAsync(CancellationToken.None);
            _services.Home.LoadStored();
            var plays = _services.Home.History.Plays.ToList();

            // What played already in this run, and what the DJ played lately, stays out of the new one.
            var played = new HashSet<string>(StringComparer.Ordinal);
            if (CurrentSet >= 0)
            {
                foreach (var set in _sets.Take(CurrentSet + 1))
                {
                    played.UnionWith(set.Tracks.Select(t => t.Uri!));
                }
            }

            played.UnionWith(Recent().Select(r => r.Uri));
            var finds = await FindNewMusicAsync(liked);

            var setSize = Math.Clamp(_services.Settings.DjSetSize, DjMix.SmallestSet, 12);
            var sets = await Task.Run(() => DjMix.Build(liked, plays, DateTimeOffset.Now, Random.Shared, RunLength, setSize, first, played, finds));
            if (sets.Count == 0)
            {
                return false;
            }

            _sets = sets;
            _positions = [];
            var run = DjMix.Flatten(sets);
            _run = run;

            // The first set counts as heard, so starting again opens with other songs.
            Remember(sets[0].Tracks.Select(t => t.Uri!));
            for (var i = 0; i < run.Count; i++)
            {
                _positions.TryAdd(run[i].Uri!, i);
            }

            CurrentSet = 0;
            _announced = -1;
            Changed?.Invoke(this, EventArgs.Empty);
            Announce(0);
            await _services.Player.PlayAsync(new PlayRequest(run, 0, null, "DJ") { Shuffle = false });
            App.MainWindow?.NoteListPlayed(MainWindow.DjKey, "DJ");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
        finally
        {
            IsStarting = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Another kind of set from now on: a new run that starts with something other than what plays.</summary>
    public Task<bool> SwitchUpAsync()
    {
        var current = CurrentSet >= 0 && CurrentSet < _sets.Count ? _sets[CurrentSet].Kind : (DjSetKind?)null;
        var kinds = Enum.GetValues<DjSetKind>().Where(k => k != current && k != DjSetKind.Mix).ToArray();
        return StartAsync(kinds[Random.Shared.Next(kinds.Length)]);
    }

    /// <summary>The voice was switched off: it stops at once.</summary>
    public void StopVoice() => _voice?.Pause();

    /// <summary>Which set plays now; a new one is introduced. Something else playing ends the run.</summary>
    private void Follow()
    {
        if (_sets.Count == 0)
        {
            return;
        }

        var uri = _services.Player.State.TrackUri;
        var set = uri is not null && _positions.TryGetValue(uri, out var index) ? DjMix.SetAt(_sets, index) : -1;
        if (set >= 0 && uri != _lastPlayedUri)
        {
            // A song of the run plays: it stays out of the next runs, and the page's Up next moves on.
            _lastPlayedUri = uri;
            Remember([uri!]);
            if (set == CurrentSet)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        if (set < 0 && IsStarting)
        {
            return;
        }

        if (set == CurrentSet)
        {
            return;
        }

        CurrentSet = set;
        if (set < 0)
        {
            _sets = [];
            _positions = [];
            _run = [];
        }
        else if (set > _announced)
        {
            Announce(set);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The set's line, said by Windows' voice over the music turned down.</summary>
    private void Announce(int set)
    {
        _announced = Math.Max(_announced, set);
        if (!_services.Settings.DjVoice || _services.IsDemo || set < 0 || set >= _sets.Count)
        {
            return;
        }

        _ = SayAsync(_sets[set].Intro);
    }

    private async Task SayAsync(string text)
    {
        try
        {
            _speech ??= MakeSpeech();
            using var stream = await _speech.SynthesizeTextToStreamAsync(text);
            _voice ??= new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Speech };

            // The voice is no media of its own: not in Windows' media controls, not on the media keys.
            _voice.CommandManager.IsEnabled = false;
            var ended = new TaskCompletionSource();
            void Done(MediaPlayer sender, object args) => ended.TrySetResult();
            void Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => ended.TrySetResult();
            _voice.MediaEnded += Done;
            _voice.MediaFailed += Failed;
            _voice.Source = MediaSource.CreateFromStream(stream, stream.ContentType);

            // The music steps back while the DJ talks, and comes back after unless the user moved it meanwhile.
            var player = _services.Player;
            var before = player.State.Volume;
            var ducked = Math.Round(before * DuckedVolume, 2);
            if (before > 0.05)
            {
                await player.SetVolumeAsync(ducked);
            }

            _voice.Play();
            await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            _voice.MediaEnded -= Done;
            _voice.MediaFailed -= Failed;
            if (before > 0.05 && Math.Abs(player.State.Volume - ducked) < 0.02)
            {
                await player.SetVolumeAsync(before);
            }
        }
        catch (Exception ex)
        {
            // No voice on this PC, or Windows refused: the music goes on without it.
            CrashGuard.Write("dj voice", ex, null);
        }
    }

    /// <summary>New music near the user's taste, looked for once a day, waiting at most <see cref="FindingWait"/>.</summary>
    private async Task<IReadOnlyList<DjFind>> FindNewMusicAsync(IReadOnlyList<TrackInfo> liked)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_findsDay == today)
        {
            return _finds;
        }

        using var timeout = new CancellationTokenSource(FindingWait);
        var top = _services.Home.Content?.TopFor(TopRange.ShortTerm)?.Artists ?? [];
        var finds = await DjDiscovery.FindAsync(_services.Api, [.. liked], top, Random.Shared, timeout.Token);
        if (finds.Count > 0)
        {
            _finds = finds;
            _findsDay = today;
        }

        return finds;
    }

    /// <summary>The songs the DJ played lately (read once from the cache folder).</summary>
    private List<(DateTimeOffset At, string Uri)> Recent()
    {
        if (_recent is null)
        {
            _recent = [];
            try
            {
                if (File.Exists(RecentFile))
                {
                    foreach (var line in File.ReadAllLines(RecentFile))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length == 2 && DateTimeOffset.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at))
                        {
                            _recent.Add((at, parts[1]));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Then every song is free again; nothing more.
            }
        }

        var since = DateTimeOffset.UtcNow - RecentFor;
        _recent.RemoveAll(r => r.At < since);
        return _recent;
    }

    /// <summary>Keeps <paramref name="uris"/> out of the next runs for a few days.</summary>
    private void Remember(IEnumerable<string> uris)
    {
        var recent = Recent();
        var now = DateTimeOffset.UtcNow;
        foreach (var uri in uris)
        {
            recent.RemoveAll(r => r.Uri == uri);
            recent.Add((now, uri));
        }

        if (recent.Count > RecentMost)
        {
            recent.RemoveRange(0, recent.Count - RecentMost);
        }

        var lines = recent.Select(r => r.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "\t" + r.Uri).ToArray();
        _ = Task.Run(() =>
        {
            try
            {
                File.WriteAllLines(RecentFile, lines);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for this session only.
            }
        });
    }

    /// <summary>
    /// An English voice (the DJ's lines are English), a natural one when
    /// Windows offers any to apps, a touch quicker than Windows' default.
    /// </summary>
    private static SpeechSynthesizer MakeSpeech()
    {
        var speech = new SpeechSynthesizer();
        var english = SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ToList();
        if (english.FirstOrDefault(v => v.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase)) is { } natural)
        {
            speech.Voice = natural;
        }
        else if (!speech.Voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) && english.Count > 0)
        {
            speech.Voice = english[0];
        }

        speech.Options.SpeakingRate = 1.05;
        return speech;
    }
}

using Microsoft.UI.Dispatching;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Resonate.App.Services;

/// <summary>
/// Resonate's own DJ (the owner's choice of 10 October 2026, as Spotify
/// lets no other app start its DJ): a long run of themed sets from Liked
/// Songs and the listening history (<see cref="DjMix"/>), played like any
/// list, with a voice from Windows that introduces each set over the music
/// (turned down meanwhile) while the user keeps it on. "Switch it up"
/// starts a new run with another kind of set. The run ends as soon as
/// something else plays.
/// </summary>
internal sealed class DjService
{
    /// <summary>About fourteen hours of music at six songs a set.</summary>
    private const int RunLength = 40;

    private const double DuckedVolume = 0.35;

    private readonly AppServices _services;
    private readonly DispatcherQueue _dispatcher;
    private IReadOnlyList<DjSet> _sets = [];
    private Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    private int _announced = -1;
    private int _followQueued;
    private MediaPlayer? _voice;
    private SpeechSynthesizer? _speech;

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

            // What played already in this run stays out of the new one.
            var played = new HashSet<string>(StringComparer.Ordinal);
            if (CurrentSet >= 0)
            {
                foreach (var set in _sets.Take(CurrentSet + 1))
                {
                    played.UnionWith(set.Tracks.Select(t => t.Uri!));
                }
            }

            var setSize = Math.Clamp(_services.Settings.DjSetSize, DjMix.SmallestSet, 12);
            var sets = await Task.Run(() => DjMix.Build(liked, plays, DateTimeOffset.Now, Random.Shared, RunLength, setSize, first, played));
            if (sets.Count == 0)
            {
                return false;
            }

            _sets = sets;
            _positions = [];
            var run = DjMix.Flatten(sets);
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

    /// <summary>An English voice (the DJ's lines are English), a touch quicker than Windows' default.</summary>
    private static SpeechSynthesizer MakeSpeech()
    {
        var speech = new SpeechSynthesizer();
        if (!speech.Voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) is { } english)
        {
            speech.Voice = english;
        }

        speech.Options.SpeakingRate = 1.05;
        return speech;
    }
}

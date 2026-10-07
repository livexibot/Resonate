using System.Globalization;
using System.Text;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Audio;

/// <summary>What to do with an equalizer change for the Spotify app.</summary>
public enum EqualizerWritePlan
{
    /// <summary>Spotify's settings file was not found; keep the change for when it is.</summary>
    NotFound,

    /// <summary>Spotify already has these settings.</summary>
    NothingToDo,

    /// <summary>Spotify is running and would overwrite the file when it quits; keep the change until it next starts.</summary>
    KeepPending,

    /// <summary>Spotify is closed: write the file now.</summary>
    WriteNow,
}

/// <summary>Where an equalizer change for the Spotify app stands.</summary>
public enum EqualizerApplyResult
{
    /// <summary>Spotify's settings have it (written now, or they already matched).</summary>
    Applied,

    /// <summary>Spotify is running; it gets the change the next time it starts.</summary>
    Pending,

    /// <summary>Spotify's settings were not found on this computer.</summary>
    SpotifyNotFound,

    /// <summary>Spotify's settings could not be written; the change is kept for the next start.</summary>
    Failed,
}

/// <summary>What Resonate knows about the Spotify app's equalizer right now.</summary>
/// <param name="Spotify">Spotify's settings as its file has them, or null when the file was not found or read.</param>
/// <param name="Pending">A change Spotify has not been given yet, if any.</param>
/// <param name="SpotifyRunning">Whether the Spotify app is running.</param>
public sealed record SpotifyEqualizerStatus(SpotifyAudioPrefs? Spotify, EqualizerSettings? Pending, bool SpotifyRunning);

/// <summary>How "Restart Spotify now" went.</summary>
/// <param name="Restart">Whether Spotify closed and started again.</param>
/// <param name="Applied">Whether Spotify's settings now have the change.</param>
/// <param name="Resume">Whether what was playing was put back.</param>
/// <param name="Before">What was playing before, for telling the user where they were.</param>
/// <param name="Position">Where in that song.</param>
public sealed record SpotifyRestartOutcome(
    SpotifyRestartStatus Restart,
    bool Applied,
    ResumeOutcome Resume,
    PlayerState? Before,
    TimeSpan Position);

/// <summary>
/// Keeps the Spotify app's own equalizer in step with Resonate's. Spotify
/// reads its settings file when it starts and rewrites it when it quits, so
/// a change is written at once only while Spotify is closed. While it runs,
/// the change waits (<see cref="Pending"/>) until Resonate next starts
/// Spotify, or until the user restarts Spotify from Resonate. Spotify's
/// audio never passes through Resonate: Spotify applies its own equalizer.
/// Thread-safe; everything here may run on a background thread.
/// </summary>
public sealed class SpotifyEqualizerSync
{
    /// <summary>Shown when Spotify's settings file is not on this computer (and in demo mode).</summary>
    public const string NotFoundText =
        "Spotify's settings were not found on this computer, so for now the equalizer only changes your own music files.";

    /// <summary>How long to wait for Spotify's media session after a restart.</summary>
    public static readonly TimeSpan ResumeTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<string> _spotifyFolders;
    private readonly ISpotifyAppLauncher _launcher;
    private readonly ISpotifyAppRestarter? _restarter;
    private readonly PlayerController? _player;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private EqualizerSettings? _pending;

    /// <param name="spotifyFolders">Spotify's own folders, where its "Users" folder is (none in demo mode).</param>
    /// <param name="launcher">Tells whether Spotify is running.</param>
    /// <param name="restarter">Restarts Spotify; null where that is not possible.</param>
    /// <param name="player">The Spotify player, to put back what was playing after a restart.</param>
    /// <param name="pending">A change kept from the last session that Spotify has not been given yet.</param>
    public SpotifyEqualizerSync(
        IReadOnlyList<string> spotifyFolders,
        ISpotifyAppLauncher launcher,
        ISpotifyAppRestarter? restarter,
        PlayerController? player,
        EqualizerSettings? pending = null,
        TimeProvider? time = null)
    {
        _spotifyFolders = spotifyFolders;
        _launcher = launcher;
        _restarter = restarter;
        _player = player;
        _pending = pending;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on any thread when <see cref="Pending"/> changes.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>A change Spotify has not been given yet, or null. Never waits for file work in progress.</summary>
    public EqualizerSettings? Pending => Volatile.Read(ref _pending);

    /// <summary>Whether Resonate can restart Spotify here (not in demo mode).</summary>
    public bool CanRestart => _restarter is not null;

    /// <summary>The decision behind <see cref="Apply"/>.</summary>
    public static EqualizerWritePlan Plan(bool found, bool alreadyThere, bool spotifyRunning) =>
        !found ? EqualizerWritePlan.NotFound
        : alreadyThere ? EqualizerWritePlan.NothingToDo
        : spotifyRunning ? EqualizerWritePlan.KeepPending
        : EqualizerWritePlan.WriteNow;

    /// <summary>
    /// Reads Spotify's settings. A pending change is written first when
    /// Spotify has closed since (for example because the user quit it).
    /// </summary>
    public SpotifyEqualizerStatus Refresh()
    {
        lock (_gate)
        {
            var running = _launcher.IsRunning;
            if (!running)
            {
                WritePendingLocked();
            }

            var path = SpotifyPrefsFile.FindNewest(_spotifyFolders);
            var spotify = path is null ? null : SpotifyPrefsFile.TryRead(path);
            return new SpotifyEqualizerStatus(spotify, _pending, running);
        }
    }

    /// <summary>
    /// Gives Spotify <paramref name="settings"/>: written to its settings now
    /// when it is closed, otherwise kept as <see cref="Pending"/>.
    /// </summary>
    public EqualizerApplyResult Apply(EqualizerSettings settings)
    {
        lock (_gate)
        {
            var path = SpotifyPrefsFile.FindNewest(_spotifyFolders);
            var current = path is null ? null : SpotifyPrefsFile.TryRead(path);
            var plan = Plan(current is not null, settings.Equals(current?.Equalizer), _launcher.IsRunning);
            switch (plan)
            {
                case EqualizerWritePlan.NotFound:
                    // Kept, so Spotify gets it once its settings exist (after it is set up).
                    SetPendingLocked(settings);
                    return EqualizerApplyResult.SpotifyNotFound;

                case EqualizerWritePlan.NothingToDo:
                    SetPendingLocked(null);
                    return EqualizerApplyResult.Applied;

                case EqualizerWritePlan.KeepPending:
                    SetPendingLocked(settings);
                    return EqualizerApplyResult.Pending;

                default:
                    if (!TryWrite(path!, settings))
                    {
                        SetPendingLocked(settings);
                        return EqualizerApplyResult.Failed;
                    }

                    // Spotify may have started meanwhile and read the old file: then it still waits.
                    if (_launcher.IsRunning)
                    {
                        SetPendingLocked(settings);
                        return EqualizerApplyResult.Pending;
                    }

                    SetPendingLocked(null);
                    return EqualizerApplyResult.Applied;
            }
        }
    }

    /// <summary>
    /// For <see cref="ISpotifyAppRestarter.BeforeStart"/>: writes a pending
    /// change right before Resonate starts Spotify.
    /// </summary>
    public void ApplyPendingBeforeStart()
    {
        lock (_gate)
        {
            WritePendingLocked();
        }
    }

    /// <summary>
    /// Restarts the Spotify app so it uses the pending change now: notes what
    /// plays and where, pauses it, closes Spotify, writes its settings, starts
    /// it again hidden, then puts the song back where it was (see
    /// <see cref="PlayerController.ResumeAsync"/>).
    /// </summary>
    public async Task<SpotifyRestartOutcome> RestartSpotifyAsync(CancellationToken cancellationToken)
    {
        if (_restarter is null)
        {
            return new SpotifyRestartOutcome(SpotifyRestartStatus.CouldNotClose, false, ResumeOutcome.NothingToResume, null, TimeSpan.Zero);
        }

        var before = _player?.State;
        var position = before?.PositionAt(_time.GetUtcNow()) ?? TimeSpan.Zero;
        if (before is { IsPlaying: true })
        {
            // A clean stop rather than cutting the sound, and Spotify notes the position.
            await _player!.PauseAsync().ConfigureAwait(false);
        }

        var applied = false;
        var restart = await _restarter.RestartAsync(
            () =>
            {
                lock (_gate)
                {
                    applied = _pending is null || WritePendingLocked();
                }
            },
            cancellationToken).ConfigureAwait(false);

        var resume = ResumeOutcome.NothingToResume;
        if (_player is not null && before is { HasTrack: true })
        {
            if (restart == SpotifyRestartStatus.Restarted)
            {
                resume = await _player.ResumeAsync(before, position, ResumeTimeout, cancellationToken).ConfigureAwait(false);
            }
            else if (restart == SpotifyRestartStatus.CouldNotClose && before.IsPlaying)
            {
                // Spotify kept running, paused by Resonate: play on.
                await _player.PlayAsync().ConfigureAwait(false);
                resume = ResumeOutcome.Playing;
            }
        }

        return new SpotifyRestartOutcome(restart, applied, resume, before, position);
    }

    /// <summary>
    /// Calls <paramref name="changed"/> (on a background thread) whenever
    /// Spotify's settings file changes, for example when the user changes the
    /// equalizer in the Spotify app. Null when there is no file to watch.
    /// </summary>
    public IDisposable? Watch(Action changed)
    {
        var path = SpotifyPrefsFile.FindNewest(_spotifyFolders);
        if (path is null)
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The status line for an equalizer change.</summary>
    public static string Describe(EqualizerApplyResult result) => result switch
    {
        EqualizerApplyResult.Applied => "Applied to Spotify.",
        EqualizerApplyResult.Pending => "Spotify will use this the next time it starts.",
        EqualizerApplyResult.SpotifyNotFound => NotFoundText,
        _ => "Spotify's settings could not be saved. Resonate tries again the next time it starts Spotify.",
    };

    /// <summary>The status line after "Restart Spotify now".</summary>
    public static string Describe(SpotifyRestartOutcome outcome)
    {
        var text = new StringBuilder();
        text.Append(outcome.Restart switch
        {
            SpotifyRestartStatus.CouldNotClose =>
                "Spotify could not be closed, so it keeps its old equalizer for now. Spotify will use this the next time it starts.",
            SpotifyRestartStatus.CouldNotStart or SpotifyRestartStatus.NotInstalled => outcome.Applied
                ? "Applied to Spotify's settings, but Spotify did not start again. Open it from the Start menu."
                : "Spotify closed but did not start again, and its settings could not be saved. Open it from the Start menu.",
            _ => outcome.Applied
                ? "Applied to Spotify."
                : "Spotify restarted, but its settings could not be saved. Resonate tries again the next time it starts Spotify.",
        });

        if (outcome.Restart != SpotifyRestartStatus.Restarted)
        {
            return text.ToString();
        }

        switch (outcome.Resume)
        {
            case ResumeOutcome.Playing:
                text.Append(" Your song carried on where it was.");
                break;
            case ResumeOutcome.Paused:
                text.Append(" Your song is paused where you left it.");
                break;
            case ResumeOutcome.NotResumed when outcome.Before?.Title is { } title:
                text.Append(CultureInfo.CurrentCulture, $" Spotify may not have reopened “{title}”: if not, play it again and move to {FormatPosition(outcome.Position)}.");
                break;
        }

        return text.ToString();
    }

    internal static string FormatPosition(TimeSpan position) =>
        position.TotalHours >= 1
            ? position.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : position.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>Writes the pending change while Spotify is closed. True when Spotify has it.</summary>
    private bool WritePendingLocked()
    {
        if (_pending is not { } pending)
        {
            return true;
        }

        if (SpotifyPrefsFile.FindNewest(_spotifyFolders) is not { } path || !TryWrite(path, pending))
        {
            return false;
        }

        SetPendingLocked(null);
        return true;
    }

    private static bool TryWrite(string path, EqualizerSettings settings)
    {
        try
        {
            SpotifyPrefsFile.WriteEqualizer(path, settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    private void SetPendingLocked(EqualizerSettings? pending)
    {
        if (Equals(_pending, pending))
        {
            return;
        }

        Volatile.Write(ref _pending, pending);

        // Raised under the lock so listeners see changes in order; they must not call back in.
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }
}

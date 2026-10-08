using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Resonate.App.Services;

public enum UpdateStatus
{
    /// <summary>Running from a build folder, not an installed copy; nothing to update.</summary>
    NotInstalled,
    UpToDate,
    ReadyToRestart,
    Failed,
}

/// <summary>
/// Installs new releases from this project's GitHub releases: checks and
/// downloads in the background, showing how the download goes, then waits
/// for one click to restart.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/livexibot/Resonate";

    // Downloads report every percent; the progress bars need no more than this.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly UpdateManager? _manager;
    private readonly object _gate = new();
    private UpdateInfo? _pending;
    private Task<UpdateStatus>? _running;
    private UpdateDownloadMeter? _meter;
    private UpdateProgress? _progress;
    private long _lastProgressEvent;

    /// <summary>Checks this project's GitHub releases (they must be public).</summary>
    public UpdateService()
        : this(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false))
    {
    }

    /// <summary>Checks another source, such as a local folder in CI's install test.</summary>
    public UpdateService(IUpdateSource source)
    {
        try
        {
            _manager = new UpdateManager(new ProgressReportingSource(source, OnFileProgress));
        }
        catch (Exception)
        {
            _manager = null;
        }
    }

    /// <summary>How often a running Resonate looks for a new version.</summary>
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromHours(4);

    /// <summary>Raised on a background thread when an update has been downloaded.</summary>
    public event EventHandler? UpdateReady;

    /// <summary>Raised on a background thread as a download goes (a few times a second at most) and when it ends.</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>The download under way, or null when nothing is downloading.</summary>
    public UpdateProgress? Progress => Volatile.Read(ref _progress);

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>
    /// Looks for a new version and downloads it. A second call while one is
    /// under way (the background check and the Settings button) waits for
    /// the same download instead of starting another.
    /// </summary>
    public Task<UpdateStatus> CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_running is not { IsCompleted: false })
            {
                _running = Task.Run(() => CheckAndDownloadOnceAsync(cancellationToken), CancellationToken.None);
            }

            return _running;
        }
    }

    /// <summary>For the screenshots in demo mode: shows a made-up download (or none) without downloading anything.</summary>
    internal void Preview(UpdateProgress? progress) => Publish(progress, force: true);

    private async Task<UpdateStatus> CheckAndDownloadOnceAsync(CancellationToken cancellationToken)
    {
        if (_manager is null || !_manager.IsInstalled)
        {
            return UpdateStatus.NotInstalled;
        }

        if (_pending is not null)
        {
            return UpdateStatus.ReadyToRestart;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return UpdateStatus.UpToDate;
            }

            var meter = new UpdateDownloadMeter(update);
            Volatile.Write(ref _meter, meter);
            Publish(meter.Start(), force: true);
            try
            {
                await _manager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken).ConfigureAwait(false);
                _pending = update;
            }
            finally
            {
                // Done or failed: the progress bars go, after the version is known to be ready.
                Volatile.Write(ref _meter, null);
                Publish(null, force: true);
            }

            UpdateReady?.Invoke(this, EventArgs.Empty);
            return UpdateStatus.ReadyToRestart;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Offline, or GitHub is not answering: try again next time.
            return UpdateStatus.Failed;
        }
    }

    private void OnFileProgress(VelopackAsset file, int percent)
    {
        if (Volatile.Read(ref _meter) is { } meter)
        {
            var progress = meter.Report(file, percent, Stopwatch.GetTimestamp());
            Publish(progress, force: progress.Preparing);
        }
    }

    private void Publish(UpdateProgress? progress, bool force)
    {
        lock (_gate)
        {
            Volatile.Write(ref _progress, progress);
            var now = Stopwatch.GetTimestamp();
            if (!force && Stopwatch.GetElapsedTime(_lastProgressEvent, now) < ProgressInterval)
            {
                // Shown with the next event; the newest progress is always kept.
                return;
            }

            _lastProgressEvent = now;
        }

        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes Resonate, installs the downloaded version and opens it again.</summary>
    public void RestartToUpdate()
    {
        if (_manager is not null && _pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }
}

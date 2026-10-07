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
/// downloads in the background, then waits for one click to restart.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/livexibot/Resonate";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (Exception)
        {
            _manager = null;
        }
    }

    /// <summary>Raised on a background thread when an update has been downloaded.</summary>
    public event EventHandler? UpdateReady;

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public async Task<UpdateStatus> CheckAndDownloadAsync(CancellationToken cancellationToken)
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

            await _manager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken).ConfigureAwait(false);
            _pending = update;
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

    /// <summary>Closes Resonate, installs the downloaded version and opens it again.</summary>
    public void RestartToUpdate()
    {
        if (_manager is not null && _pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }
}

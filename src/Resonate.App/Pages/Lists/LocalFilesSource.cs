using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;

namespace Resonate.App.Pages.Lists;

/// <summary>Music files on this computer, from the folders chosen in Settings. Resonate plays them itself.</summary>
public sealed class LocalFilesSource : TrackListSource
{
    public const string ListKey = "local";

    private readonly LocalFilesService _localFiles;
    private EventHandler<LocalLibraryChange>? _onLibraryChanged;

    public LocalFilesSource(AppServices services) => _localFiles = services.LocalFiles;

    public override string Key => ListKey;

    public override string EmptyText => _localFiles.Folders.Count == 0
        ? "Add a folder in Settings, Misc, Local Files."
        : IsLooking
            ? $"Looking for music in {_localFiles.FolderNames}…"
            : $"No music files in {_localFiles.FolderNames}.";

    public override string OwnOrderName => "Recently added";

    public override ListHeader CachedHeader => new("COLLECTION", "Local Files", null, Details(), null, "Local Files", Glyph: "\uE8B7", AccentCover: true);

    public override Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>Scans add, change and remove songs while the page is open; it follows along.</summary>
    public override void Attach(Action<bool> changed)
    {
        Detach();
        _onLibraryChanged = (_, change) => changed(change.FilesChanged);
        _localFiles.Library.Changed += _onLibraryChanged;
    }

    public override void Detach()
    {
        if (_onLibraryChanged is not null)
        {
            _localFiles.Library.Changed -= _onLibraryChanged;
            _onLibraryChanged = null;
        }
    }

    /// <summary>The first look through the folders has not finished yet.</summary>
    private bool IsLooking =>
        _localFiles.DemoTracks is null && (_localFiles.Library.Status.IsScanning || !_localFiles.Library.Status.HasScanned);

    private async Task<FullTrackList> LoadAsync(CancellationToken cancellationToken) =>
        new(await _localFiles.GetTracksAsync(cancellationToken), ItemsHidden: false);

    private string Details()
    {
        var status = _localFiles.Library.Status;
        return status.IsScanning
            ? status.ToRead > 0
                ? $"On this computer · reading {status.Read:N0} of {status.ToRead:N0} new files"
                : "On this computer · looking for new music"
            : "On this computer";
    }
}

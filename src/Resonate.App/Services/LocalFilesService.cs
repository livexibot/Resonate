using System.Runtime.CompilerServices;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Windows.LocalAudio;

namespace Resonate.App.Services;

/// <summary>
/// Local Files: the user's own music files, from the folders chosen in
/// Settings (Music and Downloads until they choose). The list comes from an
/// index at once; scans run in the background after the window shows, and
/// again by themselves when files in the folders change.
/// </summary>
public sealed class LocalFilesService : IDisposable
{
    // One TrackInfo per file, so a rescan that keeps a file keeps its row.
    private static readonly ConditionalWeakTable<LocalFile, TrackInfo> Tracks = new();

    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Lazy<IReadOnlyList<string>> _defaultFolders;
    private readonly CancellationTokenSource _lifetime = new();
    private int _started;
    private int _foldersSet;

    public LocalFilesService(
        AppSettings settings,
        Action saveSettings,
        LocalLibrary library,
        LocalCoverCache? covers,
        LocalMediaControls? controls,
        IReadOnlyList<TrackInfo>? demoTracks = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        Library = library;
        Covers = covers;
        Controls = controls;
        DemoTracks = demoTracks;
        // No folder until the user adds one (the owner's choice, 9 October 2026); the demo shows two.
        _defaultFolders = new Lazy<IReadOnlyList<string>>(() => demoTracks is null
            ? []
            : [@"C:\Users\Demo\Music", @"C:\Users\Demo\Downloads"]);
    }

    /// <summary>Raised on the interface thread when Local Files is shown in or hidden from the sidebar.</summary>
    public event EventHandler? SidebarChanged;

    public LocalLibrary Library { get; }

    /// <summary>Small covers for the rows; null in demo mode.</summary>
    public LocalCoverCache? Covers { get; }

    /// <summary>Windows' media controls for the local files player; null in demo mode.</summary>
    public LocalMediaControls? Controls { get; }

    /// <summary>Made-up songs instead of the user's files, in demo mode.</summary>
    public IReadOnlyList<TrackInfo>? DemoTracks { get; }

    public bool ShowInSidebar
    {
        get => _settings.ShowLocalFiles;
        set
        {
            if (_settings.ShowLocalFiles == value)
            {
                return;
            }

            _settings.ShowLocalFiles = value;
            _saveSettings();
            SidebarChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>The folders looked in.</summary>
    public IReadOnlyList<string> Folders => _settings.LocalFolders ?? _defaultFolders.Value;

    /// <summary>"Music and Downloads": the folders' names, for sentences.</summary>
    public string FolderNames
    {
        get
        {
            var names = Folders.Select(f => Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : f).ToList();
            return names.Count switch
            {
                0 => "no folders",
                1 => names[0],
                _ => string.Join(", ", names[..^1]) + " and " + names[^1],
            };
        }
    }

    /// <summary>Reads the index and the folders once, after the window shows; then watches them.</summary>
    public void Start()
    {
        if (DemoTracks is not null || Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        var token = _lifetime.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await LoadAsync(token).ConfigureAwait(false);
                    await Library.ScanAsync(token).ConfigureAwait(false);
                    Library.StartWatching();
                }
                catch (OperationCanceledException)
                {
                }
            },
            token);
    }

    /// <summary>The list from the index (and whatever scans found since). Call on a background thread.</summary>
    public async Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken)
    {
        if (DemoTracks is not null)
        {
            return DemoTracks;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        return Library.Files.Select(ToTrack).ToList();
    }

    public void AddFolder(string folder)
    {
        var folders = Folders.ToList();
        if (folders.Any(f => string.Equals(Path.TrimEndingDirectorySeparator(f), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        folders.Add(folder);
        SetFolders(folders);
    }

    public void RemoveFolder(string folder) =>
        SetFolders(Folders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)).ToList());


    /// <summary>Looks through the folders again now.</summary>
    public void Rescan()
    {
        if (DemoTracks is not null)
        {
            return;
        }

        var token = _lifetime.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await LoadAsync(token).ConfigureAwait(false);
                    await Library.ScanAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            },
            token);
    }

    /// <summary>The row cover for a local song, or null. Never throws.</summary>
    public async Task<byte[]?> GetThumbnailAsync(TrackInfo track)
    {
        if (Covers is null || track.FilePath is not { } path || Library.Find(path) is not { } file)
        {
            return null;
        }

        try
        {
            return await Covers.GetAsync(file).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static TrackInfo ToTrack(LocalFile file) => Tracks.GetValue(file, f => f.ToTrackInfo());

    public void Dispose()
    {
        _lifetime.Cancel();
        Library.Dispose();
        Covers?.Dispose();
    }

    private void SetFolders(List<string> folders)
    {
        _settings.LocalFolders = folders;
        _saveSettings();
        ApplyFolders();
    }

    private Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _foldersSet, 1) == 0)
        {
            Library.SetFolders(Folders);
        }

        return Library.LoadAsync(cancellationToken);
    }

    /// <summary>Files outside the folders leave the list at once; a scan finds the new folders' files.</summary>
    private void ApplyFolders()
    {
        if (DemoTracks is not null)
        {
            return;
        }

        Interlocked.Exchange(ref _foldersSet, 1);
        var folders = Folders;
        _ = Task.Run(() =>
        {
            Library.SetFolders(folders);
            Rescan();
        });
    }
}

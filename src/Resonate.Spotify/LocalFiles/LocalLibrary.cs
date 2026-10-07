using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Text.Json;

namespace Resonate.Spotify.LocalFiles;

/// <summary>Where a scan is.</summary>
/// <param name="IsScanning">Looking through the folders now.</param>
/// <param name="Found">Music files found so far.</param>
/// <param name="ToRead">New or changed files whose tags are being read.</param>
/// <param name="Read">How many of those are done.</param>
/// <param name="HasScanned">At least one scan finished since Resonate started.</param>
public sealed record LocalScanStatus(bool IsScanning, int Found, int ToRead, int Read, bool HasScanned)
{
    public static readonly LocalScanStatus NotStarted = new(false, 0, 0, 0, false);
}

/// <param name="FilesChanged">The list of files changed (not only the scan's progress).</param>
public sealed record LocalLibraryChange(bool FilesChanged);

/// <summary>
/// The user's own music files: the folders to look in, an index of every
/// file found there, and scans that only read files that are new or changed
/// (by size and last-change time). The index is a JSON file, so the list is
/// there at once on the next launch. Spotify's own folders are never looked
/// into, even inside a chosen folder. Scans run on background threads.
/// </summary>
public sealed class LocalLibrary : IDisposable
{
    /// <summary>Raise when the tag reader learns something new, so every file is read again once.</summary>
    internal const int IndexVersion = 1;

    internal static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan WatchDelay = TimeSpan.FromSeconds(3);

    private const string SpotifyStorePackage = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string? _indexFile;
    private readonly IReadOnlyList<string> _excluded;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Lock _gate = new();

    // Changed under the lock, but always replaced whole, so the interface
    // reads them without waiting for a scan.
    private volatile Dictionary<string, LocalFile> _byPath = new(PathComparer);
    private volatile LocalFile[] _files = [];
    private volatile IReadOnlyList<string> _folders = [];
    private volatile LocalScanStatus _status = LocalScanStatus.NotStarted;

    private List<FileSystemWatcher> _watchers = [];
    private long _watchGeneration;
    private Task? _loading;
    private ITimer? _watchTimer;
    private bool _watching;
    private bool _disposed;
    private long _version;

    /// <param name="indexFile">Where the index is kept; null keeps it in memory only.</param>
    /// <param name="excludedFolders">Folders never looked into; Spotify's own when null.</param>
    public LocalLibrary(string? indexFile, IReadOnlyList<string>? excludedFolders = null, TimeProvider? time = null)
    {
        _indexFile = indexFile;
        _excluded = (excludedFolders ?? SpotifyFolders()).Select(Normalize).ToList();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on a background thread when the files or the scan's progress change.</summary>
    public event EventHandler<LocalLibraryChange>? Changed;

    /// <summary>Every file in the folders, newest first.</summary>
    public IReadOnlyList<LocalFile> Files => _files;

    public IReadOnlyList<string> Folders => _folders;

    public LocalScanStatus Status => _status;

    /// <summary>Goes up by one every time <see cref="Files"/> changes.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Whether a folder is there to watch (tests stand in for a network folder that answers slowly).</summary>
    internal Func<string, bool> CanWatch { get; init; } = Directory.Exists;

    /// <summary>The user's Music folder and Downloads, where Spotify looks by default.</summary>
    /// <param name="downloadsFolder">The Downloads folder as Windows knows it (it can be moved); the profile's "Downloads" when null.</param>
    public static IReadOnlyList<string> DefaultFolders(string? downloadsFolder = null)
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var downloads = downloadsFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return new[] { music, downloads }.Where(f => !string.IsNullOrEmpty(f)).Distinct(PathComparer).ToList();
    }

    /// <summary>
    /// The Spotify app's own folders (installer and Microsoft Store
    /// versions). Resonate never reads, scans or plays anything in them.
    /// </summary>
    public static IReadOnlyList<string> SpotifyFolders()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            string.IsNullOrEmpty(roaming) ? null : Path.Combine(roaming, "Spotify"),
            string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Spotify"),
            string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Packages", SpotifyStorePackage),
        }.OfType<string>().ToList();
    }

    /// <summary>True for Spotify's folders and anything inside them, wherever the Store package sits.</summary>
    public bool IsExcluded(string path) => IsExcluded(Normalize(path), _excluded);

    /// <summary>
    /// True for anything in the Spotify app's own folders on this computer.
    /// The local files player refuses those files, whatever list they came from.
    /// </summary>
    public static bool IsInSpotifyFolder(string path) => IsExcluded(Normalize(path), SpotifyFolders().Select(Normalize));

    private static bool IsExcluded(string full, IEnumerable<string> excluded)
    {
        foreach (var folder in excluded)
        {
            if (IsSameOrInside(full, folder))
            {
                return true;
            }
        }

        foreach (var part in full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the index from disk (once), so the list shows before any scan.</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _loading ??= Task.Run(LoadIndex, CancellationToken.None);
            return _loading.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Chooses the folders to look in. Files outside them leave the list at once; call <see cref="ScanAsync"/> to find new ones.</summary>
    public void SetFolders(IEnumerable<string> folders)
    {
        var normalized = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(Normalize)
            .Distinct(PathComparer)
            .ToList();
        bool changed;
        bool watching;
        lock (_gate)
        {
            if (normalized.SequenceEqual(_folders, PathComparer))
            {
                return;
            }

            _folders = normalized;
            changed = Publish(_byPath.Values);
            watching = _watching;
        }

        Changed?.Invoke(this, new LocalLibraryChange(changed));
        if (watching)
        {
            RestartWatchers();
        }
    }

    /// <summary>The file at <paramref name="path"/>, when it is in the list.</summary>
    public LocalFile? Find(string path)
    {
        var folders = _folders;
        return _byPath.TryGetValue(path, out var file) && IsInFolders(file.Path, folders) ? file : null;
    }

    /// <summary>
    /// Looks through the folders: new and changed files have their tags
    /// read (four at a time), files that are gone leave the list, and the
    /// rest is taken from the index. One scan runs at a time.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => Scan(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>Scans again a few seconds after files in the folders change (a download finishing, a folder copied in).</summary>
    public void StartWatching()
    {
        lock (_gate)
        {
            if (_watching || _disposed)
            {
                return;
            }

            _watching = true;
        }

        RestartWatchers();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watching = false;
            _watchTimer?.Dispose();
        }

        RestartWatchers();
    }

    private void Scan(CancellationToken cancellationToken)
    {
        string[] folders;
        Dictionary<string, LocalFile> known;
        bool hasScanned;
        lock (_gate)
        {
            folders = [.. _folders];
            known = _byPath;
            hasScanned = _status.HasScanned;
        }

        SetStatus(new LocalScanStatus(true, 0, 0, 0, hasScanned));
        var found = FindFiles(folders, cancellationToken);

        var kept = new List<LocalFile>(found.Count);
        var toRead = new List<(FoundFile File, DateTimeOffset? AddedAt)>();
        foreach (var file in found)
        {
            if (known.TryGetValue(file.Path, out var entry) && entry.Size == file.Size && entry.LastWriteTicks == file.LastWriteTicks)
            {
                kept.Add(entry);
            }
            else
            {
                toRead.Add((file, entry?.AddedAt));
            }
        }

        // Files outside the folders stay in the index, so removing a folder
        // by mistake and adding it back keeps their dates.
        var outside = known.Values.Where(f => !IsInFolders(f.Path, folders)).ToList();
        var removed = known.Count - outside.Count - kept.Count;
        SetStatus(new LocalScanStatus(true, found.Count, toRead.Count, 0, hasScanned));

        // Only a first scan shows songs as it goes; a rescan would make
        // changed files blink out until they are read again.
        var firstScan = known.Count == 0;
        var read = new ConcurrentBag<LocalFile>();
        var readCount = 0;
        var lastPublish = _time.GetUtcNow();
        var now = _time.GetUtcNow();
        Parallel.ForEach(
            toRead,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            item =>
            {
                read.Add(ReadFile(item.File, item.AddedAt ?? FirstSeen(item.File, now)));
                var done = Interlocked.Increment(ref readCount);
                if (done % 25 != 0)
                {
                    return;
                }

                SetStatus(new LocalScanStatus(true, found.Count, toRead.Count, done, hasScanned), raise: false);

                var publish = false;
                lock (_gate)
                {
                    if (firstScan && _time.GetUtcNow() - lastPublish >= PublishInterval)
                    {
                        lastPublish = _time.GetUtcNow();
                        publish = true;
                        _ = Publish([.. outside, .. kept, .. read]);
                    }
                }

                Changed?.Invoke(this, new LocalLibraryChange(publish));
            });

        bool changed;
        lock (_gate)
        {
            changed = Publish([.. outside, .. kept, .. read]) | removed > 0 | toRead.Count > 0;
            _status = new LocalScanStatus(false, found.Count, toRead.Count, toRead.Count, true);
        }

        if (changed)
        {
            SaveIndex();
        }

        Changed?.Invoke(this, new LocalLibraryChange(changed));
    }

    /// <summary>Every music file under the folders, once, skipping hidden and system folders and Spotify's.</summary>
    private List<FoundFile> FindFiles(string[] folders, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(PathComparer);
        var found = new List<FoundFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder) || IsExcluded(folder))
            {
                continue;
            }

            var files = new FileSystemEnumerable<FoundFile>(
                folder,
                (ref FileSystemEntry entry) => new FoundFile(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcTicks, entry.CreationTimeUtc),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && TagReader.IsSupported(entry.FileName),

                // Links can loop back up the tree; Spotify's folders are off limits.
                // Folders synced by OneDrive and other cloud services are
                // reparse points too, but not links, so they are looked into
                // (reading the tags of an online-only file downloads it).
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                    && ((entry.Attributes & FileAttributes.ReparsePoint) == 0 || !IsLink(entry.ToFullPath()))
                    && !IsExcluded(entry.ToFullPath()),
            };

            try
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (seen.Add(file.Path))
                    {
                        found.Add(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The folder went away or locked mid-scan; the next scan tries again.
            }
        }

        return found;
    }

    /// <summary>True for symbolic links and junctions, and for folders that can not be checked.</summary>
    private static bool IsLink(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static LocalFile ReadFile(FoundFile file, DateTimeOffset addedAt)
    {
        try
        {
            using var stream = TagReader.OpenRead(file.Path);
            return LocalFile.From(file.Path, file.Size, file.LastWriteTicks, addedAt, TagReader.Read(stream, file.Path));
        }
        catch (Exception)
        {
            // Locked, unreadable or gone: listed by its name, with no change
            // time, so the next scan reads it again.
            var tags = AudioTags.Empty with { Title = Path.GetFileNameWithoutExtension(file.Path) };
            return LocalFile.From(file.Path, file.Size, lastWriteTicks: 0, addedAt, tags);
        }
    }

    /// <summary>
    /// "Date added" for a file seen for the first time: now, or when it was
    /// put in its folder (its creation time) if that is earlier, so a first
    /// scan keeps the order the music arrived in.
    /// </summary>
    private static DateTimeOffset FirstSeen(FoundFile file, DateTimeOffset now)
    {
        var created = file.CreationTimeUtc;
        return created.Year >= 1990 && created < now ? created : now;
    }

    /// <summary>Replaces the files (call under the lock); true when the list shown changed.</summary>
    private bool Publish(IEnumerable<LocalFile> all)
    {
        var byPath = new Dictionary<string, LocalFile>(PathComparer);
        foreach (var file in all)
        {
            byPath[file.Path] = file;
        }

        var shown = byPath.Values
            .Where(f => IsInFolders(f.Path, _folders))
            .OrderByDescending(f => f.AddedAt)
            .ThenBy(f => f.Path, PathComparer)
            .ToArray();
        var changed = shown.Length != _files.Length || !shown.SequenceEqual(_files);
        _byPath = byPath;
        if (changed)
        {
            _files = shown;
            Interlocked.Increment(ref _version);
        }

        return changed;
    }

    private void SetStatus(LocalScanStatus status, bool raise = true)
    {
        lock (_gate)
        {
            _status = status;
        }

        if (raise)
        {
            Changed?.Invoke(this, new LocalLibraryChange(false));
        }
    }

    private void LoadIndex()
    {
        if (_indexFile is null || !File.Exists(_indexFile))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_indexFile);
            var index = JsonSerializer.Deserialize(stream, LocalFilesJsonContext.Default.LocalIndex);
            if (index is null || index.Version != IndexVersion)
            {
                return;
            }

            bool changed;
            lock (_gate)
            {
                // A scan that finished first knows better.
                if (_byPath.Count > 0)
                {
                    return;
                }

                changed = Publish(index.Files.Where(f => !string.IsNullOrEmpty(f.Path)));
            }

            Changed?.Invoke(this, new LocalLibraryChange(changed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged index is rebuilt by the next scan.
        }
    }

    private void SaveIndex()
    {
        if (_indexFile is null)
        {
            return;
        }

        LocalIndex index;
        lock (_gate)
        {
            index = new LocalIndex { Version = IndexVersion, Files = [.. _byPath.Values] };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_indexFile)!);
            var temporary = _indexFile + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, index, LocalFilesJsonContext.Default.LocalIndex);
            }

            File.Move(temporary, _indexFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the next scan writes it again.
        }
    }

    // ---- Watching ----

    /// <summary>
    /// Watches the folders chosen now, and stops watching the others. Call
    /// outside the lock: a network folder that is asleep or gone can take
    /// many seconds to answer, and the list must not wait for it.
    /// </summary>
    private void RestartWatchers()
    {
        long generation;
        IReadOnlyList<string> folders;
        lock (_gate)
        {
            generation = ++_watchGeneration;
            folders = _watching ? _folders : [];
        }

        var started = new List<FileSystemWatcher>();
        foreach (var folder in folders)
        {
            if (IsExcluded(folder) || !CanWatch(folder))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += (_, _) => ScheduleRescan();
                watcher.EnableRaisingEvents = true;
                started.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Without a watcher the folder is still scanned at start-up and on request.
            }
        }

        // Folders changed again (or the library closed) meanwhile: the
        // newer call's watchers win.
        List<FileSystemWatcher> stopped;
        lock (_gate)
        {
            if (generation == _watchGeneration)
            {
                stopped = _watchers;
                _watchers = started;
            }
            else
            {
                stopped = started;
            }
        }

        foreach (var watcher in stopped)
        {
            watcher.Dispose();
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(e.FullPath))
        {
            ScheduleRescan();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevant(e.FullPath) || IsRelevant(e.OldFullPath))
        {
            ScheduleRescan();
        }
    }

    /// <summary>Music files, and folders (which have no extension, usually), outside Spotify's folders.</summary>
    private bool IsRelevant(string path) =>
        (TagReader.IsSupported(path) || !Path.HasExtension(path)) && !IsExcluded(path);

    private void ScheduleRescan()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Every change pushes the scan back, so a burst of changes scans once.
            _watchTimer ??= _time.CreateTimer(_ => _ = RescanAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watchTimer.Change(WatchDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RescanAsync()
    {
        try
        {
            await ScanAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The next change or launch scans again.
        }
    }

    // ---- Paths ----

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    private static bool IsSameOrInside(string path, string folder) =>
        path.Equals(folder, PathComparison)
        || (path.StartsWith(folder, PathComparison)
            && (folder.EndsWith(Path.DirectorySeparatorChar) || path[folder.Length] == Path.DirectorySeparatorChar || path[folder.Length] == Path.AltDirectorySeparatorChar));

    private static bool IsInFolders(string path, IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            if (IsSameOrInside(path, folder))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct FoundFile(string Path, long Size, long LastWriteTicks, DateTimeOffset CreationTimeUtc);
}

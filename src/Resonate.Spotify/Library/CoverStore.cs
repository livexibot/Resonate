using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Resonate.Spotify.Library;

/// <summary>
/// Spotify's cover pictures as bytes, fetched once and then kept. A picture
/// comes from memory when it was used lately, else from a folder on disk
/// (a Spotify picture's address never changes what it shows, so a kept copy
/// is always right), else from Spotify's picture servers. Downloads share
/// one HTTP/2 connection. Covers being shown go first, in the order they
/// were asked for (a list asks for the rows on screen before the ones just
/// below); covers fetched ahead (<see cref="Prefetch"/>, for example while
/// the pointer rests on a playlist) only use what is left, and move to the
/// front when they are shown. Only Spotify's own picture hosts are asked;
/// anything else is left to the caller.
/// </summary>
public sealed class CoverStore : IDisposable
{
    /// <summary>How many downloads run at once (streams on one HTTP/2 connection).</summary>
    public const int MaxDownloads = 24;

    /// <summary>How many of those may be covers fetched ahead, which start only while no shown cover waits.</summary>
    public const int MaxPrefetchDownloads = 6;

    /// <summary>Larger pictures are not kept (Spotify's largest cover is about 640 × 640, a few hundred KB).</summary>
    public const int MaxPictureBytes = 4 * 1024 * 1024;

    /// <summary>How many pictures one call to <see cref="Prefetch"/> starts at most.</summary>
    public const int MaxPrefetch = 60;

    private const string Extension = ".img";

    // Hosts Spotify serves covers, artist pictures and playlist mosaics from.
    private static readonly string[] PictureHosts = [".scdn.co", ".spotifycdn.com"];

    private readonly HttpClient? _http;
    private readonly string? _folder;
    private readonly long _memoryBudget;
    private readonly long _diskBudget;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Url, byte[] Bytes)>> _memory = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Url, byte[] Bytes)> _recent = new();
    private readonly Dictionary<string, Task<byte[]?>> _loading = new(StringComparer.Ordinal);

    // Downloads waiting for a slot: covers being shown, and covers fetched ahead. Each in order.
    private readonly LinkedList<Download> _shown = new();
    private readonly LinkedList<Download> _ahead = new();
    private readonly Dictionary<string, LinkedListNode<Download>> _queued = new(StringComparer.Ordinal);
    // Covers fetched ahead that were shown while still being looked for on disk.
    private readonly HashSet<string> _shownWhileLoading = new(StringComparer.Ordinal);
    private long _memoryBytes;
    private int _downloading;
    private int _prefetching;

    /// <param name="http">Downloads the pictures; null keeps only what is already on disk.</param>
    /// <param name="folder">Where pictures are kept between launches; null keeps them in memory only.</param>
    public CoverStore(HttpClient? http, string? folder, long memoryBudget = 24L * 1024 * 1024, long diskBudget = 200L * 1024 * 1024, TimeProvider? time = null)
    {
        _http = http;
        _folder = folder;
        _memoryBudget = memoryBudget;
        _diskBudget = diskBudget;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Whether this store fetches <paramref name="url"/> (an https address on one of Spotify's picture hosts).</summary>
    public static bool Handles(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && Array.Exists(PictureHosts, h => uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>The picture if it is in memory now, without waiting; null otherwise.</summary>
    public byte[]? TryGetRecent(string url)
    {
        lock (_gate)
        {
            if (!_memory.TryGetValue(url, out var node))
            {
                return null;
            }

            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Bytes;
        }
    }

    /// <summary>
    /// The picture's bytes from memory, disk or Spotify; null when it can not
    /// be had (offline, not one of Spotify's pictures, or shutting down).
    /// Never throws. Several calls for the same picture share one fetch.
    /// </summary>
    public Task<byte[]?> GetAsync(string url) => GetAsync(url, ahead: false);

    /// <summary>
    /// Fetches pictures ahead of need, in the background: the first
    /// <see cref="MaxPrefetch"/> that are not in memory yet. Pictures already
    /// on disk cost a file read; nothing is fetched twice, and covers being
    /// shown are never kept waiting by these.
    /// </summary>
    public void Prefetch(IEnumerable<string?> urls)
    {
        var started = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in urls)
        {
            if (started >= MaxPrefetch)
            {
                break;
            }

            if (url is null || !seen.Add(url) || !Handles(url))
            {
                continue;
            }

            lock (_gate)
            {
                if (_memory.ContainsKey(url) || _loading.ContainsKey(url))
                {
                    continue;
                }
            }

            started++;
            _ = GetAsync(url, ahead: true);
        }
    }

    /// <summary>Drops a picture that turned out to be damaged, so it is fetched again next time.</summary>
    public void Forget(string url)
    {
        lock (_gate)
        {
            if (_memory.Remove(url, out var node))
            {
                _recent.Remove(node);
                _memoryBytes -= node.Value.Bytes.Length;
            }
        }

        if (PathFor(url) is { } path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Replaced when it is next downloaded.
            }
        }
    }

    /// <summary>
    /// Keeps the folder within its budget by removing the pictures used
    /// least lately. Reads the folder, so call it off the interface thread.
    /// </summary>
    public void Prune()
    {
        if (_folder is null || !Directory.Exists(_folder))
        {
            return;
        }

        try
        {
            var files = new DirectoryInfo(_folder).EnumerateFiles("*" + Extension).ToList();
            var total = files.Sum(f => f.Length);
            if (total <= _diskBudget)
            {
                return;
            }

            // Down to three quarters, so pruning does not run on every start.
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= _diskBudget * 3 / 4)
                {
                    break;
                }

                total -= file.Length;
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again next start.
        }
    }

    /// <summary>
    /// Forgets every picture, in memory and on disk (signing out: covers say
    /// what someone listens to). Reads the folder, so call it off the interface thread.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _memory.Clear();
            _recent.Clear();
            _memoryBytes = 0;
        }

        if (_folder is null || !Directory.Exists(_folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder, "*" + Extension))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruned away in time.
        }
    }

    /// <summary>Stops downloading; waiting callers get null. (The token source is left undisposed, as downloads may still hold it.)</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        List<Download> waiting;
        lock (_gate)
        {
            waiting = [.. _shown, .. _ahead];
            _shown.Clear();
            _ahead.Clear();
            _queued.Clear();
        }

        foreach (var download in waiting)
        {
            download.Done.TrySetResult(null);
        }
    }

    /// <summary>Downloads waiting for a slot (for tests).</summary>
    internal int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _shown.Count + _ahead.Count;
            }
        }
    }

    /// <summary>The name a picture is kept under: a hash of its address.</summary>
    internal string? PathFor(string url) =>
        _folder is null
            ? null
            : Path.Combine(_folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)).AsSpan(0, 16)) + Extension);

    private Task<byte[]?> GetAsync(string url, bool ahead)
    {
        if (TryGetRecent(url) is { } recent)
        {
            return Task.FromResult<byte[]?>(recent);
        }

        if (!Handles(url) || _lifetime.IsCancellationRequested)
        {
            return Task.FromResult<byte[]?>(null);
        }

        lock (_gate)
        {
            if (_loading.TryGetValue(url, out var loading))
            {
                if (!ahead)
                {
                    MoveToShown(url);
                }

                return loading;
            }

            var task = LoadAsync(url, ahead);
            if (!task.IsCompleted)
            {
                _loading[url] = task;
            }

            return task;
        }
    }

    private async Task<byte[]?> LoadAsync(string url, bool ahead)
    {
        try
        {
            // Off the caller's thread: the interface asks, and disk and network answer.
            var bytes = await Task.Run(() => ReadKept(url)).ConfigureAwait(false)
                ?? await DownloadAsync(url, ahead).ConfigureAwait(false);
            if (bytes is not null)
            {
                Remember(url, bytes);
            }

            return bytes;
        }
        finally
        {
            lock (_gate)
            {
                _loading.Remove(url);
                _shownWhileLoading.Remove(url);
            }
        }
    }

    private byte[]? ReadKept(string url)
    {
        if (PathFor(url) is not { } path)
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is 0 or > MaxPictureBytes)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);

            // Pictures in use stay young, so pruning removes the ones nobody sees.
            var now = _time.GetUtcNow().UtcDateTime;
            if (now - file.LastWriteTimeUtc > TimeSpan.FromDays(7))
            {
                File.SetLastWriteTimeUtc(path, now);
            }

            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Waits for a download slot (shown covers first, each lane in order) and downloads.</summary>
    private Task<byte[]?> DownloadAsync(string url, bool ahead)
    {
        if (_http is null)
        {
            return Task.FromResult<byte[]?>(null);
        }

        var download = new Download(url, ahead);
        lock (_gate)
        {
            if (_lifetime.IsCancellationRequested)
            {
                return Task.FromResult<byte[]?>(null);
            }

            // Shown while it was being looked for on disk.
            if (_shownWhileLoading.Remove(url))
            {
                download.Ahead = false;
            }

            _queued[url] = (download.Ahead ? _ahead : _shown).AddLast(download);
        }

        StartDownloads();
        return download.Done.Task;
    }

    /// <summary>A cover fetched ahead is being shown now: it goes to the back of the shown covers' line.</summary>
    private void MoveToShown(string url)
    {
        if (!_queued.TryGetValue(url, out var node))
        {
            // Still being looked for on disk; if it has to be downloaded, it goes with the shown covers.
            _shownWhileLoading.Add(url);
            return;
        }

        if (node.List == _ahead)
        {
            _ahead.Remove(node);
            node.Value.Ahead = false;
            _shown.AddLast(node);
            StartDownloads();
        }
    }

    private void StartDownloads()
    {
        List<Download> starting = [];
        lock (_gate)
        {
            while (!_lifetime.IsCancellationRequested && _downloading < MaxDownloads)
            {
                LinkedListNode<Download>? next = _shown.First;
                if (next is null && _prefetching < MaxPrefetchDownloads)
                {
                    next = _ahead.First;
                }

                if (next is null)
                {
                    break;
                }

                next.List!.Remove(next);
                _queued.Remove(next.Value.Url);
                _downloading++;
                if (next.Value.Ahead)
                {
                    _prefetching++;
                }

                starting.Add(next.Value);
            }
        }

        foreach (var download in starting)
        {
            _ = Task.Run(() => RunAsync(download));
        }
    }

    private async Task RunAsync(Download download)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await FetchAsync(download.Url).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _downloading--;
                if (download.Ahead)
                {
                    _prefetching--;
                }
            }

            download.Done.TrySetResult(bytes);
            StartDownloads();
        }
    }

    private async Task<byte[]?> FetchAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            using var response = await _http!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength is > MaxPictureBytes
                || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == false)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(_lifetime.Token).ConfigureAwait(false);
            if (bytes.Length is 0 or > MaxPictureBytes)
            {
                return null;
            }

            Keep(url, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Offline, slow or shutting down: the colour tile stays, and the next visit tries again.
            return null;
        }
    }

    /// <summary>Writes a downloaded picture to the folder (whole or not at all).</summary>
    private void Keep(string url, byte[] bytes)
    {
        if (PathFor(url) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_folder!);
            var temporary = path + "." + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory this time; downloaded again next launch.
        }
    }

    private void Remember(string url, byte[] bytes)
    {
        lock (_gate)
        {
            if (_memory.ContainsKey(url))
            {
                return;
            }

            _memory[url] = _recent.AddFirst((url, bytes));
            _memoryBytes += bytes.Length;
            while (_memoryBytes > _memoryBudget && _recent.Last is { } oldest && oldest != _recent.First)
            {
                _recent.RemoveLast();
                _memory.Remove(oldest.Value.Url);
                _memoryBytes -= oldest.Value.Bytes.Length;
            }
        }
    }

    private sealed class Download(string url, bool ahead)
    {
        public string Url { get; } = url;

        /// <summary>Fetched ahead (until it is shown); counted against <see cref="MaxPrefetchDownloads"/> while it downloads.</summary>
        public bool Ahead { get; set; } = ahead;

        public TaskCompletionSource<byte[]?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

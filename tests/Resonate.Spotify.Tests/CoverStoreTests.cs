using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Tests;

public sealed class CoverStoreTests : IDisposable
{
    private const string Cover = "https://i.scdn.co/image/ab67616d00004851aaaa";
    private const string OtherCover = "https://i.scdn.co/image/ab67616d00004851bbbb";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "resonate-covers-" + Guid.NewGuid().ToString("N"));
    private readonly PictureServer _server = new();

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Downloads_a_cover_once_and_then_answers_from_memory()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        var first = await store.GetAsync(Cover);
        var second = await store.GetAsync(Cover);

        Assert.Equal(PictureServer.BytesFor(Cover), first);
        Assert.Same(first, second);
        Assert.Same(first, store.TryGetRecent(Cover));
        Assert.Equal(1, _server.Count(Cover));
    }

    [Fact]
    public async Task Asks_for_HTTP_2_so_covers_share_one_connection()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        await store.GetAsync(Cover);

        Assert.Equal(HttpVersion.Version20, _server.Versions.Single());
    }

    [Fact]
    public async Task Requests_for_the_same_cover_at_once_share_one_download()
    {
        var gate = new TaskCompletionSource();
        _server.Gate = gate.Task;
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        var a = store.GetAsync(Cover);
        var b = store.GetAsync(Cover);
        gate.SetResult();

        Assert.Same(await a, await b);
        Assert.Equal(1, _server.Count(Cover));
    }

    [Fact]
    public async Task Keeps_covers_on_disk_for_the_next_launch()
    {
        using (var http = new HttpClient(_server))
        using (var store = new CoverStore(http, _folder))
        {
            await store.GetAsync(Cover);
        }

        using var offline = new CoverStore(http: null, _folder);
        Assert.Equal(PictureServer.BytesFor(Cover), await offline.GetAsync(Cover));
        Assert.Equal(1, _server.Count(Cover));
    }

    [Fact]
    public async Task Only_asks_Spotify_s_picture_hosts()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        Assert.Null(await store.GetAsync("https://example.com/cover.jpg"));
        Assert.Null(await store.GetAsync("http://i.scdn.co/image/plain-http"));
        Assert.True(CoverStore.Handles("https://mosaic.scdn.co/640/abc"));
        Assert.True(CoverStore.Handles("https://image-cdn-ak.spotifycdn.com/image/abc"));
        Assert.False(CoverStore.Handles("https://scdn.co.example.com/image/abc"));
        Assert.Empty(_server.Asked);
    }

    [Fact]
    public async Task A_failed_download_is_null_and_tried_again_next_time()
    {
        _server.Fail = true;
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        Assert.Null(await store.GetAsync(Cover));

        _server.Fail = false;
        Assert.Equal(PictureServer.BytesFor(Cover), await store.GetAsync(Cover));
        Assert.Equal(2, _server.Count(Cover));
    }

    [Fact]
    public async Task Refuses_answers_that_are_not_pictures()
    {
        _server.ContentType = "text/html";
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        Assert.Null(await store.GetAsync(Cover));
        Assert.Null(store.PathFor(Cover) is { } path && File.Exists(path) ? path : null);
    }

    [Fact]
    public async Task Shown_covers_download_in_the_order_they_were_asked_for_before_covers_fetched_ahead()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, folder: null);

        // Every slot busy; then a cover fetched ahead, and two shown ones (a moment apart, so their order is clear).
        const string Ahead = Cover + "-ahead";
        var busy = Enumerable.Range(0, CoverStore.MaxDownloads).Select(i => $"{Cover}{i}").ToList();
        List<string> all = [.. busy, Ahead, Cover, OtherCover];
        all.ForEach(_server.Hold);
        var downloads = busy.Select(store.GetAsync).ToList();
        await _server.WaitForAsync(CoverStore.MaxDownloads);

        store.Prefetch([Ahead]);
        await Until(() => store.Waiting == 1);
        var first = store.GetAsync(Cover);
        await Until(() => store.Waiting == 2);
        var second = store.GetAsync(OtherCover);
        await Until(() => store.Waiting == 3);
        Assert.Equal(CoverStore.MaxDownloads, _server.Asked.Count());

        // One slot at a time: shown covers first, in order, then the one fetched ahead.
        foreach (var (url, expected) in new[] { (busy[0], Cover), (busy[1], OtherCover), (busy[2], Ahead) })
        {
            var asked = _server.Asked.Count();
            _server.Release(url);
            await _server.WaitForAsync(asked + 1);
            Assert.Equal(expected, _server.Asked.Last());
        }

        all.ForEach(_server.Release);
        await Task.WhenAll([.. downloads, first, second]);
    }

    [Fact]
    public async Task Covers_fetched_ahead_leave_room_and_move_up_when_shown()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, folder: null);
        var ahead = Enumerable.Range(0, CoverStore.MaxPrefetchDownloads + 3).Select(i => $"{Cover}{i}").ToList();
        ahead.ForEach(_server.Hold);

        store.Prefetch(ahead);
        await _server.WaitForAsync(CoverStore.MaxPrefetchDownloads);
        await Until(() => store.Waiting == ahead.Count - CoverStore.MaxPrefetchDownloads);
        Assert.Equal(CoverStore.MaxPrefetchDownloads, _server.Asked.Count());

        // Shown now: it starts at once, ahead of the others still waiting.
        // (Which ones wait depends on which looked on disk first.)
        var waiting = ahead.Where(url => !_server.Asked.Contains(url)).ToList();
        var shown = store.GetAsync(waiting[^1]);
        await _server.WaitForAsync(CoverStore.MaxPrefetchDownloads + 1);
        Assert.Equal(waiting[^1], _server.Asked.Last());
        Assert.DoesNotContain(waiting[0], _server.Asked);

        // And a shown cover never waits for covers fetched ahead.
        var other = store.GetAsync(OtherCover);
        Assert.Equal(PictureServer.BytesFor(OtherCover), await other);

        ahead.ForEach(_server.Release);
        Assert.Equal(PictureServer.BytesFor(waiting[^1]), await shown);
    }

    [Fact]
    public async Task Prefetch_fetches_ahead_without_asking_twice()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);

        store.Prefetch([Cover, Cover, null, "https://example.com/x.jpg", OtherCover]);
        await _server.WaitForAsync(2);
        await store.GetAsync(Cover);
        await store.GetAsync(OtherCover);

        Assert.Equal(1, _server.Count(Cover));
        Assert.Equal(1, _server.Count(OtherCover));
    }

    [Fact]
    public async Task Memory_stays_within_its_budget()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, folder: null, memoryBudget: PictureServer.Size * 2);

        await store.GetAsync(Cover);
        await store.GetAsync(OtherCover);
        await store.GetAsync(Cover + "c");

        Assert.Null(store.TryGetRecent(Cover));
        Assert.NotNull(store.TryGetRecent(OtherCover));
        Assert.NotNull(store.TryGetRecent(Cover + "c"));
    }

    [Fact]
    public async Task Pruning_removes_the_pictures_used_least_lately()
    {
        using var http = new HttpClient(_server);
        using (var store = new CoverStore(http, _folder))
        {
            await store.GetAsync(Cover);
            await store.GetAsync(OtherCover);
        }

        using var small = new CoverStore(http, _folder, diskBudget: PictureServer.Size * 3 / 2);
        File.SetLastWriteTimeUtc(small.PathFor(Cover)!, DateTime.UtcNow.AddDays(-30));
        small.Prune();

        Assert.False(File.Exists(small.PathFor(Cover)));
        Assert.True(File.Exists(small.PathFor(OtherCover)));
    }

    [Fact]
    public async Task Forget_drops_a_damaged_picture()
    {
        using var http = new HttpClient(_server);
        using var store = new CoverStore(http, _folder);
        await store.GetAsync(Cover);

        store.Forget(Cover);

        Assert.Null(store.TryGetRecent(Cover));
        Assert.False(File.Exists(store.PathFor(Cover)));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>Answers every request with a made-up picture of <see cref="Size"/> bytes and records what was asked.</summary>
    private sealed class PictureServer : HttpMessageHandler
    {
        public const int Size = 1000;

        private readonly ConcurrentQueue<string> _asked = new();
        private readonly ConcurrentQueue<Version> _versions = new();

        private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new();

        public Task? Gate { get; set; }

        public bool Fail { get; set; }

        public string ContentType { get; set; } = "image/jpeg";

        public IEnumerable<string> Asked => _asked;

        public IEnumerable<Version> Versions => _versions;

        public static byte[] BytesFor(string url) => [.. Enumerable.Range(0, Size).Select(i => (byte)(url.Length + i))];

        public int Count(string url) => _asked.Count(u => u == url);

        /// <summary>Requests for <paramref name="url"/> wait until it is released.</summary>
        public void Hold(string url) => _held[url] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string url)
        {
            if (_held.TryGetValue(url, out var held))
            {
                held.TrySetResult();
            }
        }

        public async Task WaitForAsync(int requests)
        {
            for (var i = 0; i < 500 && _asked.Count < requests; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            _asked.Enqueue(url);
            _versions.Enqueue(request.Version);
            if (Gate is { } gate)
            {
                await gate.WaitAsync(cancellationToken);
            }

            if (_held.TryGetValue(url, out var held))
            {
                await held.Task.WaitAsync(cancellationToken);
            }

            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            var content = new ByteArrayContent(BytesFor(url));
            content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}

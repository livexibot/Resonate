using Resonate.Spotify.LocalFiles;
using static Resonate.Spotify.Tests.Fakes.AudioFiles;

namespace Resonate.Spotify.Tests;

public sealed class LocalCoverCacheTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("resonate-covers-");
    private readonly string _album;
    private readonly string _cache;
    private readonly FakeShrinker _shrinker = new();

    public LocalCoverCacheTests()
    {
        _album = Path.Combine(_root.FullName, "Album");
        _cache = Path.Combine(_root.FullName, "cache");
        Directory.CreateDirectory(_album);
    }

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task A_cover_inside_the_file_is_shrunk_once_and_then_kept()
    {
        var song = Song("embedded.mp3", Png);

        Assert.Equal(FakeShrinker.Thumbnail(Png), await Cache().GetAsync(song));
        Assert.Equal(FakeShrinker.Thumbnail(Png), await Cache().GetAsync(song));
        Assert.Equal(1, _shrinker.Calls);
    }

    [Fact]
    public void The_cover_is_read_from_where_the_scan_found_it()
    {
        var path = Path.Combine(_album, "known.mp3");
        var tag = Id3Tag(3, 0, Frame23("APIC", Apic("image/png", 3, string.Empty, Png)));
        File.WriteAllBytes(path, Concat(tag, MpegFrames(3)));
        var scanned = TagReader.Read(path).Cover!;
        var song = FileFor(path) with { CoverOffset = scanned.Offset, CoverLength = scanned.Length, CoverMime = scanned.MimeType, CoverEncoding = scanned.Encoding };

        Assert.True(LocalCovers.TryReadEmbedded(song, out var cover));
        Assert.Equal(Png, cover);

        // An index from before the file changed points at the wrong bytes: the tags are read again.
        Assert.True(LocalCovers.TryReadEmbedded(song with { CoverOffset = 0 }, out cover));
        Assert.Equal(Png, cover);
    }

    [Fact]
    public async Task Without_a_cover_inside_the_file_the_folder_picture_is_used_and_shared()
    {
        var first = Song("one.mp3", cover: null);
        var second = Song("two.mp3", cover: null);
        File.WriteAllBytes(Path.Combine(_album, "Folder.jpg"), Jpeg);
        var cache = Cache();

        Assert.Equal(FakeShrinker.Thumbnail(Jpeg), await cache.GetAsync(first));
        Assert.Equal(FakeShrinker.Thumbnail(Jpeg), await cache.GetAsync(second));
        Assert.Equal(1, _shrinker.Calls);
    }

    [Fact]
    public async Task A_new_folder_picture_shows_instead_of_the_kept_one()
    {
        var song = Song("one.mp3", cover: null);
        var picture = Path.Combine(_album, "cover.jpg");
        File.WriteAllBytes(picture, Jpeg);
        Assert.Equal(FakeShrinker.Thumbnail(Jpeg), await Cache().GetAsync(song));

        File.WriteAllBytes(picture, Concat(Jpeg, Jpeg));
        File.SetLastWriteTimeUtc(picture, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(FakeShrinker.Thumbnail(Concat(Jpeg, Jpeg)), await Cache().GetAsync(song));
    }

    [Fact]
    public async Task A_song_without_any_cover_is_not_read_again_until_its_folder_changes()
    {
        var song = Song("plain.mp3", cover: null);
        var touched = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(_album, touched);

        Assert.Null(await Cache().GetAsync(song));

        // A cover put there (the folder's time does not move on every file system, so it is moved here).
        File.WriteAllBytes(Path.Combine(_album, "cover.png"), Png);
        Directory.SetLastWriteTimeUtc(_album, touched);
        Assert.Null(await Cache().GetAsync(song));

        Directory.SetLastWriteTimeUtc(_album, touched.AddMinutes(1));
        Assert.Equal(FakeShrinker.Thumbnail(Png), await Cache().GetAsync(song));
    }

    [Fact]
    public async Task A_picture_Windows_could_not_read_once_is_tried_again()
    {
        var song = Song("embedded.mp3", Png);
        _shrinker.FailNext = 1;

        Assert.Null(await Cache().GetAsync(song));
        Assert.Equal(FakeShrinker.Thumbnail(Png), await Cache().GetAsync(song));
    }

    [Fact]
    public async Task A_folder_picture_Windows_could_not_read_once_is_tried_again()
    {
        var song = Song("plain.mp3", cover: null);
        File.WriteAllBytes(Path.Combine(_album, "cover.jpg"), Jpeg);
        _shrinker.FailNext = 1;
        var cache = Cache();

        Assert.Null(await cache.GetAsync(song));
        Assert.Equal(FakeShrinker.Thumbnail(Jpeg), await cache.GetAsync(song));
    }

    [Fact]
    public async Task Markers_from_an_older_version_are_ignored()
    {
        var song = Song("embedded.mp3", Png);

        // What version 1 left after failing on every cover: a marker under the old name.
        var oldKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{song.Path.ToUpperInvariant()}|{song.Size}|{song.LastWriteTicks}")).AsSpan(0, 16));
        Directory.CreateDirectory(_cache);
        File.WriteAllText(Path.Combine(_cache, oldKey + ".none"), Directory.GetLastWriteTimeUtc(_album).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.NotEqual(oldKey, LocalCoverCache.KeyFor(song));
        Assert.Equal(FakeShrinker.Thumbnail(Png), await Cache().GetAsync(song));
    }

    [Fact]
    public async Task A_file_that_can_not_be_read_leaves_no_marker()
    {
        var song = FileFor(Path.Combine(_album, "missing.mp3"));

        Assert.Null(await Cache().GetAsync(song));
        Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache, "*.none").Any());
    }

    private LocalCoverCache Cache() => new(_cache, _shrinker);

    private LocalFile Song(string name, byte[]? cover)
    {
        var path = Path.Combine(_album, name);
        var tag = cover is null
            ? Id3Tag(3, 0, Frame23("TIT2", Latin1Text(name)))
            : Id3Tag(3, 0, Frame23("APIC", Apic("image/png", 3, string.Empty, cover)));
        File.WriteAllBytes(path, Concat(tag, MpegFrames(3)));
        return FileFor(path);
    }

    private static LocalFile FileFor(string path)
    {
        var info = new FileInfo(path);
        return new LocalFile
        {
            Path = path,
            Size = info.Exists ? info.Length : 0,
            LastWriteTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
            Title = Path.GetFileNameWithoutExtension(path),
        };
    }

    /// <summary>Stands in for Windows' decoder: a "thumbnail" is the picture with a marker byte in front.</summary>
    private sealed class FakeShrinker : ICoverShrinker
    {
        public int Calls { get; private set; }

        public int FailNext { get; set; }

        public static byte[] Thumbnail(byte[] cover) => Concat([0x7E], cover);

        public Task<byte[]?> ShrinkAsync(byte[] cover, int size)
        {
            Assert.Equal(LocalCoverCache.ThumbnailSize, size);
            Calls++;
            if (FailNext > 0)
            {
                FailNext--;
                return Task.FromResult<byte[]?>(null);
            }

            return Task.FromResult<byte[]?>(Thumbnail(cover));
        }
    }
}

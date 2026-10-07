using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.LocalFiles;
using static Resonate.Spotify.Tests.Fakes.AudioFiles;

namespace Resonate.Spotify.Tests;

public sealed class LocalLibraryTests : IDisposable
{
    // Before the test files were made, so "date added" comes from this clock, not the files.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("resonate-local-");
    private readonly string _music;
    private readonly string _index;
    private readonly List<LocalLibrary> _libraries = [];

    public LocalLibraryTests()
    {
        _music = Path.Combine(_root.FullName, "Music");
        _index = Path.Combine(_root.FullName, "data", "local-files.json");
        Directory.CreateDirectory(_music);
    }

    public void Dispose()
    {
        foreach (var library in _libraries)
        {
            library.Dispose();
        }

        _root.Delete(recursive: true);
    }

    [Fact]
    public async Task A_scan_lists_the_music_files_in_every_subfolder()
    {
        WriteSong("Album/01.mp3", "First");
        WriteSong("Album/Disc 2/02.mp3", "Second");
        File.WriteAllBytes(Path.Combine(_music, "Album", "untagged song.mp3"), MpegFrames(3));
        File.WriteAllText(Path.Combine(_music, "Album", "notes.txt"), "not music");
        var library = Create();

        await library.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["First", "Second", "untagged song"], library.Files.Select(f => f.Title).Order(StringComparer.Ordinal));
        Assert.Equal(new LocalScanStatus(false, 3, 3, 3, true), library.Status);
        Assert.Equal("First", library.Find(Path.Combine(_music, "Album", "01.mp3"))?.Title);
    }

    [Fact]
    public async Task Spotify_folders_and_hidden_folders_are_never_looked_into()
    {
        WriteSong("Mine/song.mp3", "Mine");
        WriteSong("AppData/Spotify/Users/cache.mp3", "Spotify's");
        WriteSong("Packages/SpotifyAB.SpotifyMusic_zpdnekdrzrea0/LocalState/store.mp3", "Store Spotify's");
        var hidden = Directory.CreateDirectory(Path.Combine(_music, ".hidden"));
        hidden.Attributes |= FileAttributes.Hidden;
        WriteSong(".hidden/secret.mp3", "Hidden");
        var library = Create(excluded: [Path.Combine(_music, "AppData", "Spotify")]);

        await library.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Mine"], library.Files.Select(f => f.Title));
        Assert.True(library.IsExcluded(Path.Combine(_music, "AppData", "Spotify", "x.mp3")));
        Assert.True(library.IsExcluded(Path.Combine(_music, "Packages", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0")));
        Assert.False(library.IsExcluded(Path.Combine(_music, "Mine", "song.mp3")));
    }

    [Fact]
    public async Task A_chosen_folder_inside_Spotify_is_not_scanned()
    {
        WriteSong("AppData/Spotify/song.mp3", "Spotify's");
        var library = Create(excluded: [Path.Combine(_music, "AppData", "Spotify")]);
        library.SetFolders([Path.Combine(_music, "AppData", "Spotify")]);

        await library.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Empty(library.Files);
    }

    [Fact]
    public async Task A_rescan_reads_only_new_and_changed_files_and_keeps_their_dates()
    {
        var first = WriteSong("a.mp3", "A");
        WriteSong("b.mp3", "B");
        var library = Create();
        await library.ScanAsync(TestContext.Current.CancellationToken);
        var addedAt = library.Files.Single(f => f.Title == "A").AddedAt;

        _time.Advance(TimeSpan.FromDays(1));
        WriteSong("a.mp3", "A, remastered", frames: 5);
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(1));
        WriteSong("c.mp3", "C");
        await library.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, library.Status.ToRead);
        Assert.Equal(["C", "A, remastered", "B"], library.Files.Select(f => f.Title));
        Assert.Equal(addedAt, library.Files.Single(f => f.Title == "A, remastered").AddedAt);
        Assert.Equal(_time.GetUtcNow(), library.Files[0].AddedAt);
    }

    [Fact]
    public async Task Deleted_files_leave_the_list()
    {
        var gone = WriteSong("gone.mp3", "Gone");
        WriteSong("kept.mp3", "Kept");
        var library = Create();
        await library.ScanAsync(TestContext.Current.CancellationToken);
        var version = library.Version;

        File.Delete(gone);
        await library.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Kept"], library.Files.Select(f => f.Title));
        Assert.True(library.Version > version);
    }

    [Fact]
    public async Task The_index_shows_the_list_before_any_scan_on_the_next_launch()
    {
        WriteSong("a.mp3", "A");
        var first = Create();
        await first.ScanAsync(TestContext.Current.CancellationToken);

        var next = Create();
        await next.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first.Files, next.Files);
        Assert.False(next.Status.HasScanned);
    }

    [Fact]
    public async Task A_damaged_index_is_rebuilt_by_the_next_scan()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_index)!);
        await File.WriteAllTextAsync(_index, "{ not json", TestContext.Current.CancellationToken);
        WriteSong("a.mp3", "A");
        var library = Create();

        await library.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(library.Files);

        await library.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["A"], library.Files.Select(f => f.Title));
    }

    [Fact]
    public async Task Removing_a_folder_hides_its_files_at_once_and_adding_it_back_keeps_their_dates()
    {
        var other = Path.Combine(_root.FullName, "Other");
        Directory.CreateDirectory(other);
        WriteSong("a.mp3", "A");
        await File.WriteAllBytesAsync(Path.Combine(other, "b.mp3"), Song("B"), TestContext.Current.CancellationToken);
        var library = Create();
        library.SetFolders([_music, other]);
        await library.ScanAsync(TestContext.Current.CancellationToken);
        var addedAt = library.Files.Single(f => f.Title == "B").AddedAt;

        library.SetFolders([_music]);
        Assert.Equal(["A"], library.Files.Select(f => f.Title));

        _time.Advance(TimeSpan.FromDays(3));
        library.SetFolders([_music, other]);
        await library.ScanAsync(TestContext.Current.CancellationToken);
        Assert.Equal(addedAt, library.Files.Single(f => f.Title == "B").AddedAt);
    }

    [Fact]
    public async Task New_files_in_a_watched_folder_are_found_on_their_own()
    {
        WriteSong("a.mp3", "A");
        var library = Create();
        await library.ScanAsync(TestContext.Current.CancellationToken);
        library.StartWatching();

        WriteSong("new/b.mp3", "B");
        for (var i = 0; i < 100 && library.Files.Count < 2; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            _time.Advance(LocalLibrary.WatchDelay);
        }

        Assert.Equal(["A", "B"], library.Files.Select(f => f.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_default_folders_are_Music_and_Downloads()
    {
        var downloads = Path.Combine(_root.FullName, "Downloads");

        var folders = LocalLibrary.DefaultFolders(downloads);

        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        Assert.Equal(string.IsNullOrEmpty(music) ? [downloads] : [music, downloads], folders);
    }

    [Fact]
    public void Spotify_folders_cover_the_installer_and_store_versions()
    {
        var folders = LocalLibrary.SpotifyFolders();

        Assert.Contains(folders, f => f.EndsWith("Spotify", StringComparison.Ordinal));
        Assert.Contains(folders, f => f.EndsWith("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", StringComparison.Ordinal));
    }

    [Fact]
    public void Covers_come_from_the_file_or_else_the_folder_picture()
    {
        var album = Path.Combine(_music, "Album");
        Directory.CreateDirectory(album);
        var embedded = Path.Combine(album, "embedded.mp3");
        File.WriteAllBytes(embedded, Concat(Id3Tag(3, 0, Frame23("APIC", Apic("image/png", 3, string.Empty, Png))), MpegFrames(3)));
        var plain = WriteSong("Album/plain.mp3", "Plain");
        File.WriteAllBytes(Path.Combine(album, "Folder.JPG"), Jpeg);
        File.WriteAllBytes(Path.Combine(album, "cover.png"), Png);

        Assert.Equal(Png, LocalCovers.Read(embedded));

        // "cover" comes before "folder", whatever the case.
        Assert.Equal(Path.Combine(album, "cover.png"), LocalCovers.FindFolderImage(plain));
        Assert.Equal(Png, LocalCovers.Read(plain));

        File.Delete(Path.Combine(album, "cover.png"));
        Assert.Equal(Jpeg, LocalCovers.Read(plain));
    }

    private LocalLibrary Create(IReadOnlyList<string>? excluded = null)
    {
        var library = new LocalLibrary(_index, excluded ?? [], _time);
        library.SetFolders([_music]);
        _libraries.Add(library);
        return library;
    }

    private string WriteSong(string relativePath, string title, int frames = 3)
    {
        var path = Path.Combine(_music, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Song(title, frames));
        return path;
    }

    private static byte[] Song(string title, int frames = 3) =>
        Concat(Id3Tag(3, 0, Frame23("TIT2", Latin1Text(title))), MpegFrames(frames));
}

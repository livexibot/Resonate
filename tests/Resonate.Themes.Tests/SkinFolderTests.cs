using Resonate.Themes.Skins;
using static Resonate.Themes.Tests.SkinFixtures;

namespace Resonate.Themes.Tests;

public sealed class SkinFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "resonate-skins-" + Guid.NewGuid().ToString("N"));

    private string SkinsPath => Path.Combine(_root, "Skins");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_folder_lists_nothing()
    {
        var folder = new SkinFolder(SkinsPath);

        Assert.Empty(folder.List());
    }

    [Fact]
    public void Lists_only_skins_sorted_without_regard_to_case()
    {
        Directory.CreateDirectory(Path.Combine(SkinsPath, "folder.wsz"));
        foreach (var name in new[] { "b.wsz", "A.zip", "c.WSZ", "notes.txt", "d.wsz.bak" })
        {
            File.WriteAllBytes(Path.Combine(SkinsPath, name), []);
        }

        Assert.Equal(["A.zip", "b.wsz", "c.WSZ"], new SkinFolder(SkinsPath).List());
    }

    [Fact]
    public void Importing_copies_the_skin_in_and_it_loads()
    {
        var archive = ClassicSkin();
        var source = WriteSource("Cool Skin.wsz", archive);
        var folder = new SkinFolder(SkinsPath);

        var name = folder.Import(source);

        Assert.Equal("Cool Skin.wsz", name);
        Assert.Equal(archive, File.ReadAllBytes(Path.Combine(SkinsPath, name)));
        Assert.True(File.Exists(source));
        Assert.Equal(["Cool Skin.wsz"], folder.List());

        var skin = folder.Load(name);
        Assert.Equal("Cool Skin", skin.Name);
        Assert.NotNull(skin.OwnSheet(SkinSheet.Main));
    }

    [Fact]
    public void Importing_a_taken_name_never_overwrites()
    {
        var folder = new SkinFolder(SkinsPath);
        var first = ClassicSkin();
        var second = Zip([new ZipItem("main.bmp", SheetBmp(SkinSheet.Main))]);

        Assert.Equal("Cool.wsz", folder.Import(WriteSource("Cool.wsz", first)));
        Assert.Equal("Cool (2).wsz", folder.Import(WriteSource("Cool.wsz", second)));
        Assert.Equal("Cool (3).wsz", folder.Import(WriteSource("Cool.wsz", second)));

        Assert.Equal(first, File.ReadAllBytes(Path.Combine(SkinsPath, "Cool.wsz")));
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(SkinsPath, "Cool (2).wsz")));
    }

    [Fact]
    public void Names_that_differ_only_in_case_count_as_taken()
    {
        Directory.CreateDirectory(SkinsPath);
        File.WriteAllBytes(Path.Combine(SkinsPath, "cool.ZIP"), []);

        var name = new SkinFolder(SkinsPath).Import(WriteSource("Cool.zip", ClassicSkin()));

        Assert.Equal("Cool (2).zip", name);
    }

    [Fact]
    public void A_classic_skin_with_another_ending_is_kept_as_wsz()
    {
        var folder = new SkinFolder(SkinsPath);

        Assert.Equal("Old.wsz", folder.Import(WriteSource("Old.wal", ClassicSkin())));
        Assert.Equal("Bare.wsz", folder.Import(WriteSource("Bare", ClassicSkin())));
    }

    [Fact]
    public void Characters_windows_refuses_are_replaced()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows cannot create these names in the first place.");

        var name = new SkinFolder(SkinsPath).Import(WriteSource("What? A \"skin\" <3.wsz", ClassicSkin()));

        Assert.Equal("What_ A _skin_ _3.wsz", name);
    }

    [Fact]
    public void Importing_something_that_is_not_a_skin_copies_nothing()
    {
        var folder = new SkinFolder(SkinsPath);

        var notAnArchive = Assert.Throws<SkinFormatException>(() => folder.Import(WriteSource("photo.wsz", Text("not a zip"))));
        var modern = Assert.Throws<SkinFormatException>(() => folder.Import(WriteSource("modern.wsz", Zip([new ZipItem("skin.xml", Text("<x/>"))]))));

        Assert.Equal(SkinArchive.NotAnArchiveMessage, notAnArchive.Message);
        Assert.Equal(SkinArchive.ModernSkinMessage, modern.Message);
        Assert.False(Directory.Exists(SkinsPath));
    }

    [Fact]
    public void Importing_a_file_over_16_mb_is_refused_before_reading_it()
    {
        var source = WriteSource("huge.wsz", []);
        using (var stream = new FileStream(source, FileMode.Open))
        {
            stream.SetLength(SkinFolder.MaxArchiveBytes + 1);
        }

        var error = Assert.Throws<SkinFormatException>(() => new SkinFolder(SkinsPath).Import(source));

        Assert.Equal(SkinArchive.TooLargeMessage, error.Message);
        Assert.False(Directory.Exists(SkinsPath));
    }

    [Fact]
    public void Importing_a_missing_file_is_an_io_error()
    {
        Assert.ThrowsAny<IOException>(() => new SkinFolder(SkinsPath).Import(Path.Combine(_root, "nowhere.wsz")));
    }

    [Fact]
    public void Removing_deletes_and_ignores_unknown_names()
    {
        var folder = new SkinFolder(SkinsPath);
        folder.Remove("never added.wsz");

        var name = folder.Import(WriteSource("Gone.wsz", ClassicSkin()));
        File.WriteAllBytes(Path.Combine(SkinsPath, "keep.txt"), []);
        folder.Remove(name);
        folder.Remove("never added.wsz");
        folder.Remove("keep.txt");

        Assert.Empty(folder.List());
        Assert.True(File.Exists(Path.Combine(SkinsPath, "keep.txt")));
    }

    [Fact]
    public void Loading_a_missing_skin_is_an_io_error()
    {
        Assert.ThrowsAny<IOException>(() => new SkinFolder(SkinsPath).Load("missing.wsz"));
    }

    [Fact]
    public void Loading_a_broken_skin_says_why()
    {
        Directory.CreateDirectory(SkinsPath);
        File.WriteAllBytes(Path.Combine(SkinsPath, "broken.wsz"), Text("nope"));

        var error = Assert.Throws<SkinFormatException>(() => new SkinFolder(SkinsPath).Load("broken.wsz"));

        Assert.Equal(SkinArchive.NotAnArchiveMessage, error.Message);
    }

    [Theory]
    [InlineData("../outside.wsz")]
    [InlineData("..\\outside.wsz")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("sub/inside.wsz")]
    [InlineData("C:outside.wsz")]
    [InlineData("/etc/outside.wsz")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b.wsz")]
    [InlineData("tab\there.wsz")]
    public void Names_that_could_leave_the_folder_are_refused(string name)
    {
        var folder = new SkinFolder(SkinsPath);

        Assert.ThrowsAny<ArgumentException>(() => folder.Load(name));
        Assert.ThrowsAny<ArgumentException>(() => folder.Remove(name));
    }

    [Theory]
    [InlineData("Cool.wsz", "Cool")]
    [InlineData("Cool.ZIP", "Cool")]
    [InlineData("Cool.wsz.zip", "Cool.wsz")]
    [InlineData("No ending", "No ending")]
    [InlineData(".wsz", ".wsz")]
    public void Labels_drop_the_ending(string fileName, string label) => Assert.Equal(label, SkinFolder.Label(fileName));

    private string WriteSource(string name, byte[] bytes)
    {
        var downloads = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(downloads);
        var path = Path.Combine(downloads, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

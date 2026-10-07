using System.IO.Compression;
using Resonate.Plugins.Installing;
using Resonate.Plugins.Packaging;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>Packing a release's plugins, and downloading and checking them.</summary>
public sealed class InstallTests : IDisposable
{
    private readonly Packages _packages = new();

    public void Dispose() => _packages.Dispose();

    private PluginInstaller Installer() => new(_packages.Installed, new FolderPluginFeed(_packages.Feed));

    [Fact]
    public void The_catalog_lists_every_plugin_and_the_helper_with_their_hashes()
    {
        var catalog = PluginCatalog.Load(File.OpenRead(Path.Combine(_packages.Feed, PluginPackager.CatalogFileName)));
        Assert.Equal(["skip-rules", "sleep-timer"], catalog.Plugins.Select(p => p.Manifest.Id));
        Assert.Equal("resonate-plugin-host-win-x64.zip", catalog.Host!.File);
        foreach (var package in catalog.Plugins.Select(p => p.Package).Append(catalog.Host))
        {
            var actual = PluginPackager.Describe(Path.Combine(_packages.Feed, package.File));
            Assert.Equal(actual.Sha256, package.Sha256);
            Assert.Equal(actual.Size, package.Size);
        }
    }

    [Fact]
    public void Packing_the_same_plugin_twice_gives_the_same_bytes()
    {
        var other = Path.Combine(_packages.Root, "again");
        var first = PluginPackager.PackPlugin(HostHarness.RepositoryPlugin("sleep-timer"), other);
        Thread.Sleep(1100);
        var second = PluginPackager.PackPlugin(HostHarness.RepositoryPlugin("sleep-timer"), other);
        Assert.Equal(first.Package.Sha256, second.Package.Sha256);
        Assert.Equal(_packages.Catalog.Find("sleep-timer")!.Package.Sha256, first.Package.Sha256);
    }

    [Fact]
    public void A_plugin_with_a_bad_manifest_is_not_packed()
    {
        var folder = Path.Combine(_packages.Root, "bad");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{ "id": "Bad", "name": "Bad", "version": "1.0.0", "description": "x", "permissions": ["internet"] }""");
        var error = Assert.Throws<InvalidDataException>(() => PluginPackager.PackPlugin(folder, Path.Combine(_packages.Root, "out")));
        Assert.Contains("internet", error.Message, StringComparison.Ordinal);
        Assert.Contains("main.js", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installs_once_and_reports_progress()
    {
        var installer = Installer();
        var package = _packages.Catalog.Find("skip-rules")!.Package;
        var progress = new List<double>();

        var folder = await installer.InstallAsync("plugin-skip-rules", package, new SyncProgress(progress.Add), CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(folder, "main.js")));
        Assert.True(installer.IsInstalled("plugin-skip-rules", package));
        Assert.Equal(1, progress[^1]);

        File.Delete(Path.Combine(_packages.Feed, package.File));
        Assert.Equal(folder, await installer.InstallAsync("plugin-skip-rules", package, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_file_that_does_not_match_the_catalog_is_thrown_away()
    {
        var installer = Installer();
        var package = _packages.Catalog.Find("skip-rules")!.Package;
        var file = Path.Combine(_packages.Feed, package.File);
        var bytes = File.ReadAllBytes(file);
        bytes[^10] ^= 0xFF;
        File.WriteAllBytes(file, bytes);

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => installer.InstallAsync("plugin-skip-rules", package, null, CancellationToken.None));
        Assert.Contains("not the expected file", error.Message, StringComparison.Ordinal);
        Assert.False(installer.IsInstalled("plugin-skip-rules", package));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_packages.Installed));
    }

    [Fact]
    public async Task A_longer_file_is_refused_without_reading_it_all()
    {
        var installer = Installer();
        var package = _packages.Catalog.Find("skip-rules")!.Package;
        File.AppendAllText(Path.Combine(_packages.Feed, package.File), new string('x', 100_000));
        await Assert.ThrowsAsync<PluginInstallException>(() => installer.InstallAsync("plugin-skip-rules", package, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_file_says_to_check_the_connection()
    {
        var installer = Installer();
        var package = new PluginPackage { File = "missing.zip", Sha256 = new string('a', 64), Size = 10 };
        var error = await Assert.ThrowsAsync<PluginInstallException>(() => installer.InstallAsync("plugin-x", package, null, CancellationToken.None));
        Assert.Contains("internet connection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archives_that_reach_outside_their_folder_are_refused()
    {
        var evil = Path.Combine(_packages.Feed, "evil.zip");
        using (var archive = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../../escaped.js").Open());
            writer.Write("bad");
        }

        var package = PluginPackager.Describe(evil);
        await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("plugin-evil", package, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_packages.Root, "escaped.js")));
    }

    [Fact]
    public async Task Removing_deletes_every_version()
    {
        var installer = Installer();
        var package = _packages.Catalog.Find("sleep-timer")!.Package;
        await installer.InstallAsync("plugin-sleep-timer", package, null, CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_packages.Installed, "plugin-sleep-timer", "0000000000000000"));

        installer.RemoveOtherVersions("plugin-sleep-timer", package);
        Assert.Single(Directory.EnumerateDirectories(Path.Combine(_packages.Installed, "plugin-sleep-timer")));

        installer.Remove("plugin-sleep-timer");
        Assert.False(Directory.Exists(Path.Combine(_packages.Installed, "plugin-sleep-timer")));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

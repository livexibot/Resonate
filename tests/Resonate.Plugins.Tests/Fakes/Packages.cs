using Resonate.Plugins.Packaging;

namespace Resonate.Plugins.Tests.Fakes;

/// <summary>A release's plugin files, packed from the repository's plugins into a temporary folder.</summary>
internal sealed class Packages : IDisposable
{
    public Packages()
    {
        var hostFolder = Path.Combine(Root, "host-publish");
        Directory.CreateDirectory(hostFolder);

        // The in-process tests never run this file; it only has to be packed and checked.
        File.WriteAllText(Path.Combine(hostFolder, ProcessPluginHostLauncher.ExecutableName + ".exe"), "pretend helper");
        Catalog = PluginPackager.PackAll(Path.Combine(HostHarness.RepositoryRoot(), "plugins"), hostFolder, "win-x64", null, Feed);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "resonate-packages-" + Guid.NewGuid().ToString("N"));

    /// <summary>The download folder.</summary>
    public string Feed => Path.Combine(Root, "feed");

    /// <summary>Where plugins are installed.</summary>
    public string Installed => Path.Combine(Root, "installed");

    public PluginCatalog Catalog { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

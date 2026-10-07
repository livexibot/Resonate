using System.Text.Json;

namespace Resonate.Plugins;

/// <summary>
/// The plugins a Resonate release offers, built into the app when the
/// release is made (see <c>tools/Resonate.PluginPack</c>). Each entry pins
/// the exact file to download by its SHA-256, so only plugins built from
/// this repository for this version can be installed.
/// </summary>
public sealed class PluginCatalog
{
    public const int CurrentFormat = 1;

    public static PluginCatalog Empty { get; } = new();

    public int Format { get; set; } = CurrentFormat;

    /// <summary>Where the files are downloaded from: the release's download address, ending in a slash.</summary>
    public string? Source { get; set; }

    /// <summary>The helper program that runs plugins, for this computer's processor.</summary>
    public PluginPackage? Host { get; set; }

    public List<CatalogPlugin> Plugins { get; set; } = [];

    public CatalogPlugin? Find(string id) => Plugins.Find(p => p.Manifest.Id == id);

    /// <summary>Reads a catalog; an unreadable or newer-format one is treated as empty.</summary>
    public static PluginCatalog Load(Stream? stream)
    {
        if (stream is null)
        {
            return Empty;
        }

        try
        {
            var catalog = JsonSerializer.Deserialize(stream, PluginJsonContext.Default.PluginCatalog);
            return catalog is { Format: CurrentFormat, Host: not null } ? catalog : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public void Save(Stream stream) => JsonSerializer.Serialize(stream, this, PluginJsonContext.Default.PluginCatalog);
}

public sealed class CatalogPlugin
{
    public PluginManifest Manifest { get; set; } = new();

    public PluginPackage Package { get; set; } = new();
}

/// <summary>One downloadable file.</summary>
public sealed class PluginPackage
{
    /// <summary>The file name in the release, such as "resonate-plugin-sleep-timer.zip".</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Lower-case hexadecimal.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }
}

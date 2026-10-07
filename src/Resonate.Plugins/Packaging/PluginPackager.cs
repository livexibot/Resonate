using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Resonate.Plugins.Packaging;

/// <summary>
/// Builds the files a release offers (each plugin, and the helper for one
/// processor) and the catalog the app is built with. Used by
/// tools/Resonate.PluginPack in CI and the release workflow.
/// </summary>
public static class PluginPackager
{
    public const string ManifestFileName = "plugin.json";
    public const string CatalogFileName = "plugin-catalog.json";

    /// <summary>Every file gets this date, so packing the same files twice gives the same bytes.</summary>
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static PluginManifest ReadManifest(string folder)
    {
        using var stream = File.OpenRead(Path.Combine(folder, ManifestFileName));
        return JsonSerializer.Deserialize(stream, PluginJsonContext.Default.PluginManifest)
            ?? throw new InvalidDataException($"{folder}: {ManifestFileName} is empty.");
    }

    /// <summary>Checks a plugin's folder and packs it; throws with every problem found.</summary>
    public static CatalogPlugin PackPlugin(string folder, string outputFolder)
    {
        var manifest = ReadManifest(folder);
        var errors = manifest.Validate(file => File.Exists(Path.Combine(folder, file)));
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"{folder}:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", errors));
        }

        Directory.CreateDirectory(outputFolder);
        var output = Path.Combine(outputFolder, $"resonate-plugin-{manifest.Id}-{manifest.Version}.zip");

        // Stored, not compressed: plugins are a few kilobytes, and this keeps the bytes identical on every machine.
        CreateZip(folder, FilesIn(folder), output, CompressionLevel.NoCompression);
        return new CatalogPlugin { Manifest = manifest, Package = Describe(output) };
    }

    /// <summary>Packs the published helper for one processor (such as "win-x64").</summary>
    public static PluginPackage PackHost(string publishFolder, string runtime, string outputFolder)
    {
        var files = FilesIn(publishFolder)
            .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!files.Any(f => f is ProcessPluginHostLauncher.ExecutableName or ProcessPluginHostLauncher.ExecutableName + ".exe"))
        {
            throw new InvalidDataException($"{publishFolder} has no {ProcessPluginHostLauncher.ExecutableName} program.");
        }

        Directory.CreateDirectory(outputFolder);
        var output = Path.Combine(outputFolder, $"resonate-plugin-host-{runtime}.zip");
        CreateZip(publishFolder, files, output, CompressionLevel.SmallestSize);
        return Describe(output);
    }

    /// <summary>Packs every plugin under <paramref name="pluginsFolder"/> and the helper, and writes the catalog.</summary>
    public static PluginCatalog PackAll(string pluginsFolder, string hostPublishFolder, string runtime, string? source, string outputFolder)
    {
        var catalog = new PluginCatalog
        {
            Source = source,
            Host = PackHost(hostPublishFolder, runtime, outputFolder),
        };

        foreach (var folder in Directory.EnumerateDirectories(pluginsFolder).Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(folder, ManifestFileName)))
            {
                catalog.Plugins.Add(PackPlugin(folder, outputFolder));
            }
        }

        var ids = catalog.Plugins.GroupBy(p => p.Manifest.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (ids.Count > 0)
        {
            throw new InvalidDataException("Two plugins share the id " + string.Join(", ", ids) + ".");
        }

        using var stream = File.Create(Path.Combine(outputFolder, CatalogFileName));
        catalog.Save(stream);
        return catalog;
    }

    public static PluginPackage Describe(string path)
    {
        using var stream = File.OpenRead(path);
        return new PluginPackage
        {
            File = Path.GetFileName(path),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)),
            Size = stream.Length,
        };
    }

    private static List<string> FilesIn(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(part => part.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static void CreateZip(string folder, IEnumerable<string> files, string output, CompressionLevel level)
    {
        using var stream = File.Create(output);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file, level);
            entry.LastWriteTime = FixedTime;
            using var source = File.OpenRead(Path.Combine(folder, file));
            using var target = entry.Open();
            source.CopyTo(target);
        }
    }
}

using System.IO.Compression;
using System.Security.Cryptography;

namespace Resonate.Plugins.Installing;

/// <summary>
/// Downloads a plugin or the plugin helper when the user turns a plugin on,
/// checks it against the SHA-256 in the app's catalog, and unpacks it under
/// its own folder (&lt;root&gt;/&lt;name&gt;/&lt;first 16 hash digits&gt;). A file that does not
/// match is thrown away, so only what this release was built with runs.
/// </summary>
public sealed class PluginInstaller
{
    public const long MaxDownloadSize = 64L * 1024 * 1024;
    public const long MaxUnpackedSize = 128L * 1024 * 1024;

    /// <summary>Written last, so a folder without it is a half-finished install.</summary>
    internal const string VerifiedMarker = ".verified";

    private readonly IPluginFeed _feed;

    public PluginInstaller(string root, IPluginFeed feed)
    {
        Root = root;
        _feed = feed;
    }

    public string Root { get; }

    public string FolderFor(string name, PluginPackage package) =>
        Path.Combine(Root, name, package.Sha256.ToLowerInvariant()[..Math.Min(16, package.Sha256.Length)]);

    public bool IsInstalled(string name, PluginPackage package) =>
        File.Exists(Path.Combine(FolderFor(name, package), VerifiedMarker));

    /// <summary>Downloads, checks and unpacks the package unless it is already there; returns its folder.</summary>
    /// <exception cref="PluginInstallException">The download failed or did not match.</exception>
    public async Task<string> InstallAsync(string name, PluginPackage package, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var folder = FolderFor(name, package);
        if (IsInstalled(name, package))
        {
            return folder;
        }

        if (package.Sha256.Length != 64 || package.Size is <= 0 or > MaxDownloadSize)
        {
            throw new PluginInstallException("This plugin's entry in the app is damaged.");
        }

        var temporary = Path.Combine(Root, ".download-" + Guid.NewGuid().ToString("N"));
        var archive = temporary + ".zip";
        try
        {
            Directory.CreateDirectory(Root);
            await DownloadAsync(package, archive, progress, cancellationToken).ConfigureAwait(false);
            Unpack(archive, temporary);
            await File.WriteAllTextAsync(Path.Combine(temporary, VerifiedMarker), package.Sha256, cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            Directory.Move(temporary, folder);
            progress?.Report(1);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new PluginInstallException("The plugin could not be saved on this computer.", ex);
        }
        finally
        {
            TryDelete(archive);
            TryDeleteFolder(temporary);
        }
    }

    /// <summary>Removes every installed version of <paramref name="name"/>; false when something was in use and stayed.</summary>
    public bool Remove(string name) => TryDeleteFolder(Path.Combine(Root, name));

    /// <summary>Removes the versions of <paramref name="name"/> other than <paramref name="keep"/> (left over after an update).</summary>
    public void RemoveOtherVersions(string name, PluginPackage keep)
    {
        var parent = Path.Combine(Root, name);
        if (!Directory.Exists(parent))
        {
            return;
        }

        var current = FolderFor(name, keep);
        foreach (var folder in Directory.EnumerateDirectories(parent))
        {
            if (!string.Equals(Path.GetFullPath(folder), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFolder(folder);
            }
        }
    }

    /// <summary>Removes everything under the root except the given names (plugins turned off while files were in use).</summary>
    public void RemoveAllExcept(IReadOnlyCollection<string> names)
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            if (!names.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
            {
                TryDeleteFolder(folder);
            }
        }
    }

    private async Task DownloadAsync(PluginPackage package, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Stream content;
        try
        {
            (content, _) = await _feed.OpenAsync(package.File, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PluginInstallException("The download did not start. Check the internet connection and try again.", ex);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (content.ConfigureAwait(false))
        {
            var file = File.Create(path);
            await using (file.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    int read;
                    try
                    {
                        read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
                    {
                        throw new PluginInstallException("The download stopped. Check the internet connection and try again.", ex);
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    received += read;
                    if (received > package.Size)
                    {
                        throw new PluginInstallException("The download was not the expected file, so it was thrown away.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(0.95 * received / package.Size);
                }
            }
        }

        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (received != package.Size || !string.Equals(digest, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginInstallException("The download was not the expected file, so it was thrown away.");
        }
    }

    private static void Unpack(string archivePath, string folder)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > MaxUnpackedSize)
            {
                throw new PluginInstallException("The plugin is larger than allowed.");
            }
        }

        // Refuses entries that would land outside the folder.
        Directory.CreateDirectory(folder);
        archive.ExtractToDirectory(folder, overwriteFiles: false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next clean-up.
        }
    }

    private static bool TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use (the helper is still closing); removed on a later start.
            return false;
        }
    }
}

public sealed class PluginInstallException : Exception
{
    public PluginInstallException()
    {
    }

    public PluginInstallException(string message)
        : base(message)
    {
    }

    public PluginInstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

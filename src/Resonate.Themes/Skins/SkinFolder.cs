using System.Buffers;
using IOPath = System.IO.Path;

namespace Resonate.Themes.Skins;

/// <summary>
/// The folder of skins the user added. Adding one copies the archive in, so
/// the original can be moved or deleted; the built-in skin is not a file.
/// File names given to it must be plain names inside the folder: anything
/// with a folder in it throws <see cref="ArgumentException"/>. Problems with
/// the disk come out as <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class SkinFolder(string path)
{
    /// <summary>The largest archive Resonate accepts.</summary>
    public const long MaxArchiveBytes = 16L * 1024 * 1024;

    // Enough for any real use; stops a strange folder from looping for ever.
    private const int MaxCopies = 1000;

    // Longer names are cut so " (2).wsz" still fits in a file name.
    private const int MaxStemLength = 120;

    // Characters Windows refuses in file names, checked the same on every system.
    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create("/\\:*?\"<>|");

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The skins' file names (.wsz or .zip), sorted by name; empty when the folder does not exist.</summary>
    public IReadOnlyList<string> List()
    {
        var names = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path))
            {
                var name = IOPath.GetFileName(file);
                if (IsSkinFileName(name) && IsPlainFileName(name))
                {
                    names.Add(name);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        names.Sort(static (a, b) =>
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a, b);
        });
        return names;
    }

    /// <summary>
    /// Checks <paramref name="sourceFile"/> is a usable classic skin, copies it
    /// in (adding " (2)" and so on when the name is taken) and returns its file
    /// name. Throws <see cref="SkinFormatException"/> when it is not a usable skin.
    /// </summary>
    public string Import(string sourceFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceFile);
        var bytes = ReadArchive(sourceFile);
        var fileName = TargetFileName(IOPath.GetFileName(sourceFile));

        // What is checked is exactly what gets copied, read once.
        _ = Skin.Load(bytes, Label(fileName));
        Directory.CreateDirectory(Path);
        return WriteNew(fileName, bytes);
    }

    /// <summary>Deletes a skin the user added; unknown names are ignored.</summary>
    public void Remove(string fileName)
    {
        var file = Resolve(fileName);
        if (IsSkinFileName(fileName) && File.Exists(file))
        {
            File.Delete(file);
        }
    }

    /// <summary>Reads a skin from the folder. Throws <see cref="SkinFormatException"/> when it cannot be used.</summary>
    public Skin Load(string fileName) => Skin.Load(ReadArchive(Resolve(fileName)), Label(fileName));

    /// <summary>A skin's name for showing: the file name without .wsz or .zip.</summary>
    public static string Label(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return IsSkinFileName(fileName) && fileName.Length > 4 ? fileName[..^4] : fileName;
    }

    /// <summary>True for a name the folder can hold: no folders, no characters Windows refuses, not "." or "..".</summary>
    internal static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.AsSpan().Trim(" .").IsEmpty)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return !name.AsSpan().ContainsAny(ForbiddenCharacters);
    }

    private static bool IsSkinFileName(string name) =>
        name.EndsWith(".wsz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads an archive whole, refusing one over <see cref="MaxArchiveBytes"/> before reading it.</summary>
    private static byte[] ReadArchive(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxArchiveBytes)
        {
            throw new SkinFormatException(SkinArchive.TooLargeMessage);
        }

        var bytes = new byte[stream.Length];
        var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return read == bytes.Length ? bytes : bytes[..read];
    }

    /// <summary>
    /// The name an imported skin is kept under: the original's, with any
    /// character Windows refuses replaced, ending in .wsz when it ended in
    /// something else (a classic skin saved as .wal, say).
    /// </summary>
    private static string TargetFileName(string original)
    {
        var extension = IsSkinFileName(original) ? IOPath.GetExtension(original) : ".wsz";
        var stem = IOPath.GetFileNameWithoutExtension(original);
        var characters = stem.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (char.IsControl(characters[i]) || ForbiddenCharacters.Contains(characters[i]) || IsLoneSurrogate(characters, i))
            {
                characters[i] = '_';
            }
        }

        // Never cut an emoji in half: half of one can't be saved in the settings, so the skin would be lost on restart.
        var length = Math.Min(characters.Length, MaxStemLength);
        if (length < characters.Length && char.IsHighSurrogate(characters[length - 1]))
        {
            length--;
        }

        stem = new string(characters, 0, length).Trim();
        var name = stem + extension;
        return IsPlainFileName(name) && !stem.AsSpan().Trim(" .").IsEmpty ? name : "Skin" + extension;
    }

    private static bool IsLoneSurrogate(char[] characters, int i) =>
        char.IsHighSurrogate(characters[i])
            ? i + 1 >= characters.Length || !char.IsLowSurrogate(characters[i + 1])
            : char.IsLowSurrogate(characters[i]) && (i == 0 || !char.IsHighSurrogate(characters[i - 1]));

    /// <summary>Writes a new file, never over an old one: "Name.wsz", then "Name (2).wsz" and so on.</summary>
    private string WriteNew(string fileName, byte[] bytes)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path))
        {
            taken.Add(IOPath.GetFileName(file));
        }

        var stem = IOPath.GetFileNameWithoutExtension(fileName);
        var extension = IOPath.GetExtension(fileName);
        for (var copy = 1; copy <= MaxCopies; copy++)
        {
            var candidate = copy == 1 ? fileName : $"{stem} ({copy}){extension}";
            if (taken.Contains(candidate))
            {
                continue;
            }

            var target = IOPath.Combine(Path, candidate);
            var created = false;
            try
            {
                using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                stream.Write(bytes);
            }
            catch (IOException) when (!created && File.Exists(target))
            {
                // Someone took the name in the meantime; try the next one.
                continue;
            }
            catch when (created)
            {
                // Never leave half a skin behind.
                TryDelete(target);
                throw;
            }

            return candidate;
        }

        throw new IOException("There are too many skins with this name already.");
    }

    /// <summary>The full path of a skin in the folder, refusing any name that would point outside it.</summary>
    private string Resolve(string fileName)
    {
        if (!IsPlainFileName(fileName))
        {
            throw new ArgumentException("A skin's file name must be a plain name, without folders.", nameof(fileName));
        }

        var file = IOPath.GetFullPath(IOPath.Combine(Path, fileName));
        var folder = IOPath.TrimEndingDirectorySeparator(IOPath.GetFullPath(Path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(IOPath.GetDirectoryName(file), folder, comparison))
        {
            throw new ArgumentException("A skin's file name must be a plain name, without folders.", nameof(fileName));
        }

        return file;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

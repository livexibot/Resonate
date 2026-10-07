namespace Resonate.Themes.Skins;

/// <summary>
/// The folder of skins the user added. Adding one copies the archive in, so
/// the original can be moved or deleted; the built-in skin is not a file.
/// </summary>
public sealed class SkinFolder(string path)
{
    /// <summary>The largest archive Resonate accepts.</summary>
    public const long MaxArchiveBytes = 16L * 1024 * 1024;

    public string Path { get; } = path;

    /// <summary>The skins' file names (.wsz or .zip), sorted by name; empty when the folder does not exist.</summary>
    public IReadOnlyList<string> List() => throw new NotImplementedException();

    /// <summary>
    /// Checks <paramref name="sourceFile"/> is a usable classic skin, copies it
    /// in (adding " (2)" and so on when the name is taken) and returns its file
    /// name. Throws <see cref="SkinFormatException"/> when it is not a usable skin.
    /// </summary>
    public string Import(string sourceFile) => throw new NotImplementedException();

    /// <summary>Deletes a skin the user added; unknown names are ignored.</summary>
    public void Remove(string fileName) => throw new NotImplementedException();

    /// <summary>Reads a skin from the folder. Throws <see cref="SkinFormatException"/> when it cannot be used.</summary>
    public Skin Load(string fileName) => throw new NotImplementedException();

    /// <summary>A skin's name for showing: the file name without .wsz or .zip.</summary>
    public static string Label(string fileName) => throw new NotImplementedException();
}

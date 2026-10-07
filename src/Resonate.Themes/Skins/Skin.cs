namespace Resonate.Themes.Skins;

/// <summary>
/// A classic (Winamp 2 style) skin for the classic player: its pictures, the
/// visualiser's colours and the playlist colours. A picture the skin lacks
/// comes from Resonate's own built-in skin, one sheet at a time, as Winamp
/// did with its base skin.
/// </summary>
public sealed class Skin
{
    /// <summary>viscolor.txt holds this many colours.</summary>
    public const int VisColorCount = 24;

    private static readonly Lazy<Skin> BuiltInSkinInstance = new(BuiltInSkin.Create);

    private readonly Dictionary<SkinSheet, SkinImage> _sheets;

    internal Skin(string name, IReadOnlyDictionary<SkinSheet, SkinImage> sheets, IReadOnlyList<uint> visColors, PlaylistColors playlist, bool isBuiltIn = false)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(visColors);
        if (visColors.Count != VisColorCount)
        {
            throw new ArgumentException($"A skin has {VisColorCount} visualiser colours.", nameof(visColors));
        }

        Name = name;
        _sheets = new Dictionary<SkinSheet, SkinImage>(sheets);
        VisColors = [.. visColors];
        Playlist = playlist;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>Resonate's own skin, drawn in code; it has every sheet.</summary>
    public static Skin BuiltIn => BuiltInSkinInstance.Value;

    /// <summary>What the skin is called (its file name without the extension, or the built-in skin's name).</summary>
    public string Name { get; }

    public bool IsBuiltIn { get; }

    /// <summary>The visualiser's 24 colours (viscolor.txt), as <c>0xFFRRGGBB</c>.</summary>
    public IReadOnlyList<uint> VisColors { get; }

    /// <summary>The playlist colours (pledit.txt).</summary>
    public PlaylistColors Playlist { get; }

    /// <summary>The skin's own picture, or null when its archive lacks it.</summary>
    public SkinImage? OwnSheet(SkinSheet sheet) => _sheets.GetValueOrDefault(sheet);

    /// <summary>
    /// The picture to draw from: the skin's own; for a missing balance.bmp its
    /// volume.bmp (same layout); otherwise the built-in skin's.
    /// </summary>
    public SkinImage Sheet(SkinSheet sheet)
    {
        if (_sheets.TryGetValue(sheet, out var own))
        {
            return own;
        }

        if (sheet == SkinSheet.Balance && _sheets.TryGetValue(SkinSheet.Volume, out var volume))
        {
            return volume;
        }

        return IsBuiltIn
            ? throw new InvalidOperationException($"The built-in skin has no {SkinSheets.FileName(sheet)}.")
            : BuiltIn.Sheet(sheet);
    }

    /// <summary>
    /// Reads a .wsz (or .zip) archive. Anything wrong with it (not an archive,
    /// too large, no classic skin pictures, a damaged bitmap) throws
    /// <see cref="SkinFormatException"/> with a message for the user.
    /// </summary>
    public static Skin Load(ReadOnlySpan<byte> archive, string name) => SkinArchive.Read(archive, name);
}

/// <summary>The playlist colours of a skin (pledit.txt), as <c>0xFFRRGGBB</c>.</summary>
public sealed record PlaylistColors(uint Normal, uint Current, uint NormalBackground, uint SelectedBackground, string? Font);

/// <summary>A skin file could not be used; the message says why, in words for the user.</summary>
public sealed class SkinFormatException : Exception
{
    public SkinFormatException()
    {
    }

    public SkinFormatException(string message)
        : base(message)
    {
    }

    public SkinFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>A look as shared text: a small envelope so other versions can tell what it is.</summary>
public sealed class SharedLook
{
    /// <summary>The format version. Always 1 for now.</summary>
    public int ResonateLook { get; set; } = 1;

    public ThemeDefinition? Look { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SharedLook))]
[JsonSerializable(typeof(ThemeDefinition))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;

/// <summary>
/// Copying a look as text and pasting one back. Pasted text comes from
/// anywhere, so reading never throws: it returns null for anything that is
/// not a look, and clamps every value of one that is.
/// </summary>
public static class ThemeJson
{
    /// <summary>Longer text than this is not a look (they are about 1 KB).</summary>
    public const int MaxLength = 32 * 1024;

    public static string Export(ThemeDefinition look) =>
        JsonSerializer.Serialize(new SharedLook { Look = look.Normalize() }, ThemeJsonContext.Default.SharedLook);

    public static ThemeDefinition? Import(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }

        text = text.Trim();
        try
        {
            var shared = JsonSerializer.Deserialize(text, ThemeJsonContext.Default.SharedLook);
            var look = shared?.Look ?? JsonSerializer.Deserialize(text, ThemeJsonContext.Default.ThemeDefinition);
            return look is null || !LooksLikeALook(text) ? null : look.Normalize();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Any JSON object deserialises to a look full of defaults; require at
    /// least one colour so random JSON on the clipboard is not mistaken for one.
    /// </summary>
    private static bool LooksLikeALook(string text) =>
        text.Contains("\"accent\"", StringComparison.OrdinalIgnoreCase)
        || text.Contains("\"background\"", StringComparison.OrdinalIgnoreCase);

    /// <summary>A short, file-name-safe slug, for ids of saved looks.</summary>
    internal static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= 24)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }
}

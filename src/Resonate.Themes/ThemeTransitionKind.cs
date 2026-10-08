using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>
/// How the window changes from one look to another. Settings store the name,
/// so never rename a member; new ones go at the end.
/// </summary>
[JsonConverter(typeof(ThemeTransitionKindConverter))]
public enum ThemeTransitionKind
{
    /// <summary>Every colour flows into the new one; shapes cross-fade.</summary>
    Morph,

    /// <summary>The new look spreads in a circle from where you clicked.</summary>
    Ripple,

    /// <summary>The old look splits down the middle and slides away.</summary>
    Split,

    /// <summary>The old look falls away in strips, one after another.</summary>
    Blinds,

    /// <summary>A soft edge sweeps the new look across the window.</summary>
    Wipe,

    /// <summary>A different one each time.</summary>
    Random,

    /// <summary>Change at once.</summary>
    None,

    /// <summary>The old look fades out over the new one: a plain cross-fade.</summary>
    Fade,

    /// <summary>The new look spreads from the middle of the window, in the window's own shape.</summary>
    Grow,
}

/// <summary>
/// Reads and writes <see cref="ThemeTransitionKind"/> by name. A name or
/// number it does not know (from a newer or older version) reads as
/// <see cref="ThemeTransitionKind.Morph"/>, because a failure here would make
/// the settings file unreadable and reset every setting. No reflection, so it
/// works under Native AOT.
/// </summary>
public sealed class ThemeTransitionKindConverter : JsonConverter<ThemeTransitionKind>
{
    /// <summary>What an unknown value reads as: the default setting.</summary>
    public const ThemeTransitionKind Fallback = ThemeTransitionKind.Morph;

    public override ThemeTransitionKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.Number:
                return reader.TryGetInt32(out var number) && Enum.IsDefined((ThemeTransitionKind)number)
                    ? (ThemeTransitionKind)number
                    : Fallback;

            default:
                // An object or array is skipped whole, so the rest of the file still reads.
                reader.Skip();
                return Fallback;
        }
    }

    public override void Write(Utf8JsonWriter writer, ThemeTransitionKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : Fallback.ToString());

    /// <summary>A member's name, in any case; anything else is the fallback.</summary>
    public static ThemeTransitionKind Parse(string? name)
    {
        foreach (var kind in Enum.GetValues<ThemeTransitionKind>())
        {
            if (string.Equals(kind.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return Fallback;
    }
}

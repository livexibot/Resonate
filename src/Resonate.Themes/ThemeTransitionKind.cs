using System.Text.Json.Serialization;

namespace Resonate.Themes;

/// <summary>How the window changes from one look to another.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemeTransitionKind>))]
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
}

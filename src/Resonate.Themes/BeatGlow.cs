namespace Resonate.Themes;

/// <summary>
/// Beat glow, a built-in plugin: how strongly the window's edge glows from
/// the music's low end. The analyser's lowest bands (bass and kick drum)
/// give the level; it jumps up with each beat and fades between them.
/// </summary>
public static class BeatGlow
{
    /// <summary>How many of the lowest bands count as the beat.</summary>
    public const int BassBands = 6;

    /// <summary>The tallest a band is (the analyser's rows).</summary>
    public const float Rows = 15f;

    /// <summary>How fast the glow fades, in levels a second.</summary>
    public const float Fade = 2.4f;

    /// <summary>The bass level, 0 to 1, from the analyser's bands (heights in rows, lowest first).</summary>
    public static float Bass(ReadOnlySpan<float> bands)
    {
        var count = Math.Min(BassBands, bands.Length);
        if (count == 0)
        {
            return 0;
        }

        var sum = 0f;
        for (var i = 0; i < count; i++)
        {
            sum += bands[i];
        }

        // Squared, so quiet passages stay dark and beats stand out.
        var level = Math.Clamp(sum / count / Rows, 0f, 1f);
        return level * level;
    }

    /// <summary>The glow after <paramref name="seconds"/>: straight up to <paramref name="target"/>, fading down at <see cref="Fade"/>.</summary>
    public static float Follow(float level, float target, float seconds) =>
        target >= level ? target : Math.Max(target, level - (Fade * Math.Max(0, seconds)));
}

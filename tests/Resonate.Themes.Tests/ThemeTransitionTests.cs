using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Resonate.Themes.Tests;

/// <summary>Settings hold the switching animation like this (AppSettings lives in the WinUI app).</summary>
public sealed class TransitionSetting
{
    public ThemeTransitionKind ThemeTransition { get; set; } = ThemeTransitionKind.Morph;

    public string Name { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransitionSetting))]
internal sealed partial class TransitionJsonContext : JsonSerializerContext;

public sealed class ThemeTransitionKindTests
{
    // Saved settings hold these names; renaming one would lose the user's choice.
    [Theory]
    [InlineData(ThemeTransitionKind.Morph, "Morph", 0)]
    [InlineData(ThemeTransitionKind.Ripple, "Ripple", 1)]
    [InlineData(ThemeTransitionKind.Split, "Split", 2)]
    [InlineData(ThemeTransitionKind.Blinds, "Blinds", 3)]
    [InlineData(ThemeTransitionKind.Wipe, "Wipe", 4)]
    [InlineData(ThemeTransitionKind.Random, "Random", 5)]
    [InlineData(ThemeTransitionKind.None, "None", 6)]
    [InlineData(ThemeTransitionKind.Fade, "Fade", 7)]
    [InlineData(ThemeTransitionKind.Grow, "Grow", 8)]
    public void Every_kind_is_saved_by_its_name_and_reads_back(ThemeTransitionKind kind, string name, int number)
    {
        Assert.Equal(number, (int)kind);
        var json = JsonSerializer.Serialize(new TransitionSetting { ThemeTransition = kind }, TransitionJsonContext.Default.TransitionSetting);
        Assert.Contains($"\"themeTransition\":\"{name}\"", json, StringComparison.Ordinal);
        Assert.Equal(kind, JsonSerializer.Deserialize(json, TransitionJsonContext.Default.TransitionSetting)!.ThemeTransition);
    }

    [Fact]
    public void Every_kind_is_pinned_by_the_test_above() =>
        Assert.Equal(9, Enum.GetValues<ThemeTransitionKind>().Length);

    [Theory]
    [InlineData("\"Sparkle\"")]
    [InlineData("\"\"")]
    [InlineData("\"5,6\"")]
    [InlineData("42")]
    [InlineData("-1")]
    [InlineData("3.5")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{\"kind\":\"Ripple\"}")]
    [InlineData("[\"Ripple\"]")]
    public void An_unknown_value_reads_as_Morph_and_keeps_the_other_settings(string value)
    {
        var json = $"{{\"themeTransition\":{value},\"name\":\"kept\"}}";
        var setting = JsonSerializer.Deserialize(json, TransitionJsonContext.Default.TransitionSetting)!;
        Assert.Equal(ThemeTransitionKind.Morph, setting.ThemeTransition);
        Assert.Equal("kept", setting.Name);
    }

    [Theory]
    [InlineData("\"ripple\"", ThemeTransitionKind.Ripple)]
    [InlineData("\" Grow \"", ThemeTransitionKind.Grow)]
    [InlineData("3", ThemeTransitionKind.Blinds)]
    public void Known_names_in_any_case_and_known_numbers_still_read(string value, ThemeTransitionKind expected)
    {
        var json = $"{{\"themeTransition\":{value}}}";
        Assert.Equal(expected, JsonSerializer.Deserialize(json, TransitionJsonContext.Default.TransitionSetting)!.ThemeTransition);
    }

    [Fact]
    public void An_undefined_value_is_written_as_Morph()
    {
        var json = JsonSerializer.Serialize(new TransitionSetting { ThemeTransition = (ThemeTransitionKind)99 }, TransitionJsonContext.Default.TransitionSetting);
        Assert.Contains("\"themeTransition\":\"Morph\"", json, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Resonate.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository was not found above the test's folder.");
    }
}

public sealed class ThemeTransitionCatalogTests
{
    private static IEnumerable<ThemeTransitionKind> Animated =>
        Enum.GetValues<ThemeTransitionKind>().Where(k => k != ThemeTransitionKind.None);

    [Fact]
    public void Surprise_me_picks_from_every_kind_that_animates()
    {
        var expected = Enum.GetValues<ThemeTransitionKind>().Where(k => k is not (ThemeTransitionKind.Random or ThemeTransitionKind.None));
        Assert.Equal(expected.Order(), ThemeTransitionCatalog.RandomPool.Order());
    }

    [Fact]
    public void Random_resolves_to_each_kind_in_the_pool_and_nothing_else()
    {
        var seen = Enumerable.Range(0, ThemeTransitionCatalog.RandomPool.Count)
            .Select(i => ThemeTransitionCatalog.Resolve(ThemeTransitionKind.Random, _ => i))
            .ToHashSet();
        Assert.Equal(ThemeTransitionCatalog.RandomPool.ToHashSet(), seen);

        // A picker that goes out of range still lands on a kind from the pool.
        Assert.Contains(ThemeTransitionCatalog.Resolve(ThemeTransitionKind.Random, n => n + 5), ThemeTransitionCatalog.RandomPool);
        Assert.Contains(ThemeTransitionCatalog.Resolve(ThemeTransitionKind.Random, _ => -1), ThemeTransitionCatalog.RandomPool);
    }

    [Fact]
    public void Other_kinds_resolve_to_themselves()
    {
        foreach (var kind in Enum.GetValues<ThemeTransitionKind>().Where(k => k != ThemeTransitionKind.Random))
        {
            Assert.Equal(kind, ThemeTransitionCatalog.Resolve(kind, _ => 0));
        }
    }

    [Fact]
    public void Every_kind_but_None_lasts_one_to_two_seconds()
    {
        foreach (var kind in Animated)
        {
            var duration = ThemeTransitionCatalog.Spec(kind).Duration;
            Assert.InRange(duration, TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(1.6));
        }

        Assert.Equal(TimeSpan.Zero, ThemeTransitionCatalog.Spec(ThemeTransitionKind.None).Duration);
    }

    [Fact]
    public void Every_kind_but_None_eases_in_and_out()
    {
        foreach (var kind in Animated)
        {
            var curve = ThemeTransitionCatalog.Spec(kind).Curve;
            Assert.True(curve.EasesInAndOut, $"{kind} does not ease in and out: {curve}");
            AssertComesToRestAtBothEnds(curve);
        }
    }

    [Fact]
    public void Quick_edits_are_a_short_eased_cross_fade()
    {
        var quick = ThemeTransitionCatalog.QuickEdit;
        Assert.InRange(quick.Duration, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(400));
        Assert.True(quick.Curve.EasesInAndOut);
    }

    [Theory]
    [InlineData(0, 1200)]
    [InlineData(500, 1200)]
    [InlineData(2500, 1300)]
    [InlineData(4000, 1480)]
    [InlineData(20000, 1600)]
    public void A_ripple_takes_longer_the_further_it_reaches(double reach, double milliseconds) =>
        Assert.Equal(milliseconds, ThemeTransitionCatalog.RippleDuration(reach).TotalMilliseconds, 3);

    [Fact]
    public void Curves_run_from_start_to_end_without_going_back()
    {
        foreach (var curve in new[] { ThemeTransitionCatalog.Emphasized, ThemeTransitionCatalog.Soft, ThemeTransitionCatalog.Gentle, EasingCurve.Linear })
        {
            Assert.Equal(0, curve.Evaluate(0), 9);
            Assert.Equal(1, curve.Evaluate(1), 9);
            Assert.Equal(0, curve.Evaluate(-0.5), 9);
            Assert.Equal(1, curve.Evaluate(1.5), 9);
            var previous = 0.0;
            for (var i = 1; i <= 1000; i++)
            {
                var value = curve.Evaluate(i / 1000.0);
                Assert.True(value >= previous - 1e-9, $"{curve} goes back at {i / 1000.0}");
                previous = value;
            }
        }
    }

    [Fact]
    public void Curves_match_their_known_shapes()
    {
        Assert.Equal(0.3, EasingCurve.Linear.Evaluate(0.3), 6);

        // Symmetric curves are halfway at half time.
        Assert.Equal(0.5, ThemeTransitionCatalog.Emphasized.Evaluate(0.5), 6);
        Assert.Equal(0.5, ThemeTransitionCatalog.Soft.Evaluate(0.5), 6);

        // The emphasized curve is close to the ease-in-out cubic it stands for.
        foreach (var t in new[] { 0.1, 0.25, 0.4, 0.6, 0.75, 0.9 })
        {
            var cubic = t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
            Assert.InRange(ThemeTransitionCatalog.Emphasized.Evaluate(t), cubic - 0.03, cubic + 0.03);
        }
    }

    [Fact]
    public void Curves_that_start_or_end_at_speed_are_not_eased()
    {
        Assert.False(EasingCurve.Linear.EasesInAndOut);
        Assert.False(new EasingCurve(0.1, 0.9, 0.2, 1).EasesInAndOut);
        Assert.False(new EasingCurve(0.7, 0, 1, 0.5).EasesInAndOut);
        Assert.False(new EasingCurve(0, 0, 0.35, 1).EasesInAndOut);
    }

    [Fact]
    public void A_growing_shape_reveals_its_area_along_the_curve()
    {
        Assert.Equal(0, ThemeTransitionCatalog.RevealScale(0));
        Assert.Equal(1, ThemeTransitionCatalog.RevealScale(1));
        Assert.Equal(0, ThemeTransitionCatalog.RevealScale(-0.2));
        Assert.Equal(1, ThemeTransitionCatalog.RevealScale(1.2));

        var previous = 0.0;
        for (var i = 1; i <= 1000; i++)
        {
            var time = i / 1000.0;
            var progress = ThemeTransitionCatalog.Emphasized.Evaluate(time);
            var scale = ThemeTransitionCatalog.RevealScale(progress);
            Assert.True(scale >= previous, $"The shape shrinks at {time}");

            // Area grows with the square of the size, so it follows the curve exactly.
            Assert.Equal(progress, scale * scale, 9);
            previous = scale;
        }

        // At half time half the window is new.
        Assert.Equal(Math.Sqrt(0.5), ThemeTransitionCatalog.RevealScale(ThemeTransitionCatalog.Emphasized.Evaluate(0.5)), 6);
    }

    [Theory]
    [InlineData(1280, 820, 640, 410)]
    [InlineData(1280, 820, 0, 0)]
    [InlineData(1280, 820, 1100, 90)]
    [InlineData(3413, 1440, 400, 300)]
    public void A_ripple_reaches_every_corner(double width, double height, double x, double y)
    {
        var reach = ThemeTransitionCatalog.Reach(width, height, x, y);
        foreach (var (cx, cy) in new[] { (0.0, 0.0), (width, 0.0), (0.0, height), (width, height) })
        {
            Assert.True(Math.Sqrt(Math.Pow(cx - x, 2) + Math.Pow(cy - y, 2)) <= reach + 1e-9);
        }

        Assert.Contains(new[] { (0.0, 0.0), (width, 0.0), (0.0, height), (width, height) }, c => Math.Abs(Math.Sqrt(Math.Pow(c.Item1 - x, 2) + Math.Pow(c.Item2 - y, 2)) - reach) < 1e-9);
    }

    [Theory]
    [InlineData(1280, 820)]
    [InlineData(800, 600)]
    [InlineData(3413, 1440)]
    [InlineData(5120, 2160)]
    [InlineData(10, 10)]
    public void The_shape_that_grows_from_the_middle_covers_the_window_at_full_size(double width, double height)
    {
        var (corner, margin) = ThemeTransitionCatalog.GrowShape(width, height);
        Assert.InRange(corner, 0, 96);
        Assert.True(margin > 0);

        // The window's corner, seen from the centre of the shape's rounded corner (which sits at
        // corner − margin inside the window on each axis), must be within the corner's radius.
        var inset = corner - margin;
        Assert.True(inset <= 0 || Math.Sqrt(2) * inset <= corner - 1, $"The window's corner shows at {width}x{height}");
    }

    [Fact]
    public void Blinds_fold_one_after_another_and_all_finish()
    {
        const int Count = 10;
        Assert.InRange(ThemeTransitionCatalog.BlindsShare(Count), 0.3, 0.9);
        for (var i = 0; i < Count; i++)
        {
            Assert.Equal(0, ThemeTransitionCatalog.StripProgress(0, i, Count));
            Assert.Equal(1, ThemeTransitionCatalog.StripProgress(1, i, Count), 9);
            var previous = 0.0;
            for (var p = 0; p <= 100; p++)
            {
                var own = ThemeTransitionCatalog.StripProgress(p / 100.0, i, Count);
                Assert.True(own >= previous);
                previous = own;
            }
        }

        // The first strip starts at once, the last one only after the others.
        Assert.True(ThemeTransitionCatalog.StripProgress(0.01, 0, Count) > 0);
        Assert.Equal(0, ThemeTransitionCatalog.StripProgress(0.3, Count - 1, Count));
        Assert.True(ThemeTransitionCatalog.StripProgress(0.5, 0, Count) > ThemeTransitionCatalog.StripProgress(0.5, Count - 1, Count));
    }

    [Fact]
    public void Only_Ripple_and_Grow_reveal_the_live_window()
    {
        var live = Enum.GetValues<ThemeTransitionKind>().Where(ThemeTransitionCatalog.RevealsLive);
        Assert.Equal(new[] { ThemeTransitionKind.Ripple, ThemeTransitionKind.Grow }, live);
    }

    /// <summary>No speed at either end: the first and last thousandth of the time cover far less than a thousandth of the way.</summary>
    private static void AssertComesToRestAtBothEnds(EasingCurve curve)
    {
        const double Step = 0.001;
        Assert.True(curve.Evaluate(Step) < Step / 10, $"{curve} starts at speed");
        Assert.True(1 - curve.Evaluate(1 - Step) < Step / 10, $"{curve} ends at speed");
    }
}

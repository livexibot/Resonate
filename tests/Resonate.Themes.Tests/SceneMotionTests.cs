using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Resonate.Themes.Tests;

/// <summary>The moving parts of the special looks' scenery: their tags, expressions and rests.</summary>
public sealed partial class SceneMotionTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void A_tag_becomes_expressions_for_what_it_moves_only()
    {
        var twinkle = SceneMotion.Parse("twinkle p=8 ph=0.25 lo=0.2 o=0.6");
        Assert.Null(twinkle.Translation);
        Assert.Null(twinkle.Scale);
        Assert.Null(twinkle.Rotation);
        Assert.NotNull(twinkle.Opacity);
        Assert.StartsWith("0.6 * ", twinkle.Opacity, StringComparison.Ordinal);
        Assert.Equal(0.6, twinkle.BaseOpacity);
        Assert.Equal(0.6, twinkle.RestOpacity);

        var both = SceneMotion.Parse("scroll p=40 dx=-600; sway p=20 dy=4");
        Assert.NotNull(both.Translation);
        Assert.Null(both.Opacity);
        Assert.Contains("(-600)", both.Translation, StringComparison.Ordinal);

        var swing = SceneMotion.Parse("swing p=24 a=12 cx=820 cy=340");
        Assert.NotNull(swing.Rotation);
        Assert.Equal((820.0, 340.0), (swing.CentreX, swing.CentreY));

        // What crosses the scene is not there while nothing moves; what glows stays.
        Assert.Equal(0, SceneMotion.Parse("shoot p=37 dx=500 dy=100").RestOpacity);
        Assert.Equal(0, SceneMotion.Parse("travel p=40 dx=2600").RestOpacity);
        Assert.Equal(1, SceneMotion.Parse("blink p=3 w=0.3").RestOpacity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("o=0.5")]
    [InlineData("wobble p=3")]
    [InlineData("twinkle lo=0.2")]
    [InlineData("twinkle p=0")]
    [InlineData("twinkle p=8 dx=3")]
    [InlineData("twinkle p=eight")]
    [InlineData("swing p=8 a=3 cx=1; pulse p=8 s=0.1 cx=2")]
    [InlineData("approach p=8 top=300 bottom=600 y0=200")]
    public void A_tag_that_does_not_make_sense_is_refused(string tag) =>
        Assert.Throws<FormatException>(() => SceneMotion.Parse(tag));

    [Theory]
    [InlineData(8, 8)]
    [InlineData(7, 7.017544)]
    [InlineData(1200, 1200)]
    [InlineData(5000, 1200)]
    [InlineData(37, 37.5)]
    public void Every_period_repeats_a_whole_number_of_times_per_loop(double seconds, double fits)
    {
        var frequency = SceneMotion.Frequency(seconds);
        var turns = frequency * SceneWeather.LoopSeconds;
        Assert.Equal(Math.Round(turns), turns, 9);
        Assert.Equal(fits, 1 / frequency, 6);
    }

    [Fact]
    public void Every_moving_part_of_the_scenery_has_a_motion_and_a_name_the_app_finds()
    {
        var art = XDocument.Load(Path.Combine(AppFolder(), "Controls", "SceneArt.xaml"));
        var scenes = art.Descendants().Where(e => e.Attribute(X + "Load") is not null).ToList();
        Assert.Equal(Enum.GetValues<ThemeScene>().Length - 1, scenes.Count);
        var moving = 0;
        foreach (var scene in scenes)
        {
            // The scene's Tag: the prefix of its moving parts' names and how many there are, numbered from 0.
            var tag = scene.Attribute("Tag")!.Value.Split(' ');
            var (prefix, count) = (tag[0], int.Parse(tag[1], System.Globalization.CultureInfo.InvariantCulture));
            var parts = scene.Descendants().Where(e => e.Attribute(X + "Name") is not null).ToList();
            Assert.Equal(
                Enumerable.Range(0, count).Select(i => $"{prefix}M{i}").Order(StringComparer.Ordinal),
                parts.Select(p => p.Attribute(X + "Name")!.Value).Order(StringComparer.Ordinal));
            foreach (var part in parts)
            {

                // The app sets a moving part's opacity itself, so XAML must not.
                Assert.Null(part.Attribute("Opacity"));
                var motion = SceneMotion.Parse(part.Attribute("Tag")!.Value);
                foreach (var expression in (string?[])[motion.Translation, motion.Opacity, motion.Scale, motion.Rotation])
                {
                    if (expression is not null)
                    {
                        Assert.Equal(expression.Count(c => c == '('), expression.Count(c => c == ')'));
                        Assert.DoesNotMatch(Unknown(), expression);
                    }
                }
            }

            moving += count;
        }

        Assert.True(moving > 60);
    }

    // Anything but the clock's time, the functions the compositor knows, numbers and operators.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.])(?!(?:c\.Time|Mod|Sin|Cos|Clamp|Max|Min|Floor|Vector3)\b)[A-Za-z_][A-Za-z_.]*")]
    private static partial Regex Unknown();

    private static string AppFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Resonate.slnx")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(
            folder?.FullName ?? throw new InvalidOperationException("The repository was not found above the test's folder."),
            "src",
            "Resonate.App");
    }
}

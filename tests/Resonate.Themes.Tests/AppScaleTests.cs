using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Resonate.Themes.Tests;

public sealed partial class AppScaleTests
{
    [Theory]
    [InlineData(100, 100)]
    [InlineData(0, 50)]
    [InlineData(-50, 50)]
    [InlineData(500, 300)]
    [InlineData(122, 120)]
    [InlineData(102, 100)]
    [InlineData(141, 133)] // halfway: the smaller step
    [InlineData(142, 150)]
    public void Nearest_lands_any_setting_on_an_app_size(int saved, int expected) =>
        Assert.Equal(expected, AppScale.Nearest(saved, AppScale.AppSizes));

    [Theory]
    [InlineData(100, 105)]
    [InlineData(125, 133)]
    [InlineData(175, 200)]
    [InlineData(300, 300)]
    [InlineData(10, 50)]
    public void Larger_takes_the_next_step_up_and_stops_at_the_largest(int from, int expected) =>
        Assert.Equal(expected, AppScale.Larger(from, AppScale.AppSizes));

    [Theory]
    [InlineData(100, 95)]
    [InlineData(125, 120)]
    [InlineData(50, 50)]
    [InlineData(1000, 300)]
    public void Smaller_takes_the_next_step_down_and_stops_at_the_smallest(int from, int expected) =>
        Assert.Equal(expected, AppScale.Smaller(from, AppScale.AppSizes));

    [Fact]
    public void Steps_rise_and_include_the_usual_size()
    {
        foreach (var steps in new[] { AppScale.AppSizes, AppScale.TextSizes, AppScale.FontSizes })
        {
            Assert.Equal(steps.Order(), steps);
            Assert.Equal(steps.Count, steps.Distinct().Count());
        }

        Assert.Contains(AppScale.Normal, AppScale.AppSizes);
        Assert.Contains(AppScale.Normal, AppScale.TextSizes);
    }

    [Theory]
    [InlineData(14, 100, 14)]
    [InlineData(14, 150, 21)]
    [InlineData(12, 125, 15)]
    [InlineData(48, 90, 43.2)]
    public void Font_scales_text_by_the_text_size(double size, int percent, double expected) =>
        Assert.Equal(expected, AppScale.Font(size, percent), 6);

    [Theory]
    [InlineData(760, 100, 3000, 760)]
    [InlineData(760, 200, 3000, 1520)]
    [InlineData(1140, 125, 5120, 1425)]
    [InlineData(760, 200, 1280, 1280)] // never larger than the screen
    [InlineData(760, 80, 3000, 608)]
    public void The_smallest_window_grows_with_the_app_size_within_the_screen(int minimum, int percent, int available, int expected) =>
        Assert.Equal(expected, AppScale.MinimumWindow(minimum, percent, available));

    [Theory]
    [InlineData(75, 30, 56)]
    [InlineData(100, 40, 56)]
    [InlineData(150, 60, 76)]
    [InlineData(200, 80, 96)]
    public void Larger_covers_keep_their_room_in_the_row(int percent, int cover, double row)
    {
        Assert.Equal(cover, AppScale.Cover(40, percent));
        Assert.Equal(row, AppScale.CoverRow(56, 40, AppScale.Cover(40, percent)));
    }

    [Fact]
    public void Labels_are_whole_percentages() => Assert.Equal("125%", AppScale.Label(125));

    [Fact]
    public void Tokens_define_exactly_the_text_sizes_the_theme_service_scales()
    {
        var tokens = XDocument.Load(Path.Combine(AppFolder(), "Themes", "Tokens.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var defined = tokens.Descendants()
            .Select(e => (string?)e.Attribute(x + "Key"))
            .Where(key => key is not null && key.StartsWith("ResonateFontSize", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(AppScale.FontSizes.Select(AppScale.FontKey), defined);
        foreach (var size in AppScale.FontSizes)
        {
            var value = tokens.Descendants().Single(e => (string?)e.Attribute(x + "Key") == AppScale.FontKey(size)).Value;
            Assert.Equal(size.ToString(System.Globalization.CultureInfo.InvariantCulture), value);
        }
    }

    [Fact]
    public void Every_text_size_used_in_the_app_is_a_token_that_scales()
    {
        var used = Directory.EnumerateFiles(AppFolder(), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(file => FontToken().Matches(File.ReadAllText(file)).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, size => Assert.Contains(size, AppScale.FontSizes));
    }

    [Fact]
    public void Text_is_never_given_a_plain_size_in_xaml()
    {
        // Icons keep their sizes; anything else must use a ResonateFontSize token.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var plain = new List<string>();
        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (var element in XDocument.Load(file).Descendants())
            {
                var size = (string?)element.Attribute("FontSize");
                if (size is null || !char.IsAsciiDigit(size[0]) || IsIcon(element))
                {
                    continue;
                }

                plain.Add($"{Path.GetFileName(file)}: {element.Name.LocalName} {(string?)element.Attribute(x + "Name")} {size}");
            }
        }

        Assert.Empty(plain);
    }

    // A glyph from the icon font, or a button whose content is one.
    private static bool IsIcon(XElement element) =>
        element.Name.LocalName == "FontIcon"
        || ((string?)element.Attribute("FontFamily"))?.Contains("ResonateIconFont", StringComparison.Ordinal) == true
        || ((string?)element.Attribute("Style"))?.Contains("ResonateIconButtonStyle", StringComparison.Ordinal) == true;

    [GeneratedRegex(@"ResonateFontSize(\d+)")]
    private static partial Regex FontToken();

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

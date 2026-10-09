using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace Resonate.Themes.Tests;

/// <summary>
/// The now-playing stage's pictures made in code: the dithered cloud mask
/// that keeps the clouds free of rings, the PNG it travels in, and the
/// visualizer's bars.
/// </summary>
public sealed class StageVisualsTests
{
    [Fact]
    public void The_png_holds_exactly_the_pixels_it_was_given()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 11);
        }

        var png = PngWriter.Write(pixels, 3, 2);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, header[8..]);

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var rows = inflated.ToArray();
        Assert.Equal(2 * (1 + (3 * 4)), rows.Length);
        Assert.Equal(0, rows[0]);
        Assert.Equal(pixels[..12], rows[1..13]);
        Assert.Equal(0, rows[13]);
        Assert.Equal(pixels[12..], rows[14..]);
    }

    [Fact]
    public void The_crc_is_the_one_png_readers_check()
    {
        // The standard check value of CRC-32.
        Assert.Equal(0xCBF43926u, PngWriter.Crc("123456789"u8));
    }

    [Fact]
    public void The_cloud_fades_from_full_in_the_middle_to_nothing_at_the_rim()
    {
        Assert.Equal(1, CloudMask.Falloff(0));
        Assert.Equal(0.5625, CloudMask.Falloff(0.5), 6);
        Assert.Equal(0, CloudMask.Falloff(1));
        Assert.Equal(0, CloudMask.Falloff(1.4));

        // No step at the rim: the last stretch before it is already almost nothing.
        Assert.True(CloudMask.Falloff(0.97) < 0.004);
        for (var r = 0.0; r < 1; r += 0.01)
        {
            Assert.True(CloudMask.Falloff(r + 0.01) <= CloudMask.Falloff(r));
        }
    }

    [Fact]
    public void The_mask_is_the_same_every_time_and_white()
    {
        var first = CloudMask.Pixels(64);
        Assert.Equal(first, CloudMask.Pixels(64));
        for (var i = 0; i < first.Length; i += 4)
        {
            Assert.Equal(0xFF, first[i]);
            Assert.Equal(0xFF, first[i + 1]);
            Assert.Equal(0xFF, first[i + 2]);
        }
    }

    [Fact]
    public void The_mask_is_dithered_but_keeps_its_average_and_its_empty_corners()
    {
        const int size = CloudMask.Size;
        var pixels = CloudMask.Pixels();
        var half = size / 2.0;
        double error = 0;
        var samples = 0;
        var differs = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5 - half) / half;
                var dy = (y + 0.5 - half) / half;
                var radius = Math.Sqrt((dx * dx) + (dy * dy));
                var exact = CloudMask.Falloff(radius) * 255;
                var alpha = pixels[(((y * size) + x) * 4) + 3];
                Assert.True(Math.Abs(alpha - exact) <= CloudMask.Dither + 0.5, $"({x}, {y}) is {alpha}, about {exact:0.0} expected");
                if (radius >= 1)
                {
                    Assert.Equal(0, alpha);
                }

                // A ring around the middle: plain rounding would give long runs of one value there.
                if (radius is > 0.4 and < 0.8)
                {
                    error += alpha - exact;
                    samples++;
                    if (alpha != (int)Math.Round(exact))
                    {
                        differs++;
                    }
                }
            }
        }

        Assert.True(Math.Abs(error / samples) < 0.05, $"The dithered mask is off by {error / samples:0.000} on average");
        Assert.True(differs > samples / 3, "The mask is hardly dithered");
    }

    [Fact]
    public void The_mask_png_is_a_square_of_the_mask_size()
    {
        var png = CloudMask.Png(32);
        var header = Chunks(png)[0].Data;
        Assert.Equal(32, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(32, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
    }

    [Theory]
    [InlineData(0, StageBars.MinCount)]
    [InlineData(100, StageBars.MinCount)]
    [InlineData(700, 50)]
    [InlineData(5000, StageBars.MaxCount)]
    public void As_many_bars_fit_as_the_width_allows(double width, int expected) =>
        Assert.Equal(expected, StageBars.Count(width));

    [Theory]
    [InlineData(2000, 64, 64)]
    [InlineData(2000, 200, StageBars.MaxCount)]
    [InlineData(200, 64, 40)]
    [InlineData(50, 64, StageBars.MinCount)]
    public void The_users_bar_count_is_kept_while_it_fits(double width, int wanted, int expected) =>
        Assert.Equal(expected, StageBars.Count(width, wanted));

    [Fact]
    public void Bars_rise_quickly_and_fall_slowly_at_any_refresh_rate()
    {
        // One second at 60 and at 165 frames a second ends in the same place.
        static float Run(float level, float target, int fps)
        {
            for (var i = 0; i < fps; i++)
            {
                level = StageBars.Smooth(level, target, 1f / fps, 0.6);
            }

            return level;
        }

        Assert.Equal(Run(0, 1, 60), Run(0, 1, 165), 3);
        var risen = StageBars.Smooth(0, 1, 0.05f, 0.6);
        var fallen = 1 - StageBars.Smooth(1, 0, 0.05f, 0.6);
        Assert.True(risen > fallen, $"rose {risen}, fell {fallen}");
        Assert.InRange(StageBars.Smooth(0.5f, 0.5f, 0.05f, 0.6), 0.5f, 0.5f);
    }

    [Fact]
    public void Smoothing_slows_the_sway_and_the_default_keeps_its_pace()
    {
        Assert.Equal(1f, StageBars.SwayPace(0.6), 4);
        Assert.True(StageBars.SwayPace(0) > 1.5f);
        Assert.True(StageBars.SwayPace(1) is > 0.3f and < 0.5f);
        Assert.Equal(StageBars.SwayPace(1), StageBars.SwayPace(3));
    }

    [Fact]
    public void Bars_reach_a_quarter_of_the_stage_at_most() =>
        Assert.Equal([0, 26, StageBars.MaxHeight], new[] { 0.0, 100, 4000 }.Select(StageBars.Height));

    [Fact]
    public void The_made_up_motion_stays_in_range_and_repeats_without_a_jump()
    {
        const int count = 48;
        for (var i = 0; i < count; i++)
        {
            for (var t = 0.0; t < 30; t += 0.37)
            {
                var level = StageBars.Synthetic(i, count, t);
                Assert.InRange(level, 0.02, 1);
            }

            Assert.Equal(StageBars.Synthetic(i, count, 0.25), StageBars.Synthetic(i, count, StageBars.LoopSeconds + 0.25), 6);
        }
    }

    [Fact]
    public void The_lows_grow_taller_than_the_highs()
    {
        const int count = 64;
        var lows = Enumerable.Range(0, count / 4).Average(i => StageBars.Shape(i, count));
        var highs = Enumerable.Range(count * 3 / 4, count / 4).Average(i => StageBars.Shape(i, count));
        Assert.True(lows > highs + 0.2, $"lows {lows:0.00}, highs {highs:0.00}");
    }

    [Fact]
    public void The_bars_do_not_move_together()
    {
        var speeds = Enumerable.Range(0, 32).Select(i => StageBars.Motion(i).Speed1).Distinct().Count();
        Assert.True(speeds > 16);
    }

    [Fact]
    public void The_expression_is_written_the_same_in_every_language()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var expression = StageBars.SyntheticExpression(5, 40, "p.Time");
            Assert.DoesNotContain(",", expression, StringComparison.Ordinal);
            Assert.Contains("Sin(p.Time*", expression, StringComparison.Ordinal);
            Assert.Equal(expression.Count(c => c == '('), expression.Count(c => c == ')'));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_spectrum_spreads_over_the_bars()
    {
        var bands = new float[75];
        bands[0] = 15;
        bands[StageBars.UsedBands - 1] = 0;
        bands[74] = 15;
        var bars = new float[12];

        StageBars.Resample(bands, bars);

        Assert.Equal(1f, bars[0]);
        Assert.Equal(0f, bars[^1]);
        Assert.All(bars, b => Assert.InRange(b, 0f, 1f));

        // Quiet bands are lifted a little: a quarter of the rows shows as more than a quarter.
        Array.Fill(bands, 15 / 4f);
        StageBars.Resample(bands, bars);
        Assert.All(bars, b => Assert.InRange(b, 0.3f, 0.4f));
    }

    [Theory]
    [InlineData("#3A2A18", "#121212")]
    [InlineData("#101010", "#000000")]
    [InlineData("#F4E9D8", "#FFFFFF")]
    [InlineData("#E05030", "#202020")]
    public void Bars_stand_out_from_the_page(string colour, string page)
    {
        Assert.True(ThemeColor.TryParse(colour, out var bar));
        Assert.True(ThemeColor.TryParse(page, out var background));

        var shown = StageColours.ForBars(bar, background);

        Assert.True(ThemeColor.ContrastRatio(shown, background) >= 3, $"{shown} on {page}");
        Assert.Equal(0xFF, shown.A);
    }

    [Fact]
    public void A_bright_bar_colour_is_kept_as_it_is()
    {
        Assert.True(ThemeColor.TryParse("#E05030", out var bar));
        Assert.Equal(bar, StageColours.ForBars(bar, ThemeColor.Black));
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = png.AsSpan(at + 4, 4);
            var data = png.AsSpan(at + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(PngWriter.Crc(data, PngWriter.Crc(type)), crc);
            chunks.Add((System.Text.Encoding.ASCII.GetString(type), data));
            at += 12 + length;
        }

        return chunks;
    }
}

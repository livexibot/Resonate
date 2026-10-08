using System.Buffers.Binary;
using System.Text;
using Resonate.Themes.Skins;
using static Resonate.Themes.Tests.SkinFixtures;

namespace Resonate.Themes.Tests;

public sealed class BmpDecoderTests
{
    private const uint Black = 0xFF000000;

    [Theory]
    [InlineData(1, 40)]
    [InlineData(2, 40)]
    [InlineData(4, 40)]
    [InlineData(8, 40)]
    [InlineData(1, 12)]
    [InlineData(4, 12)]
    [InlineData(8, 12)]
    [InlineData(4, 64)]
    [InlineData(8, 108)]
    [InlineData(8, 124)]
    public void Reads_palettised_pictures_with_every_header(int bitCount, int headerSize)
    {
        // An odd width, so rows need padding.
        const int width = 13;
        const int height = 5;
        var palette = Palette(1 << bitCount);
        int Index(int x, int y) => (x + (3 * y)) % palette.Length;

        var bmp = Bmp(width, height, bitCount, Rows(width, height, bitCount, (x, y) => (uint)Index(x, y)), palette, headerSize: headerSize);

        AssertSame(Image(width, height, (x, y) => palette[Index(x, y)]), BmpDecoder.Decode(bmp));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(32)]
    public void Reads_top_down_pictures(int bitCount)
    {
        const int width = 7;
        const int height = 4;
        var palette = bitCount == 8 ? Palette(256) : null;
        var expected = Pattern(width, height);
        uint Value(int x, int y) => bitCount == 8 ? (uint)((x * 5) + y) : expected[x, y] & 0xFFFFFF;

        var bmp = Bmp(width, -height, bitCount, Rows(width, height, bitCount, Value, topDown: true), palette);

        var picture = BmpDecoder.Decode(bmp);
        AssertSame(bitCount == 8 ? Image(width, height, (x, y) => palette![Value(x, y)]) : expected, picture);
    }

    [Fact]
    public void Reads_24_bit_pictures()
    {
        var expected = Pattern(10, 6, seed: 3);

        var bmp = Bmp(10, 6, 24, Rows(10, 6, 24, (x, y) => expected[x, y] & 0xFFFFFF));

        AssertSame(expected, BmpDecoder.Decode(bmp));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x7F)]
    public void Ignores_alpha_in_32_bit_pictures(byte alpha)
    {
        var expected = Pattern(9, 3, seed: 5);

        var bmp = Bmp(9, 3, 32, Rows(9, 3, 32, (x, y) => (expected[x, y] & 0xFFFFFF) | ((uint)alpha << 24)));

        AssertSame(expected, BmpDecoder.Decode(bmp));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(56)]
    [InlineData(108)]
    [InlineData(124)]
    public void Reads_32_bit_fields_in_any_order(int headerSize)
    {
        var expected = Pattern(6, 5, seed: 7);
        uint Swapped(int x, int y)
        {
            var c = expected[x, y];
            return ((c >> 16) & 0xFF) | (c & 0xFF00) | ((c & 0xFF) << 16);
        }

        var bmp = Bmp(6, 5, 32, Rows(6, 5, 32, Swapped), compression: BitFields, headerSize: headerSize, masks: [0x000000FF, 0x0000FF00, 0x00FF0000]);

        AssertSame(expected, BmpDecoder.Decode(bmp));
    }

    [Fact]
    public void Reads_16_bit_555_pictures()
    {
        var bmp = Bmp(32, 3, 16, Rows(32, 3, 16, (x, y) => (uint)((x << 10) | (((x + y) % 32) << 5) | (31 - x))));

        var picture = BmpDecoder.Decode(bmp);

        Assert.Equal(Expand(0, 0, 31, 5), picture[0, 0]);
        Assert.Equal(Expand(31, 31, 0, 5), picture[31, 0]);
        Assert.Equal(Expand(10, 12, 21, 5), picture[10, 2]);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(108)]
    public void Reads_16_bit_565_pictures(int headerSize)
    {
        var bmp = Bmp(32, 2, 16, Rows(32, 2, 16, (x, y) => (uint)((x << 11) | ((x * 2) << 5) | (31 - x))), compression: BitFields, headerSize: headerSize, masks: [0xF800, 0x07E0, 0x001F]);

        var picture = BmpDecoder.Decode(bmp);

        Assert.Equal(Black | ((uint)Scale(31, 31) << 16) | ((uint)Scale(62, 63) << 8) | (uint)Scale(0, 31), picture[31, 1]);
        Assert.Equal(Black | ((uint)Scale(5, 31) << 16) | ((uint)Scale(10, 63) << 8) | (uint)Scale(26, 31), picture[5, 0]);
    }

    [Fact]
    public void Bit_fields_without_masks_fall_back_to_555()
    {
        var bmp = Bmp(2, 1, 16, Rows(2, 1, 16, (x, _) => x == 0 ? 0x7C00u : 0x001Fu), compression: BitFields, headerSize: 124, masks: [0, 0, 0]);

        var picture = BmpDecoder.Decode(bmp);

        Assert.Equal(0xFFFF0000, picture[0, 0]);
        Assert.Equal(0xFF0000FF, picture[1, 0]);
    }

    [Fact]
    public void Reads_rle8_runs_literals_jumps_and_row_ends()
    {
        var palette = Palette(8);
        byte[] stream =
        [
            0x03, 0x01, 0x00, 0x03, 0x02, 0x03, 0x04, 0x00, 0x00, 0x00, // bottom row: run, literals (padded), end of row
            0x00, 0x02, 0x02, 0x00, 0x02, 0x05, 0x00, 0x00, // middle row: jump right 2, run of 2, end of row
            0x06, 0x07, 0x00, 0x01, // top row: run of 6, end of picture
        ];

        var picture = BmpDecoder.Decode(Bmp(6, 3, 8, stream, palette, compression: Rle8));

        int?[][] expected =
        [
            [7, 7, 7, 7, 7, 7],
            [null, null, 5, 5, null, null],
            [1, 1, 1, 2, 3, 4],
        ];
        AssertSame(Image(6, 3, (x, y) => expected[y][x] is { } index ? palette[index] : Black), picture);
    }

    [Fact]
    public void Reads_rle4_runs_and_literals()
    {
        var palette = Palette(16);
        byte[] stream =
        [
            0x05, 0x12, 0x00, 0x00, // bottom row: five pixels taking turns between colours 1 and 2
            0x00, 0x05, 0x34, 0x56, 0x70, 0x00, 0x00, 0x01, // top row: five literal pixels, padded; end of picture
        ];

        var picture = BmpDecoder.Decode(Bmp(5, 2, 4, stream, palette, compression: Rle4));

        int[][] expected = [[3, 4, 5, 6, 7], [1, 2, 1, 2, 1]];
        AssertSame(Image(5, 2, (x, y) => palette[expected[y][x]]), picture);
    }

    [Fact]
    public void Rle_jumps_up_rows_runs_past_the_edge_and_stops_at_the_end_of_the_data()
    {
        var palette = Palette(4);
        byte[] stream =
        [
            0x0A, 0x01, // a run longer than the row: clipped, not wrapped
            0x00, 0x00, // end of the bottom row
            0x00, 0x02, 0x01, 0x01, // jump one right and one row up
            0x02, 0x03, // and a run there
            0x03, // cut off mid-pair
        ];

        var picture = BmpDecoder.Decode(Bmp(4, 4, 8, stream, palette, compression: Rle8));

        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(palette[1], picture[x, 3]));
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(Black, picture[x, 2]));
        Assert.Equal(Black, picture[0, 1]);
        Assert.Equal(palette[3], picture[1, 1]);
        Assert.Equal(palette[3], picture[2, 1]);
        Assert.Equal(Black, picture[3, 1]);
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(Black, picture[x, 0]));
    }

    [Fact]
    public void A_short_palette_leaves_the_missing_colours_black()
    {
        // Only 4 of 256 colours are written and the header does not say so; the pixel offset does.
        var palette = Palette(4);

        var picture = BmpDecoder.Decode(Bmp(8, 1, 8, Rows(8, 1, 8, (x, _) => (uint)x), palette));

        Assert.Equal(palette[3], picture[3, 0]);
        Assert.Equal(Black, picture[4, 0]);
        Assert.Equal(Black, picture[7, 0]);
    }

    [Fact]
    public void Colours_used_limits_the_palette()
    {
        var palette = Palette(16);

        var picture = BmpDecoder.Decode(Bmp(8, 1, 4, Rows(8, 1, 4, (x, _) => (uint)x), palette, colorsUsed: 4));

        Assert.Equal(palette[3], picture[3, 0]);
        Assert.Equal(Black, picture[4, 0]);
    }

    [Fact]
    public void An_index_past_a_one_colour_palette_is_black()
    {
        var picture = BmpDecoder.Decode(Bmp(2, 1, 1, Rows(2, 1, 1, (x, _) => (uint)x), [0xFF336699], colorsUsed: 1));

        Assert.Equal(0xFF336699, picture[0, 0]);
        Assert.Equal(Black, picture[1, 0]);
    }

    [Fact]
    public void Truncated_pixels_leave_the_rest_black()
    {
        var expected = Pattern(4, 4, seed: 11);
        var bmp = Bmp(4, 4, 24, Rows(4, 4, 24, (x, y) => expected[x, y] & 0xFFFFFF));

        // Rows are 12 bytes; cutting 7 leaves one whole pixel of the last (top) row.
        var picture = BmpDecoder.Decode(bmp.AsSpan(0, bmp.Length - 7));

        Assert.Equal(expected[0, 0], picture[0, 0]);
        Assert.Equal(Black, picture[1, 0]);
        Assert.Equal(Black, picture[3, 0]);
        for (var y = 1; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(expected[x, y], picture[x, y]);
            }
        }
    }

    [Fact]
    public void Crops_to_the_size_asked_for()
    {
        var expected = Pattern(13, 5, seed: 2);
        var bmp = Bmp(13, 5, 24, Rows(13, 5, 24, (x, y) => expected[x, y] & 0xFFFFFF));

        AssertSame(expected.Crop(0, 0, 3, 2), BmpDecoder.Decode(bmp, 3, 2));
        AssertSame(expected, BmpDecoder.Decode(bmp, 100, 100));
    }

    [Fact]
    public void Huge_sizes_are_refused_before_any_memory_is_taken()
    {
        var bmp = Bmp(1, 1, 24, new byte[4]);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 100_000);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 100_000);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 64 * 1024, $"Decoding allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(2049, 1)]
    [InlineData(1, 2049)]
    [InlineData(1, -2049)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-5, 1)]
    [InlineData(1, int.MinValue)]
    public void Impossible_sizes_are_refused(int width, int height)
    {
        var bmp = Bmp(1, 1, 24, new byte[4]);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);

        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));
    }

    [Fact]
    public void The_largest_allowed_side_is_accepted()
    {
        var picture = BmpDecoder.Decode(Bmp(SkinImage.MaxSide, 1, 24, new byte[SkinImage.MaxSide * 3]));

        Assert.Equal(SkinImage.MaxSide, picture.Width);
    }

    [Theory]
    [InlineData(24, 4u)] // JPEG inside
    [InlineData(24, 5u)] // PNG inside
    [InlineData(24, 3u)] // bit fields need 16 or 32 bits
    [InlineData(3, 0u)]
    [InlineData(8, 2u)] // RLE4 needs 4 bits
    [InlineData(4, 1u)] // RLE8 needs 8 bits
    [InlineData(8, 99u)]
    public void Unsupported_kinds_are_refused(int bitCount, uint compression)
    {
        var bmp = Bmp(2, 2, bitCount, new byte[64], bitCount <= 8 ? Palette(1 << bitCount) : null, compression);

        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(200)]
    [InlineData(-1)]
    public void Unknown_header_sizes_are_refused(int headerSize)
    {
        var bmp = Bmp(2, 2, 24, new byte[16]);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), headerSize);

        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));
    }

    [Fact]
    public void Pixel_offsets_outside_the_file_or_inside_the_header_are_refused()
    {
        var bmp = Bmp(2, 2, 24, new byte[16]);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), bmp.Length + 1);
        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));

        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 20);
        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp));

        // Right at the end is fine: the picture is simply all black.
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), bmp.Length);
        Assert.All(BmpDecoder.Decode(bmp).Pixels, pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void Cut_headers_and_other_formats_are_refused()
    {
        var bmp = Bmp(2, 2, 24, new byte[16]);
        for (var length = 0; length < 54; length++)
        {
            Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(bmp.AsSpan(0, length)));
        }

        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        Assert.False(BmpDecoder.IsBmp(png));
        Assert.Throws<SkinFormatException>(() => BmpDecoder.Decode(png));
        Assert.True(BmpDecoder.IsBmp(bmp));
    }

    [Fact]
    public void Encoded_pictures_decode_to_the_same_pixels()
    {
        var random = new Random(42);
        foreach (var (width, height) in new[] { (1, 1), (2, 3), (37, 23), (275, 116) })
        {
            var picture = Image(width, height, (_, _) => 0xFF000000 | (uint)random.Next(0x1000000));

            var bmp = BmpEncoder.Encode(picture);

            Assert.Equal(54 + ((((width * 3) + 3) & ~3) * height), bmp.Length);
            AssertSame(picture, BmpDecoder.Decode(bmp));
        }
    }

    [Fact]
    public void The_encoder_drops_alpha()
    {
        var picture = Filled(2, 2, 0x80123456);

        Assert.Equal(0xFF123456, BmpDecoder.Decode(BmpEncoder.Encode(picture))[1, 1]);
    }

    internal static void AssertSame(SkinImage expected, SkinImage actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static uint Expand(int red, int green, int blue, int bits)
    {
        var max = (1 << bits) - 1;
        return Black | ((uint)Scale(red, max) << 16) | ((uint)Scale(green, max) << 8) | (uint)Scale(blue, max);
    }

    private static int Scale(int value, int max) => ((value * 255) + (max / 2)) / max;
}

public sealed class SkinArchiveTests
{
    [Fact]
    public void Reads_every_sheet_and_both_text_files()
    {
        var archive = Zip(
        [
            .. ClassicSheets(),
            new ZipItem("viscolor.txt", Text("1,2,3\n4,5,6\n")),
            new ZipItem("pledit.txt", Text("[Text]\nNormal=#123456\nFont=Tahoma\n")) { Deflate = false },
        ]);

        var skin = Skin.Load(archive, "Mine");

        Assert.Equal("Mine", skin.Name);
        Assert.False(skin.IsBuiltIn);
        foreach (var sheet in SkinSheets.All)
        {
            var picture = skin.OwnSheet(sheet);
            Assert.NotNull(picture);
            Assert.Equal(SkinSheets.ExpectedSize(sheet), (picture.Width, picture.Height));
            Assert.All(picture.Pixels, pixel => Assert.Equal(SheetColour(sheet), pixel));
        }

        Assert.Equal(0xFF010203u, skin.VisColors[0]);
        Assert.Equal(0xFF040506u, skin.VisColors[1]);
        Assert.Equal(SkinTextFiles.DefaultVisColors[2], skin.VisColors[2]);
        Assert.Equal(0xFF123456u, skin.Playlist.Normal);
        Assert.Equal("Tahoma", skin.Playlist.Font);
        Assert.Equal(SkinTextFiles.DefaultPlaylistColors.Current, skin.Playlist.Current);
    }

    [Fact]
    public void Without_text_files_the_colours_are_the_defaults()
    {
        var skin = Skin.Load(Zip([new ZipItem("main.bmp", SheetBmp(SkinSheet.Main))]), "Plain");

        Assert.Equal(SkinTextFiles.DefaultVisColors, skin.VisColors);
        Assert.Equal(SkinTextFiles.DefaultPlaylistColors, skin.Playlist);
        Assert.Null(skin.OwnSheet(SkinSheet.TitleBar));
    }

    [Fact]
    public void Finds_files_in_folders_with_either_slash_and_any_case()
    {
        var skin = Skin.Load(
            Zip(
            [
                new ZipItem("My Skin/", []) { Deflate = false },
                new ZipItem("My Skin/MAIN.BMP", SheetBmp(SkinSheet.Main)),
                new ZipItem("My Skin\\Deeper\\TitleBar.Bmp", SheetBmp(SkinSheet.TitleBar)),
                new ZipItem("Skïn/cbuttons.bmp", SheetBmp(SkinSheet.CButtons)),
                new ZipItem("My Skin/VISCOLOR.TXT", Text("9 9 9")),
            ]),
            "Nested");

        Assert.Equal(SheetColour(SkinSheet.Main), skin.OwnSheet(SkinSheet.Main)![0, 0]);
        Assert.Equal(SheetColour(SkinSheet.TitleBar), skin.OwnSheet(SkinSheet.TitleBar)![0, 0]);
        Assert.Equal(SheetColour(SkinSheet.CButtons), skin.OwnSheet(SkinSheet.CButtons)![0, 0]);
        Assert.Equal(0xFF090909u, skin.VisColors[0]);
    }

    [Fact]
    public void The_last_of_several_files_with_one_name_wins()
    {
        var skin = Skin.Load(
            Zip(
            [
                new ZipItem("main.bmp", BmpEncoder.Encode(Filled(275, 116, 0xFFFF0000))),
                new ZipItem("nested/Main.BMP", BmpEncoder.Encode(Filled(275, 116, 0xFF0000FF))),
            ]),
            "Twice");

        Assert.Equal(0xFF0000FFu, skin.OwnSheet(SkinSheet.Main)![0, 0]);
    }

    [Fact]
    public void A_png_named_bmp_is_left_out_like_a_missing_sheet()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

        var skin = Skin.Load(Zip([new ZipItem("main.bmp", png), new ZipItem("titlebar.bmp", SheetBmp(SkinSheet.TitleBar))]), "Png");

        Assert.Null(skin.OwnSheet(SkinSheet.Main));
        Assert.NotNull(skin.OwnSheet(SkinSheet.TitleBar));
    }

    [Fact]
    public void A_damaged_picture_is_left_out_and_the_rest_still_load()
    {
        var damaged = SheetBmp(SkinSheet.Main);
        BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(14), 77);

        var skin = Skin.Load(
            Zip(
            [
                new ZipItem("main.bmp", damaged),
                new ZipItem("cbuttons.bmp", [0xFF, 0xFF, 0xFF, 0xFF]) { Method = 8 },
                new ZipItem("titlebar.bmp", SheetBmp(SkinSheet.TitleBar)),
            ]),
            "Damaged");

        Assert.Null(skin.OwnSheet(SkinSheet.Main));
        Assert.Null(skin.OwnSheet(SkinSheet.CButtons));
        Assert.NotNull(skin.OwnSheet(SkinSheet.TitleBar));
    }

    [Fact]
    public void A_skin_whose_pictures_are_all_damaged_says_so()
    {
        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(
            Zip([new ZipItem("main.bmp", [0xFF, 0xFF, 0xFF, 0xFF]) { Method = 8 }, new ZipItem("text.bmp", Text("not a picture"))]),
            "Broken"));

        Assert.Equal(SkinArchive.DamagedMessage, error.Message);
    }

    [Fact]
    public void Short_sheets_are_kept_as_they_are_and_large_ones_cut_down()
    {
        var skin = Skin.Load(
            Zip(
            [
                new ZipItem("posbar.bmp", BmpEncoder.Encode(Pattern(307, 4))),
                new ZipItem("volume.bmp", BmpEncoder.Encode(Pattern(68, 420))),
                new ZipItem("main.bmp", BmpEncoder.Encode(Pattern(300, 150, seed: 1))),
            ]),
            "Odd sizes");

        Assert.Equal((307, 4), (skin.OwnSheet(SkinSheet.PosBar)!.Width, skin.OwnSheet(SkinSheet.PosBar)!.Height));
        Assert.Equal((68, 420), (skin.OwnSheet(SkinSheet.Volume)!.Width, skin.OwnSheet(SkinSheet.Volume)!.Height));
        BmpDecoderTests.AssertSame(Pattern(300, 150, seed: 1).Crop(0, 0, 275, 116), skin.OwnSheet(SkinSheet.Main)!);

        // Balance falls back to the skin's own volume picture.
        Assert.Same(skin.OwnSheet(SkinSheet.Volume), skin.Sheet(SkinSheet.Balance));
    }

    [Fact]
    public void Numbers_without_nums_ex_get_a_minus_sign_built_from_them()
    {
        var numbers = Pattern(99, 13, seed: 4);

        var skin = Skin.Load(Zip([new ZipItem("numbers.bmp", BmpEncoder.Encode(numbers))]), "Old digits");

        BmpDecoderTests.AssertSame(numbers, skin.OwnSheet(SkinSheet.Numbers)!);
        var numsEx = skin.OwnSheet(SkinSheet.NumsEx)!;
        Assert.Equal((108, 13), (numsEx.Width, numsEx.Height));
        for (var y = 0; y < 13; y++)
        {
            for (var x = 0; x < 108; x++)
            {
                var expected = x < 99 ? numbers[x, y]
                    : y == 6 && x is >= 101 and < 106 ? numbers[20 + (x - 101), 6]
                    : numbers[90 + (x - 99), y];
                Assert.Equal(expected, numsEx[x, y]);
            }
        }
    }

    [Fact]
    public void Numbers_108_wide_are_used_as_nums_ex_as_they_are()
    {
        var numbers = Pattern(108, 13, seed: 5);

        var skin = Skin.Load(Zip([new ZipItem("numbers.bmp", BmpEncoder.Encode(numbers))]), "Wide digits");

        BmpDecoderTests.AssertSame(numbers, skin.OwnSheet(SkinSheet.NumsEx)!);
    }

    [Fact]
    public void A_skins_own_nums_ex_is_kept()
    {
        var skin = Skin.Load(
            Zip([new ZipItem("numbers.bmp", SheetBmp(SkinSheet.Numbers)), new ZipItem("nums_ex.bmp", SheetBmp(SkinSheet.NumsEx))]),
            "Both");

        Assert.All(skin.OwnSheet(SkinSheet.NumsEx)!.Pixels, pixel => Assert.Equal(SheetColour(SkinSheet.NumsEx), pixel));
        Assert.All(skin.OwnSheet(SkinSheet.Numbers)!.Pixels, pixel => Assert.Equal(SheetColour(SkinSheet.Numbers), pixel));
    }

    [Theory]
    [InlineData("skin.xml")]
    [InlineData("Skins/MySkin.WAL")]
    public void A_modern_winamp_skin_is_named_as_such(string marker)
    {
        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(
            Zip([new ZipItem(marker, Text("<WinampAbstractionLayer/>")), new ZipItem("player/background.png", [1, 2, 3])]),
            "Modern"));

        Assert.Equal(SkinArchive.ModernSkinMessage, error.Message);
    }

    [Fact]
    public void A_modern_skin_that_also_has_classic_pictures_loads_them()
    {
        var skin = Skin.Load(Zip([new ZipItem("skin.xml", Text("<x/>")), new ZipItem("main.bmp", SheetBmp(SkinSheet.Main))]), "Both kinds");

        Assert.NotNull(skin.OwnSheet(SkinSheet.Main));
    }

    [Fact]
    public void Archives_without_classic_pictures_say_so()
    {
        Assert.Equal(SkinArchive.NoPicturesMessage, Assert.Throws<SkinFormatException>(() => Skin.Load(Zip([]), "Empty")).Message);
        Assert.Equal(
            SkinArchive.NoPicturesMessage,
            Assert.Throws<SkinFormatException>(() => Skin.Load(Zip([new ZipItem("readme.txt", Text("hi")), new ZipItem("viscolor.txt", Text("1,1,1"))]), "Text")).Message);
    }

    [Fact]
    public void Files_that_are_not_archives_say_so()
    {
        foreach (var bytes in new byte[][] { [], Text("hello"), SheetBmp(SkinSheet.Main), new byte[100] })
        {
            Assert.Equal(SkinArchive.NotAnArchiveMessage, Assert.Throws<SkinFormatException>(() => Skin.Load(bytes, "Nope")).Message);
        }

        var cut = ClassicSkin();
        Assert.Equal(SkinArchive.NotAnArchiveMessage, Assert.Throws<SkinFormatException>(() => Skin.Load(cut.AsSpan(0, cut.Length / 2), "Cut")).Message);
    }

    [Fact]
    public void Archives_over_16_mb_are_refused()
    {
        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(new byte[SkinFolder.MaxArchiveBytes + 1], "Big"));

        Assert.Equal(SkinArchive.TooLargeMessage, error.Message);
    }

    [Fact]
    public void An_oversized_entry_is_refused()
    {
        var stored = new ZipItem("main.bmp", new byte[SkinArchive.MaxEntryBytes + 1]) { Deflate = false };

        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(Zip([stored]), "Big entry"));

        Assert.Equal(SkinArchive.FileTooLargeMessage, error.Message);
    }

    [Fact]
    public void A_zip_bomb_stops_at_the_limit_whatever_size_it_claims()
    {
        // 64 MB of zeros packs into about 64 KB and claims to be 1000 bytes.
        var bomb = Zip([new ZipItem("main.bmp", new byte[64 * 1024 * 1024]) { ClaimedSize = 1000 }]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(bomb, "Bomb"));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(SkinArchive.FileTooLargeMessage, error.Message);
        Assert.True(allocated < 24 * 1024 * 1024, $"Reading the bomb allocated {allocated} bytes.");
    }

    [Fact]
    public void Files_the_player_does_not_use_are_never_unpacked()
    {
        var archive = Zip([new ZipItem("readme.txt", new byte[64 * 1024 * 1024]), new ZipItem("main.bmp", SheetBmp(SkinSheet.Main))]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var skin = Skin.Load(archive, "Readme bomb");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(skin.OwnSheet(SkinSheet.Main));
        Assert.True(allocated < 4 * 1024 * 1024, $"Reading the skin allocated {allocated} bytes.");
    }

    [Fact]
    public void The_whole_skin_has_an_unpacked_budget()
    {
        var sheets = new[] { SkinSheet.Main, SkinSheet.TitleBar, SkinSheet.CButtons, SkinSheet.PlayPaus, SkinSheet.MonoSter };
        var archive = Zip(sheets.Select(sheet => new ZipItem(SkinSheets.FileName(sheet), new byte[7 * 1024 * 1024])));

        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(archive, "Heavy"));

        Assert.Equal(SkinArchive.UnpacksTooLargeMessage, error.Message);
    }

    [Fact]
    public void Only_the_first_1024_entries_are_looked_at()
    {
        static IEnumerable<ZipItem> Filler(int count) =>
            Enumerable.Range(0, count).Select(i => new ZipItem($"junk/{i}.txt", []) { Deflate = false });
        var main = new ZipItem("main.bmp", SheetBmp(SkinSheet.Main));

        Assert.NotNull(Skin.Load(Zip([.. Filler(SkinArchive.MaxEntries - 1), main]), "Just in").OwnSheet(SkinSheet.Main));
        Assert.Equal(
            SkinArchive.NoPicturesMessage,
            Assert.Throws<SkinFormatException>(() => Skin.Load(Zip([.. Filler(SkinArchive.MaxEntries), main]), "Too far")).Message);
    }

    [Fact]
    public void Encrypted_entries_and_unknown_packing_are_skipped()
    {
        var skin = Skin.Load(
            Zip(
            [
                new ZipItem("main.bmp", BmpEncoder.Encode(Filled(275, 116, 0xFFFF0000))),
                new ZipItem("main.bmp", BmpEncoder.Encode(Filled(275, 116, 0xFF0000FF))) { Encrypted = true },
                new ZipItem("titlebar.bmp", SheetBmp(SkinSheet.TitleBar)) { Encrypted = true },
                new ZipItem("cbuttons.bmp", SheetBmp(SkinSheet.CButtons)) { Method = 12 },
            ]),
            "Locked");

        Assert.Equal(0xFFFF0000u, skin.OwnSheet(SkinSheet.Main)![0, 0]);
        Assert.Null(skin.OwnSheet(SkinSheet.TitleBar));
        Assert.Null(skin.OwnSheet(SkinSheet.CButtons));
    }

    [Fact]
    public void A_skin_whose_pictures_are_all_locked_or_packed_oddly_says_they_cant_be_read()
    {
        var error = Assert.Throws<SkinFormatException>(() => Skin.Load(
            Zip(
            [
                new ZipItem("main.bmp", SheetBmp(SkinSheet.Main)) { Encrypted = true },
                new ZipItem("cbuttons.bmp", SheetBmp(SkinSheet.CButtons)) { Method = 12 },
                new ZipItem("viscolor.txt", Text("1,1,1")),
            ]),
            "Locked"));

        Assert.Equal(SkinArchive.DamagedMessage, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_archives_written_by_a_real_zip_writer(bool streaming)
    {
        var items = ClassicSheets().ToList();
        items[0] = items[0] with { Deflate = false };

        var skin = Skin.Load(ZipWithDotNet(items, streaming), "Real");

        Assert.All(SkinSheets.All, sheet => Assert.Equal(SheetColour(sheet), skin.OwnSheet(sheet)![0, 0]));
    }

    [Fact]
    public void Reads_zip64_archives()
    {
        var skin = Skin.Load(Zip(ClassicSheets(), zip64: true), "Zip64");

        Assert.All(SkinSheets.All, sheet => Assert.Equal(SheetColour(sheet), skin.OwnSheet(sheet)![0, 0]));
    }

    [Fact]
    public void Reads_archives_with_something_in_front()
    {
        var archive = ClassicSkin();
        var selfExtracting = new byte[1000 + archive.Length];
        selfExtracting[0] = (byte)'M';
        selfExtracting[1] = (byte)'Z';
        archive.CopyTo(selfExtracting, 1000);

        var skin = Skin.Load(selfExtracting, "Self-extracting");

        Assert.All(SkinSheets.All, sheet => Assert.NotNull(skin.OwnSheet(sheet)));
    }

    [Fact]
    public void Reads_archives_with_a_comment_at_the_end()
    {
        var archive = ClassicSkin();
        var comment = Encoding.ASCII.GetBytes("Made with care. PK\u0005\u0006 is not a real end record.");
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(archive.Length - 2), (ushort)comment.Length);

        var skin = Skin.Load([.. archive, .. comment], "Commented");

        Assert.NotNull(skin.OwnSheet(SkinSheet.Main));
    }
}

public sealed class SkinTextFilesTests
{
    [Fact]
    public void Reads_viscolor_with_comments_spaces_and_a_byte_order_mark()
    {
        var text = "﻿// Visualisation colours\r\n"
            + "0,0,0, // color 0 = black\r\n"
            + "  10 , 20 ,30\r\n"
            + "\t40\t50\t60\r\n"
            + "70 80 90 trailing words\r\n"
            + "300,256,999\r\n"
            + "not a colour\r\n"
            + "1,2\r\n"
            + "5,6,7,";

        var colours = SkinTextFiles.ParseVisColors(Encoding.UTF8.GetBytes(text));

        Assert.Equal(new uint[] { 0xFF000000, 0xFF0A141E, 0xFF28323C, 0xFF46505A, 0xFFFFFFFF, 0xFF050607 }, colours[..6]);
        Assert.Equal(SkinTextFiles.DefaultVisColors.Skip(6), colours.Skip(6));
    }

    [Fact]
    public void A_full_viscolor_file_sets_all_24_and_ignores_the_rest()
    {
        var lines = Enumerable.Range(0, 30).Select(i => $"{i},{i},{i}");

        var colours = SkinTextFiles.ParseVisColors(Text(string.Join('\n', lines)));

        Assert.Equal(Skin.VisColorCount, colours.Length);
        Assert.Equal(0xFF171717u, colours[23]);
    }

    [Fact]
    public void Empty_or_unreadable_viscolor_gives_the_defaults()
    {
        Assert.Equal(SkinTextFiles.DefaultVisColors, SkinTextFiles.ParseVisColors([]));
        Assert.Equal(SkinTextFiles.DefaultVisColors, SkinTextFiles.ParseVisColors([0xC3, 0x28, 0xFF, 0x00]));
    }

    [Fact]
    public void Reads_viscolor_saved_as_utf16()
    {
        var colours = SkinTextFiles.ParseVisColors([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("1,2,3\r\n")]);

        Assert.Equal(0xFF010203u, colours[0]);
    }

    [Fact]
    public void Reads_pledit_in_its_usual_form()
    {
        var text = "[Text]\r\nNormal=#00FF00\r\nCurrent=#FFFFFF\r\nNormalBG=#000000\r\nSelectedBG=#0000FF\r\nFont=Arial\r\n";

        var colours = SkinTextFiles.ParsePlaylistColors(Text(text));

        Assert.Equal(new PlaylistColors(0xFF00FF00, 0xFFFFFFFF, 0xFF000000, 0xFF0000FF, "Arial"), colours);
    }

    [Fact]
    public void Reads_pledit_quirks()
    {
        var text = "﻿; made by hand\n"
            + "[general]\nNormal=#111111\n"
            + "[ text ]\n"
            + "normal = 123456\n"
            + "CURRENT=#ABCDEF00\n"
            + "NormalBG = \"#0A0B0C\"\n"
            + "SelectedBG=##FEDCBA=ignored\n"
            + "Font = 'Lucida Console'\n"
            + "MbFG=#FFFFFF\n";

        var colours = SkinTextFiles.ParsePlaylistColors(Text(text));

        Assert.Equal(new PlaylistColors(0xFF123456, 0xFFABCDEF, 0xFF0A0B0C, 0xFFFEDCBA, "Lucida Console"), colours);
    }

    [Fact]
    public void Unreadable_pledit_values_keep_their_defaults()
    {
        var defaults = SkinTextFiles.DefaultPlaylistColors;
        var text = "[Text]\nNormal=#12345\nCurrent=#GGGGGG\nNormalBG=\nSelectedBG=#é12345\nFont=\n";

        Assert.Equal(defaults, SkinTextFiles.ParsePlaylistColors(Text(text)));
        Assert.Equal(defaults, SkinTextFiles.ParsePlaylistColors(Text("Normal=#FF0000\n")));
        Assert.Equal(defaults, SkinTextFiles.ParsePlaylistColors([0xFF, 0xC0, 0x80]));
    }

    [Fact]
    public void Long_font_names_are_cut()
    {
        var colours = SkinTextFiles.ParsePlaylistColors(Text("[Text]\nFont=" + new string('x', 1000)));

        Assert.Equal(SkinTextFiles.MaxFontNameLength, colours.Font!.Length);
    }

    [Fact]
    public void Random_bytes_never_break_the_text_parsers()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(200)];
            random.NextBytes(bytes);

            Assert.Equal(Skin.VisColorCount, SkinTextFiles.ParseVisColors(bytes).Length);
            Assert.NotNull(SkinTextFiles.ParsePlaylistColors([.. "[Text]\nNormal="u8, .. bytes]));
        }
    }
}

/// <summary>Damaged input of every kind must only ever end in a <see cref="SkinFormatException"/> of the decoder's own making.</summary>
public sealed class SkinFuzzTests
{
    [Fact]
    public void Damaged_pictures_only_ever_throw_skin_format_errors()
    {
        var palette = Palette(256);
        byte[][] samples =
        [
            Bmp(9, 5, 8, Rows(9, 5, 8, (x, y) => (uint)(x * y)), palette),
            Bmp(9, 5, 4, Rows(9, 5, 4, (x, y) => (uint)(x + y) % 16), Palette(16), headerSize: 12),
            Bmp(9, 5, 1, Rows(9, 5, 1, (x, y) => (uint)(x ^ y) & 1), Palette(2)),
            Bmp(9, -5, 24, Rows(9, 5, 24, (x, y) => (uint)((x * 1000) + y), topDown: true)),
            Bmp(9, 5, 32, Rows(9, 5, 32, (x, y) => (uint)((x * 77) + y)), compression: BitFields, headerSize: 124, masks: [0xFF0000, 0xFF00, 0xFF, 0]),
            Bmp(9, 5, 16, Rows(9, 5, 16, (x, y) => (uint)((x * 999) + y)), compression: BitFields, masks: [0xF800, 0x07E0, 0x001F]),
            Bmp(6, 3, 8, [3, 1, 0, 3, 2, 3, 4, 0, 0, 0, 0, 2, 2, 0, 2, 5, 0, 0, 6, 7, 0, 1], Palette(8), compression: Rle8),
            Bmp(5, 2, 4, [5, 0x12, 0, 0, 0, 5, 0x34, 0x56, 0x70, 0, 0, 1], Palette(16), compression: Rle4),
        ];
        var random = new Random(20261007);

        for (var i = 0; i < 8000; i++)
        {
            var mutated = Mutate(samples[i % samples.Length], random, headerBytes: 80);
            try
            {
                var picture = BmpDecoder.Decode(mutated);
                Assert.InRange(picture.Width, 1, SkinImage.MaxSide);
                Assert.InRange(picture.Height, 1, SkinImage.MaxSide);
            }
            catch (SkinFormatException error)
            {
                Assert.Null(error.InnerException);
            }
        }
    }

    [Fact]
    public void Damaged_archives_only_ever_throw_skin_format_errors()
    {
        byte[][] samples =
        [
            Zip(
            [
                new ZipItem("Skin/main.bmp", BmpEncoder.Encode(Pattern(20, 8))),
                new ZipItem("Skin/titlebar.bmp", BmpEncoder.Encode(Pattern(12, 6, seed: 1))) { Deflate = false },
                new ZipItem("Skin/numbers.bmp", Bmp(9, 5, 8, Rows(9, 5, 8, (x, y) => (uint)(x * y)), Palette(256))),
                new ZipItem("Skin/viscolor.txt", Text("1,2,3\n4,5,6\n")),
                new ZipItem("Skin/pledit.txt", Text("[Text]\nNormal=#00FF00\n")) { Deflate = false },
                new ZipItem("Skin/readme.txt", Text("Thanks for downloading")),
            ]),
            Zip(
            [
                new ZipItem("volume.bmp", Bmp(6, 3, 8, [3, 1, 0, 3, 2, 3, 4, 0, 0, 0, 0, 1], Palette(8), compression: Rle8)),
                new ZipItem("text.bmp", BmpEncoder.Encode(Pattern(15, 6, seed: 2))),
            ],
            zip64: true),
        ];
        var random = new Random(1997);

        for (var i = 0; i < 4000; i++)
        {
            var sample = samples[i % samples.Length];

            // Half the time aim at the end of the archive, where its directory is.
            var mutated = Mutate(sample, random, headerBytes: 0, tailBytes: random.Next(2) == 0 ? 400 : 0);
            try
            {
                _ = Skin.Load(mutated, "Fuzz");
            }
            catch (SkinFormatException error)
            {
                Assert.Null(error.InnerException);
            }
        }
    }

    /// <summary>
    /// Changes a few bytes (often in the first <paramref name="headerBytes"/>
    /// or the last <paramref name="tailBytes"/>), writes extreme 32-bit
    /// numbers, or cuts the data short.
    /// </summary>
    private static byte[] Mutate(byte[] sample, Random random, int headerBytes, int tailBytes = 0)
    {
        var bytes = (byte[])sample.Clone();
        int Position()
        {
            if (headerBytes > 0 && random.Next(2) == 0)
            {
                return random.Next(Math.Min(headerBytes, bytes.Length));
            }

            return tailBytes > 0 ? bytes.Length - 1 - random.Next(Math.Min(tailBytes, bytes.Length)) : random.Next(bytes.Length);
        }

        uint[] extremes = [0, 1, 0xFF, 0xFFFF, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 2048, 2049, 0xFFFFFFF0];
        for (var change = random.Next(1, 5); change > 0; change--)
        {
            switch (random.Next(4))
            {
                case 0 when bytes.Length > 4:
                    var at = Math.Min(Position(), bytes.Length - 4);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), extremes[random.Next(extremes.Length)]);
                    break;
                case 1:
                    Array.Resize(ref bytes, random.Next(bytes.Length));
                    if (bytes.Length == 0)
                    {
                        return bytes;
                    }

                    break;
                default:
                    bytes[Position()] = (byte)random.Next(256);
                    break;
            }
        }

        return bytes;
    }
}

using System.Buffers.Binary;
using System.Text;

namespace Resonate.Themes.Tests;

public sealed class BundledFontsTests
{
    private static readonly int[] Weights = [400, 600, 700];

    [Theory]
    [InlineData("Inter, Segoe UI", "ms-appx:///Assets/Fonts/Inter.ttc#Inter, Segoe UI")]
    [InlineData("Sitka Display, Georgia", "Sitka Display, Georgia")]
    [InlineData("  plus jakarta sans ,Segoe UI Variable Text ",
        "ms-appx:///Assets/Fonts/PlusJakartaSans.ttc#Plus Jakarta Sans, Segoe UI Variable Text")]
    [InlineData("Geist Mono,Geist", "ms-appx:///Assets/Fonts/GeistMono.ttc#Geist Mono, ms-appx:///Assets/Fonts/Geist.ttc#Geist")]
    [InlineData("Inter,, Segoe UI", "ms-appx:///Assets/Fonts/Inter.ttc#Inter, Segoe UI")]
    [InlineData("Segoe UI", "Segoe UI")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Resolve_points_bundled_fonts_at_their_files(string list, string expected) =>
        Assert.Equal(expected, BundledFonts.Resolve(list));

    [Theory]
    [InlineData("ms-appx:///Assets/Fonts/Inter.ttc#Inter")]
    [InlineData("Inter#Inter")]
    [InlineData("/Fonts/Inter.ttf")]
    public void Resolve_leaves_entries_that_already_name_a_file(string entry) =>
        Assert.Equal($"{entry}, ms-appx:///Assets/Fonts/Inter.ttc#Inter", BundledFonts.Resolve($" {entry} , Inter"));

    [Fact]
    public void Resolve_handles_null() => Assert.Equal("", BundledFonts.Resolve(null));

    [Theory]
    [InlineData("Inter", "Inter")]
    [InlineData("inter", "Inter")]
    [InlineData("  Geist Mono ", "Geist Mono")]
    [InlineData("JETBRAINS MONO", "JetBrains Mono")]
    public void Find_ignores_case_and_spaces_around(string name, string expected) =>
        Assert.Equal(expected, BundledFonts.Find(name)?.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Segoe UI")]
    [InlineData("Geist Mon")]
    [InlineData("PlusJakartaSans")]
    public void Find_returns_null_for_anything_else(string? name) => Assert.Null(BundledFonts.Find(name));

    [Fact]
    public void The_catalog_is_alphabetical_and_unique()
    {
        var names = BundledFonts.All.Select(f => f.Name).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(names.Count, BundledFonts.All.Select(f => f.File).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(BundledFonts.All, f => Assert.Equal(f.Name.Replace(" ", "", StringComparison.Ordinal) + ".ttc", f.File));
    }

    [Fact]
    public void Every_font_has_its_file_and_licence()
    {
        Assert.All(BundledFonts.All, font =>
        {
            Assert.True(File.Exists(Path.Combine(FontsFolder, font.File)), $"{font.File} is missing");
            var licence = Path.Combine(FontsFolder, Path.GetFileNameWithoutExtension(font.File) + "-OFL.txt");
            Assert.True(File.Exists(licence), $"{licence} is missing");
            var text = File.ReadAllText(licence);
            Assert.Contains("SIL Open Font License, Version 1.1", text, StringComparison.Ordinal);
            var copyright = text.Split("This Font Software is licensed", 2)[0];
            Assert.DoesNotContain("Reserved Font Name", copyright, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Every_font_file_is_in_the_catalog()
    {
        var files = Directory.GetFiles(FontsFolder, "*.ttc").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal);
        Assert.Equal(BundledFonts.All.Select(f => f.File).Order(StringComparer.Ordinal), files);
    }

    [Fact]
    public void Every_file_holds_the_three_weights_under_the_catalog_name()
    {
        var fullNames = new HashSet<string>(StringComparer.Ordinal);
        var postScriptNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var font in BundledFonts.All)
        {
            var faces = OpenType.ReadCollection(File.ReadAllBytes(Path.Combine(FontsFolder, font.File)));
            Assert.Equal(Weights, faces.Select(f => f.Weight).Order());
            Assert.All(faces, face =>
            {
                Assert.Equal(font.Name, face.TypographicFamily);
                Assert.True(face.TrueTypeOutlines, $"{font.File} must have TrueType outlines");
                Assert.False(face.Variable, $"{font.File} must hold static faces");
                Assert.Equal(font.Kind == FontKind.Mono, face.FixedPitch);
                Assert.True(fullNames.Add(face.Names[4]), $"full name {face.Names[4]} appears twice");
                Assert.True(postScriptNames.Add(face.Names[6]), $"PostScript name {face.Names[6]} appears twice");
            });
        }
    }

    private static string FontsFolder { get; } = Path.Combine(RepositoryRoot(), "src", "Resonate.App", "Assets", "Fonts");

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Resonate.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Resonate.slnx not found above the test folder");
    }

    /// <summary>
    /// Just enough of the OpenType format to read a font collection: each
    /// face's table directory, its Windows names, weight and pitch.
    /// </summary>
    private static class OpenType
    {
        public sealed record Face(int Weight, IReadOnlyDictionary<int, string> Names, bool TrueTypeOutlines, bool Variable, bool FixedPitch)
        {
            /// <summary>Name 16 when the face has one, else name 1.</summary>
            public string? TypographicFamily => Names.GetValueOrDefault(16) ?? Names.GetValueOrDefault(1);
        }

        public static List<Face> ReadCollection(byte[] data)
        {
            Assert.Equal("ttcf", Encoding.ASCII.GetString(data, 0, 4));
            var count = U32(data, 8);
            var faces = new List<Face>();
            for (var i = 0; i < count; i++)
            {
                faces.Add(ReadFace(data, checked((int)U32(data, 12 + (4 * i)))));
            }

            return faces;
        }

        private static Face ReadFace(byte[] data, int offset)
        {
            var tables = new Dictionary<string, int>(StringComparer.Ordinal);
            var tableCount = U16(data, offset + 4);
            for (var i = 0; i < tableCount; i++)
            {
                var record = offset + 12 + (16 * i);
                tables[Encoding.ASCII.GetString(data, record, 4)] = checked((int)U32(data, record + 8));
            }

            var os2 = tables["OS/2"];
            var post = tables["post"];
            return new Face(
                Weight: U16(data, os2 + 4),
                Names: ReadNames(data, tables["name"]),
                TrueTypeOutlines: U32(data, offset) == 0x00010000 && tables.ContainsKey("glyf"),
                Variable: tables.ContainsKey("fvar") || tables.ContainsKey("gvar"),
                FixedPitch: U32(data, post + 12) != 0);
        }

        /// <summary>The English Windows names (platform 3, encoding 1, UTF-16BE).</summary>
        private static Dictionary<int, string> ReadNames(byte[] data, int table)
        {
            var names = new Dictionary<int, string>();
            var count = U16(data, table + 2);
            var strings = table + U16(data, table + 4);
            for (var i = 0; i < count; i++)
            {
                var record = table + 6 + (12 * i);
                if (U16(data, record) == 3 && U16(data, record + 2) == 1 && U16(data, record + 4) == 0x409)
                {
                    names[U16(data, record + 6)] = Encoding.BigEndianUnicode.GetString(
                        data, strings + U16(data, record + 10), U16(data, record + 8));
                }
            }

            return names;
        }

        private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at, 2));

        private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));
    }
}

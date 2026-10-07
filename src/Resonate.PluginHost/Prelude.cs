namespace Resonate.PluginHost;

/// <summary>prelude.js, built into the helper.</summary>
internal static class Prelude
{
    public static string Source { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(Prelude).Assembly.GetManifestResourceStream("prelude.js")
            ?? throw new InvalidOperationException("prelude.js is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

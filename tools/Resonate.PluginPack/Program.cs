using Resonate.Plugins.Packaging;

// Usage:
//   dotnet run --project tools/Resonate.PluginPack -- \
//     --plugins plugins --host <published helper folder> --runtime win-x64 \
//     --out <folder> [--source https://github.com/livexibot/Resonate/releases/download/v1.2.3/]
//
// Checks every plugin's plugin.json, packs each plugin and the helper, and
// writes plugin-catalog.json, which the app is then built with
// (-p:PluginCatalog=<folder>/plugin-catalog.json).

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i]] = args[i + 1];
}

string Required(string name)
{
    if (options.TryGetValue(name, out var value))
    {
        return value;
    }

    Console.Error.WriteLine($"Missing {name}. Needs --plugins, --host, --runtime and --out (and optionally --source).");
    Environment.Exit(2);
    return string.Empty;
}

try
{
    var catalog = PluginPackager.PackAll(
        Required("--plugins"),
        Required("--host"),
        Required("--runtime"),
        options.GetValueOrDefault("--source"),
        Required("--out"));

    Console.WriteLine($"Plugin helper: {catalog.Host!.File} ({catalog.Host.Size / 1024} KB)");
    foreach (var plugin in catalog.Plugins)
    {
        Console.WriteLine($"Plugin: {plugin.Manifest.Name} {plugin.Manifest.Version} -> {plugin.Package.File} ({plugin.Package.Size} bytes)");
    }

    return 0;
}
catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

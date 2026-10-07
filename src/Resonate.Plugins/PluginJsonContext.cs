using System.Text.Json.Serialization;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins;

// Source-generated JSON, so reading and writing work under Native AOT.

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PluginCatalog))]
[JsonSerializable(typeof(PluginStateFile))]
internal sealed partial class PluginJsonContext : JsonSerializerContext;

/// <summary>One message per line between Resonate and the plugin helper: compact, and nulls left out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HostMessage))]
[JsonSerializable(typeof(NowPlaying))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;

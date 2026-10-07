using System.Text.Json;

namespace Resonate.Plugins.Protocol;

/// <summary>JSON for what plugins see, shared by Resonate and the helper.</summary>
public static class ProtocolJson
{
    /// <summary>What is playing as JSON ("null" when nothing is), as plugins receive it.</summary>
    public static string Serialize(NowPlaying? state) =>
        state is null ? "null" : JsonSerializer.Serialize(state, ProtocolJsonContext.Default.NowPlaying);
}

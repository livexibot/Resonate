using Resonate.PluginHost;
using Resonate.Plugins.Protocol;

// The helper that runs Resonate's plugins. Resonate downloads it the first
// time a plugin is turned on, starts it while any plugin is on, and talks to
// it over standard input and output (one JSON message per line). It exits
// when Resonate tells it to or when Resonate goes away.
if (!Console.IsInputRedirected)
{
    Console.Error.WriteLine("This program runs Resonate's plugins. Resonate starts it when a plugin is turned on.");
    return 1;
}

using var channel = new MessageChannel(Console.OpenStandardInput(), Console.OpenStandardOutput());
using var loop = new HostLoop(channel);
await loop.RunAsync(CancellationToken.None).ConfigureAwait(false);
return 0;

using Resonate.Plugins.Protocol;

namespace Resonate.PluginHost;

/// <summary>
/// Reads Resonate's messages and passes them to the plugins. Each plugin
/// runs on its own thread (see <see cref="PluginInstance"/>), so this loop
/// never waits for one.
/// </summary>
public sealed class HostLoop : IDisposable
{
    public const string Version = "1";

    private readonly MessageChannel _channel;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, PluginInstance> _plugins = new(StringComparer.Ordinal);
    private NowPlaying? _state;

    public HostLoop(MessageChannel channel, TimeProvider? time = null)
    {
        _channel = channel;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Runs until Resonate says to stop or goes away.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _channel.Send(new HostMessage { Type = MessageTypes.Ready, Version = Version });
        await foreach (var message in _channel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (message.Type == MessageTypes.Shutdown)
            {
                break;
            }

            Handle(message);
        }

        Dispose();
    }

    public void Dispose()
    {
        foreach (var plugin in _plugins.Values)
        {
            plugin.Dispose();
        }

        _plugins.Clear();
    }

    private void Handle(HostMessage message)
    {
        switch (message.Type)
        {
            case MessageTypes.Load when message is { Plugin: { } id, Folder: { } folder, Main: { } main }:
                Unload(id);
                var plugin = new PluginInstance(id, folder, main, message.Permissions ?? [], message.Settings, message.Storage, Send, _time);
                _plugins[id] = plugin;
                plugin.Start();
                if (_state is not null)
                {
                    plugin.OnState(_state);
                }

                break;

            case MessageTypes.Unload when message.Plugin is { } id:
                Unload(id);
                break;

            case MessageTypes.State:
                _state = message.State;
                foreach (var each in _plugins.Values)
                {
                    each.OnState(message.State);
                }

                break;

            case MessageTypes.Settings when message is { Plugin: { } id, Settings: { } settings }:
                if (_plugins.TryGetValue(id, out var target))
                {
                    target.OnSettings(settings);
                }

                break;

            case MessageTypes.Invoke when message is { Plugin: { } id, Command: { } command }:
                if (_plugins.TryGetValue(id, out var invoked))
                {
                    invoked.OnInvoke(command);
                }

                break;

            case MessageTypes.Ping:
                // Each plugin answers once its queue reaches this point; the last answer sends the pong.
                var waiting = _plugins.Count + 1;
                void Answered()
                {
                    if (Interlocked.Decrement(ref waiting) == 0)
                    {
                        Send(new HostMessage { Type = MessageTypes.Pong, Text = message.Text });
                    }
                }

                foreach (var each in _plugins.Values)
                {
                    each.AfterQueued(Answered);
                }

                Answered();
                break;
        }
    }

    private void Unload(string id)
    {
        if (_plugins.Remove(id, out var plugin))
        {
            plugin.Dispose();
        }
    }

    private void Send(HostMessage message) => _channel.Send(message);
}

using System.IO.Pipes;
using Microsoft.Extensions.Time.Testing;
using Resonate.PluginHost;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins.Tests.Fakes;

/// <summary>Runs the real helper loop in this process instead of starting a program.</summary>
internal sealed class InProcessLauncher : IPluginHostLauncher
{
    private readonly TimeProvider _time;

    public InProcessLauncher(TimeProvider? time = null) => _time = time ?? new FakeTimeProvider();

    public List<string> StartedFolders { get; } = [];

    public InProcessHost? Current { get; private set; }

    public IPluginHostProcess Start(string hostFolder)
    {
        StartedFolders.Add(hostFolder);
        Current = new InProcessHost(_time);
        return Current;
    }

    internal sealed class InProcessHost : IPluginHostProcess
    {
        private readonly AnonymousPipeServerStream _toHost = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromHost = new(PipeDirection.In);
        private readonly MessageChannel _hostSide;
        private readonly Task _loop;

        public InProcessHost(TimeProvider time)
        {
            _hostSide = new MessageChannel(
                new AnonymousPipeClientStream(PipeDirection.In, _toHost.ClientSafePipeHandle),
                new AnonymousPipeClientStream(PipeDirection.Out, _fromHost.ClientSafePipeHandle));
            Channel = new MessageChannel(_fromHost, _toHost);
            var loop = new HostLoop(_hostSide, time);
            _loop = Task.Run(() => loop.RunAsync(CancellationToken.None));
        }

        public MessageChannel Channel { get; }

        public long? MemoryBytes => 1024 * 1024;

        public string? CrashReport => "pretend crash";

        public bool IsDisposed { get; private set; }

        /// <summary>The helper dies: Resonate sees its output end.</summary>
        public void Crash() => _hostSide.Dispose();

        public void Dispose()
        {
            IsDisposed = true;
            Channel.Send(new HostMessage { Type = MessageTypes.Shutdown });
            _loop.Wait(TimeSpan.FromSeconds(5));
            Channel.Dispose();
            _hostSide.Dispose();
        }
    }
}

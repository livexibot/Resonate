using System.Diagnostics;
using Resonate.Plugins.Protocol;

namespace Resonate.Plugins;

/// <summary>Starts the plugin helper.</summary>
public interface IPluginHostLauncher
{
    /// <summary>Starts the helper installed in <paramref name="hostFolder"/>.</summary>
    IPluginHostProcess Start(string hostFolder);
}

/// <summary>A running plugin helper.</summary>
public interface IPluginHostProcess : IDisposable
{
    MessageChannel Channel { get; }

    /// <summary>The helper's memory in use, when it can be read.</summary>
    long? MemoryBytes { get; }

    /// <summary>The last lines the helper wrote about a crash, if any.</summary>
    string? CrashReport { get; }
}

/// <summary>
/// Runs the helper as its own process, at below-normal priority so it never
/// competes with the window, talking over its standard input and output.
/// It exits by itself when Resonate does (its input closes).
/// </summary>
public sealed class ProcessPluginHostLauncher : IPluginHostLauncher
{
    public const string ExecutableName = "Resonate.PluginHost";

    private readonly Func<string, ProcessStartInfo>? _startInfo;

    /// <param name="startInfo">How to start the helper in a folder; the default runs its executable.</param>
    public ProcessPluginHostLauncher(Func<string, ProcessStartInfo>? startInfo = null) => _startInfo = startInfo;

    public static string ExecutablePath(string hostFolder) =>
        Path.Combine(hostFolder, OperatingSystem.IsWindows() ? ExecutableName + ".exe" : ExecutableName);

    public IPluginHostProcess Start(string hostFolder)
    {
        var info = _startInfo?.Invoke(hostFolder) ?? new ProcessStartInfo(ExecutablePath(hostFolder));
        info.UseShellExecute = false;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        info.WorkingDirectory = hostFolder;

        var process = Process.Start(info) ?? throw new InvalidOperationException("The plugin helper did not start.");
        return new HostProcess(process);
    }

    private sealed class HostProcess : IPluginHostProcess
    {
        private const int KeptErrorLines = 20;

        private readonly Process _process;
        private readonly Queue<string> _errors = new();

        public HostProcess(Process process)
        {
            _process = process;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                // Already gone, or not allowed here; it still works at normal priority.
            }

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (_errors)
                {
                    _errors.Enqueue(e.Data.Length > 300 ? e.Data[..300] : e.Data);
                    while (_errors.Count > KeptErrorLines)
                    {
                        _errors.Dequeue();
                    }
                }
            };
            process.BeginErrorReadLine();
            Channel = new MessageChannel(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        }

        public MessageChannel Channel { get; }

        public long? MemoryBytes
        {
            get
            {
                try
                {
                    _process.Refresh();
                    return _process.HasExited ? null : _process.WorkingSet64;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public string? CrashReport
        {
            get
            {
                lock (_errors)
                {
                    return _errors.Count == 0 ? null : string.Join('\n', _errors);
                }
            }
        }

        public void Dispose()
        {
            Channel.Send(new HostMessage { Type = MessageTypes.Shutdown });
            try
            {
                if (!_process.WaitForExit(TimeSpan.FromSeconds(2)))
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }

            Channel.Dispose();
            _process.Dispose();
        }
    }
}

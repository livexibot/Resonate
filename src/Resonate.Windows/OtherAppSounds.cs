using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Pause for other sounds, a built-in plugin: whether another program plays
/// sound on the default output right now (a call, a video), by the level
/// meter Windows keeps for each program's sound in the mixer. Nothing is
/// heard or recorded: only how loud each program is at this moment.
/// Resonate itself, its own player and Spotify do not count.
/// </summary>
public static class OtherAppSounds
{
    /// <summary>A program louder than this (0 to 1, its loudest channel) plays sound.</summary>
    public const float Audible = 0.02f;

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>
    /// True when a program other than Resonate (and what it started) or
    /// Spotify is heard now. Any thread; false when Windows does not answer.
    /// </summary>
    public static bool AnyPlaying()
    {
        try
        {
            Marshal.ThrowExceptionForHR(Ole32.CoCreateInstance(
                CoreAudioIds.MMDeviceEnumerator, 0, CoreAudioIds.ClsCtxAll, CoreAudioIds.IMMDeviceEnumerator, out var pointer));
            IMMDeviceEnumerator enumerator;
            try
            {
                enumerator = (IMMDeviceEnumerator)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            var device = enumerator.GetDefaultAudioEndpoint(CoreAudioIds.ERender, CoreAudioIds.EMultimedia);
            var sessions = device.Activate(CoreAudioIds.IAudioSessionManager2, CoreAudioIds.ClsCtxAll, 0).GetSessionEnumerator();
            var count = sessions.GetCount();
            Dictionary<uint, uint>? parents = null;
            var own = (uint)Environment.ProcessId;
            for (var i = 0; i < count; i++)
            {
                var control = sessions.GetSession(i);
                if (control.GetProcessId(out var processId) < 0 || processId == 0 || control is not IAudioMeterInformation meter)
                {
                    continue;
                }

                if (meter.GetPeakValue() <= Audible)
                {
                    continue;
                }

                parents ??= ProcessTree.Parents();
                if (!ProcessTree.Descends(processId, own, parents) && !ProcessImage.IsSpotify(processId))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Which program started which, read once from Windows' process list.</summary>
internal static unsafe partial class ProcessTree
{
    private const uint SnapProcess = 0x2;

    /// <summary>Every running program's parent, by process ID.</summary>
    public static Dictionary<uint, uint> Parents()
    {
        var parents = new Dictionary<uint, uint>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
        {
            return parents;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                parents[entry.ProcessId] = entry.ParentProcessId;
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    /// <summary>Whether <paramref name="processId"/> is <paramref name="root"/> or was started by it, or by what it started.</summary>
    public static bool Descends(uint processId, uint root, Dictionary<uint, uint> parents)
    {
        var current = processId;
        for (var depth = 0; depth < 16; depth++)
        {
            if (current == root)
            {
                return true;
            }

            if (!parents.TryGetValue(current, out var parent) || parent == 0 || parent == current)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, ref ProcessEntry entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    /// <summary>PROCESSENTRY32W.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }
}

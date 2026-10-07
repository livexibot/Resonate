using System.Runtime.InteropServices;

namespace Resonate.Windows.Interop;

internal static partial class User32
{
    public const int SwShowMinNoActive = 7;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);
}

internal static partial class Advapi32
{
    public const uint CredTypeGeneric = 1;
    public const uint CredPersistLocalMachine = 2;
    public const int ErrorNotFound = 1168;

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredWrite(in Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    public static partial void CredFree(nint buffer);

    /// <summary>CREDENTIALW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }
}

internal static partial class Windowing
{
    public const int SwHide = 0;
    public const int SwRestore = 9;
    public const int SwShowMinNoActive = 7;
    public const uint GwOwner = 4;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExAppWindow = 0x00040000;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint window);
}

internal static partial class Processes
{
    public const uint QueryLimitedInformation = 0x1000;
    public const uint QueryInformation = 0x0400;
    public const uint SetInformation = 0x0200;
    public const uint SetQuota = 0x0100;
    public const uint IdlePriorityClass = 0x40;
    public const uint NormalPriorityClass = 0x20;
    public const int ProcessPowerThrottling = 4;
    public const uint PowerThrottlingCurrentVersion = 1;
    public const uint PowerThrottlingExecutionSpeed = 0x1;
    public const int ProcessCommandLineInformation = 60;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(nint process, uint priorityClass);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(nint process);

    [LibraryImport("ntdll.dll")]
    public static unsafe partial int NtQueryInformationProcess(nint process, int informationClass, void* information, uint length, out uint returnLength);

    /// <summary>PROCESS_POWER_THROTTLING_STATE.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>UNICODE_STRING.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }
}

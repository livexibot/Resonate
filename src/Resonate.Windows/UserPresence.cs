using System.Runtime.InteropServices;

namespace Resonate.Windows;

/// <summary>
/// Whether someone is at the computer and what has the screen, for the
/// away screen and the stage's clouds (built-in plugins): when the keyboard
/// or mouse was last touched in this session, the window in front, and
/// whether a full-screen game or video, a presentation or a locked screen
/// has the display. Nothing here watches anything; each call asks once.
/// </summary>
public static partial class UserPresence
{
    // SHQueryUserNotificationState's answers (QUERY_USER_NOTIFICATION_STATE).
    private const int NotPresent = 1;
    private const int Busy = 2;
    private const int RunningFullScreen = 3;
    private const int PresentationMode = 4;

    /// <summary>
    /// The tick count (milliseconds, as <see cref="Environment.TickCount"/>)
    /// of the last keyboard or mouse input in this session; null when Windows
    /// does not say.
    /// </summary>
    public static uint? LastInputTick
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            return GetLastInputInfo(ref info) ? info.Time : null;
        }
    }

    /// <summary>How long nobody has touched the keyboard or mouse; zero when Windows does not say.</summary>
    public static TimeSpan IdleTime =>
        LastInputTick is { } last
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - last))
            : TimeSpan.Zero;

    /// <summary>Whether <paramref name="window"/> is the window in front.</summary>
    public static bool IsForeground(nint window) => window != 0 && GetForegroundWindow() == window;

    /// <summary>
    /// True while a full-screen game or video, a presentation, a busy
    /// full-screen app or the lock screen (or screen saver) has the display.
    /// </summary>
    public static bool IsScreenTaken()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
            {
                return false;
            }

            return state is NotPresent or Busy or RunningFullScreen or PresentationMode;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    /// <summary>LASTINPUTINFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}

using System.Globalization;
using Microsoft.UI.Xaml;

namespace Resonate.App.Services;

/// <summary>
/// Keeps Resonate open when something goes wrong (the owner's request,
/// 9 October 2026: "make sure the app never crashes, ever"). A mistake that
/// reaches the interface thread unhandled is written to <c>crash.log</c> in
/// the cache folder and marked handled, so the window stays; one in a task
/// nobody waited for is written down and observed; one on another thread,
/// which .NET always ends the app for, is at least written down. Each entry
/// is the time, where, the exception's type, message and stack: never a
/// token (Resonate never puts one in an exception). CI's tours still
/// record every error with handlers of their own.
/// </summary>
internal static class CrashGuard
{
    private const long MaxBytes = 512 * 1024;

    // A mistake that repeats every frame is written down this often at most per run.
    private const int MostEntries = 100;

    private static readonly Lock Gate = new();
    private static string? _folder;
    private static int _entries;

    private static string? LogPath => _folder is null ? null : Path.Combine(_folder, "crash.log");

    public static void Start(Application app, string folder)
    {
        _folder = folder;
        app.UnhandledException += (_, e) =>
        {
            Write("interface", e.Exception, e.Message);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("task", e.Exception, null);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("fatal", e.ExceptionObject as Exception, null);
    }

    /// <summary>Writes one entry; a log that cannot be written is never worth more than the app itself.</summary>
    public static void Write(string where, Exception? exception, string? message)
    {
        if (LogPath is not { } path || Interlocked.Increment(ref _entries) > MostEntries)
        {
            return;
        }

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {where}: {exception?.GetType().FullName ?? "?"}: {exception?.Message ?? message}{Environment.NewLine}{exception?.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(_folder!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, Path.Combine(_folder!, "crash.old.log"), overwrite: true);
                }

                File.AppendAllText(path, text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do.
            }
        }
    }
}

using System.Globalization;
using Resonate.Spotify.Playback;

namespace Resonate.App.Services;

/// <summary>
/// Keeps <see cref="PlaybackLog"/>'s lines in <c>playback.log</c> in the
/// cache folder, each with the time, so a playback problem can be looked at
/// afterwards. At most about 256 KB: past that the file starts again, the
/// last one kept as <c>playback.old.log</c>. Never used in demo mode.
/// </summary>
internal static class PlaybackLogFile
{
    private const long MaxBytes = 256 * 1024;

    private static readonly Lock Gate = new();

    public static void Start(string folder)
    {
        var path = Path.Combine(folder, "playback.log");
        var old = Path.Combine(folder, "playback.old.log");
        PlaybackLog.Sink = line =>
        {
            var text = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        File.Move(path, old, overwrite: true);
                    }

                    File.AppendAllText(path, text);
                }
                catch (IOException)
                {
                    // A log line is never worth more than playback itself.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        };
        PlaybackLog.Note($"Resonate {AppInfo.Version} started");
    }
}

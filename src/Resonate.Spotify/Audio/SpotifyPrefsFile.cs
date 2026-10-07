using System.Text;

namespace Resonate.Spotify.Audio;

/// <summary>What Resonate read from the Spotify app's per-account settings file.</summary>
/// <param name="Path">The file it came from.</param>
/// <param name="Equalizer">Spotify's equalizer as the file has it.</param>
/// <param name="IsLossless">Whether Spotify streams in Lossless; null when the file does not say.</param>
public sealed record SpotifyAudioPrefs(string Path, EqualizerSettings Equalizer, bool? IsLossless);

/// <summary>
/// Finds, reads and writes the Spotify app's per-account settings file,
/// "Users\&lt;name&gt;-user\prefs" inside one of Spotify's folders. The
/// global "prefs" next to "Users" holds sign-in data and is never opened.
/// Nothing read from these files is ever logged.
/// </summary>
public static class SpotifyPrefsFile
{
    public const string FileName = "prefs";

    /// <summary>The one copy of the file as it was before Resonate first changed it ("prefs.resonate-backup").</summary>
    public const string BackupSuffix = ".resonate-backup";

    private const string UsersFolder = "Users";
    private const string AccountFolderPattern = "*-user";
    private const string NewFileSuffix = ".resonate-new";

    /// <summary>Strict, so a file that is not valid text is left alone rather than damaged.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Every per-account settings file inside <paramref name="spotifyFolders"/>
    /// (Spotify's own folders, such as "%APPDATA%\Spotify"), most recently
    /// written first: that is the account signed in last.
    /// </summary>
    public static IReadOnlyList<string> FindAll(IEnumerable<string> spotifyFolders)
    {
        var found = new List<(string Path, DateTime Written)>();
        foreach (var folder in spotifyFolders)
        {
            var users = Path.Combine(folder, UsersFolder);
            try
            {
                if (!Directory.Exists(users))
                {
                    continue;
                }

                foreach (var account in Directory.EnumerateDirectories(users, AccountFolderPattern))
                {
                    var file = Path.Combine(account, FileName);
                    if (File.Exists(file))
                    {
                        found.Add((file, File.GetLastWriteTimeUtc(file)));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder Resonate may not read: look in the others.
            }
        }

        return [.. found.OrderByDescending(f => f.Written).Select(f => f.Path)];
    }

    /// <summary>The settings file of the account that used Spotify last, or null when there is none.</summary>
    public static string? FindNewest(IEnumerable<string> spotifyFolders) => FindAll(spotifyFolders) is [var newest, ..] ? newest : null;

    /// <summary>The equalizer and streaming quality in <paramref name="path"/>, or null when it can not be read.</summary>
    public static SpotifyAudioPrefs? TryRead(string path)
    {
        try
        {
            var prefs = SpotifyPrefs.Parse(ReadText(path, out _));
            return new SpotifyAudioPrefs(path, SpotifyPrefs.ReadEqualizer(prefs), SpotifyPrefs.IsLossless(prefs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> into Spotify's settings file, changing
    /// only the equalizer's lines. The first time, the file is copied to
    /// "prefs.resonate-backup" next to it. The new file is written in full
    /// beside the old one and then swapped in, so Spotify never sees half of
    /// it. Only call this while Spotify is not running: it rewrites the file
    /// when it quits.
    /// </summary>
    /// <returns>False when the file already had these settings, so nothing was written.</returns>
    /// <exception cref="IOException">The file could not be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Resonate may not write the file.</exception>
    /// <exception cref="DecoderFallbackException">The file is not text, so it is left alone.</exception>
    public static bool WriteEqualizer(string path, EqualizerSettings settings)
    {
        var text = ReadText(path, out var hasBom);
        var updated = SpotifyPrefs.WriteEqualizer(text, settings);
        if (string.Equals(updated, text, StringComparison.Ordinal))
        {
            return false;
        }

        var backup = path + BackupSuffix;
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
        }

        var fresh = path + NewFileSuffix;
        try
        {
            using (var stream = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (hasBom)
                {
                    stream.Write(Utf8Bom);
                }

                stream.Write(Utf8.GetBytes(updated));
                stream.Flush(flushToDisk: true);
            }

            File.Replace(fresh, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch
        {
            TryDelete(fresh);
            throw;
        }

        return true;
    }

    private static string ReadText(string path, out bool hasBom)
    {
        byte[] bytes;

        // Spotify may have the file open; reading must not get in its way.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        return Utf8.GetString(hasBom ? bytes.AsSpan(Utf8Bom.Length) : bytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is overwritten next time.
        }
    }
}

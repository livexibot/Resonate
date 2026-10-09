namespace Resonate.App.Services;

/// <summary>What a media shortcut does (Media shortcuts, a built-in plugin).</summary>
internal enum MediaShortcut
{
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Like,
}

/// <summary>Where each media shortcut's keys are kept in the settings file.</summary>
internal static class MediaShortcutKeys
{
    public static IReadOnlyList<MediaShortcut> All { get; } = Enum.GetValues<MediaShortcut>();

    public static string? Read(AppSettings settings, MediaShortcut action) => action switch
    {
        MediaShortcut.PlayPause => settings.ShortcutPlayPause,
        MediaShortcut.Next => settings.ShortcutNext,
        MediaShortcut.Previous => settings.ShortcutPrevious,
        MediaShortcut.VolumeUp => settings.ShortcutVolumeUp,
        MediaShortcut.VolumeDown => settings.ShortcutVolumeDown,
        _ => settings.ShortcutLike,
    };

    public static void Write(AppSettings settings, MediaShortcut action, string? keys)
    {
        switch (action)
        {
            case MediaShortcut.PlayPause:
                settings.ShortcutPlayPause = keys;
                break;
            case MediaShortcut.Next:
                settings.ShortcutNext = keys;
                break;
            case MediaShortcut.Previous:
                settings.ShortcutPrevious = keys;
                break;
            case MediaShortcut.VolumeUp:
                settings.ShortcutVolumeUp = keys;
                break;
            case MediaShortcut.VolumeDown:
                settings.ShortcutVolumeDown = keys;
                break;
            default:
                settings.ShortcutLike = keys;
                break;
        }
    }
}

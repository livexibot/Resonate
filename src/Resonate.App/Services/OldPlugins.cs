using System.Text.Json;

namespace Resonate.App.Services;

/// <summary>
/// Until 10 October 2026 two plugins, Sleep timer and Skip rules, were
/// downloaded with a helper that ran them. Both are built in now, under the
/// same IDs. Once, at the first start after that: a plugin that was on is
/// turned on, its settings are carried over, and the old plugins file and
/// folder are deleted.
/// </summary>
internal static class OldPlugins
{
    public static void MoveIn(AppSettings settings, Action save)
    {
        if (File.Exists(AppPaths.PluginsFile))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.PluginsFile));
                if (MoveIn(document.RootElement, settings))
                {
                    save();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Not worth stopping for: the plugins are simply off.
            }

            TryDelete(() => File.Delete(AppPaths.PluginsFile));
        }

        if (Directory.Exists(AppPaths.PluginsFolder))
        {
            // The helper is about 11 MB; deleting it never holds up the start.
            _ = Task.Run(() => TryDelete(() => Directory.Delete(AppPaths.PluginsFolder, recursive: true)));
        }
    }

    /// <summary>Takes over what was on and its settings; true when anything changed.</summary>
    internal static bool MoveIn(JsonElement root, AppSettings settings)
    {
        var changed = false;
        if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in enabled.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null))
            {
                if (id is BuiltInPlugins.SleepTimer or BuiltInPlugins.SkipSongs && !settings.BuiltInPlugins.Contains(id))
                {
                    settings.BuiltInPlugins.Add(id);
                    changed = true;
                }
            }
        }

        if (!root.TryGetProperty("settings", out var all) || all.ValueKind != JsonValueKind.Object)
        {
            return changed;
        }

        if (all.TryGetProperty(BuiltInPlugins.SleepTimer, out var sleep) && sleep.ValueKind == JsonValueKind.Object)
        {
            if (sleep.TryGetProperty("fade", out var fade) && fade.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                settings.SleepTimerFade = fade.GetBoolean();
                changed = true;
            }

            if (sleep.TryGetProperty("customMinutes", out var minutes) && minutes.TryGetInt32(out var m))
            {
                settings.SleepTimerMinutes = Math.Clamp(m, 5, 240);
                changed = true;
            }
        }

        if (all.TryGetProperty(BuiltInPlugins.SkipSongs, out var skip) && skip.ValueKind == JsonValueKind.Object)
        {
            changed |= Lines(skip, "artists", settings.SkipArtists);
            changed |= Lines(skip, "versions", settings.SkipVersions);
            changed |= Lines(skip, "songs", settings.SkipSongs);
            if (skip.TryGetProperty("longerThan", out var longer) && longer.TryGetInt32(out var l))
            {
                settings.SkipLongerThanMinutes = Math.Clamp(l, 0, 120);
                changed = true;
            }

            if (skip.TryGetProperty("shorterThan", out var shorter) && shorter.TryGetInt32(out var s))
            {
                settings.SkipShorterThanSeconds = Math.Clamp(s, 0, 300);
                changed = true;
            }
        }

        return changed;
    }

    private static bool Lines(JsonElement plugin, string key, List<string> into)
    {
        if (!plugin.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var changed = false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } line)
            {
                changed |= Resonate.Spotify.Playback.SkipFilter.AddTo(into, line);
            }
        }

        return changed;
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for next time.
        }
    }
}

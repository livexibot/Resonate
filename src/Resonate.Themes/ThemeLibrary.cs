using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// The presets, the user's saved looks, and which one is in use. Presets
/// never change: editing one starts a "Custom" look based on it, which can
/// then be saved under a name. Editing a saved look changes it in place.
/// The app stores <see cref="ActiveId"/>, <see cref="Custom"/> and
/// <see cref="Saved"/> after every change.
/// </summary>
public sealed class ThemeLibrary
{
    public const string CustomId = "custom";
    public const int MaxSavedLooks = 48;

    private readonly List<ThemeDefinition> _saved;

    public ThemeLibrary(string? activeId, ThemeDefinition? custom, IEnumerable<ThemeDefinition>? saved)
    {
        Custom = custom is null ? null : custom.Normalize() with { Id = CustomId };
        _saved = [];
        foreach (var look in saved ?? [])
        {
            var clean = look.Normalize();
            if (_saved.Count < MaxSavedLooks
                && !ThemePresets.IsPreset(clean.Id)
                && clean.Id != CustomId
                && _saved.All(s => s.Id != clean.Id))
            {
                _saved.Add(clean);
            }
        }

        Select(activeId);
    }

    public string ActiveId { get; private set; } = ThemePresets.Default.Id;

    /// <summary>The look in use.</summary>
    public ThemeDefinition Active => Find(ActiveId) ?? ThemePresets.Default;

    /// <summary>The unsaved look made by editing a preset, if there is one.</summary>
    public ThemeDefinition? Custom { get; private set; }

    public IReadOnlyList<ThemeDefinition> Saved => _saved;

    public bool ActiveIsPreset => ThemePresets.IsPreset(ActiveId);

    public ThemeDefinition? Find(string? id) =>
        id == CustomId ? Custom : ThemePresets.Find(id) ?? _saved.FirstOrDefault(s => s.Id == id);

    /// <summary>Uses a preset or saved look; anything unknown falls back to the default.</summary>
    public ThemeDefinition Select(string? id)
    {
        ActiveId = Find(id) is { } look ? look.Id : ThemePresets.Default.Id;
        return Active;
    }

    /// <summary>
    /// Changes the look in use. A preset is copied into the custom look
    /// first, so presets always stay as they shipped.
    /// </summary>
    public ThemeDefinition Edit(Func<ThemeDefinition, ThemeDefinition> change)
    {
        var current = Active;
        var changed = change(current).Normalize();

        if (ActiveIsPreset || ActiveId == CustomId)
        {
            Custom = changed with { Id = CustomId, Name = ActiveIsPreset ? $"{current.Name} (custom)" : current.Name };
            ActiveId = CustomId;
            return Custom;
        }

        var index = _saved.FindIndex(s => s.Id == ActiveId);
        _saved[index] = changed with { Id = current.Id, Name = current.Name };
        return _saved[index];
    }

    /// <summary>Saves the look in use under a name and switches to the saved copy.</summary>
    public ThemeDefinition SaveAs(string name) => Add(Active with { Name = name });

    /// <summary>Adds a look (for example one pasted as text) and switches to it.</summary>
    public ThemeDefinition Add(ThemeDefinition look)
    {
        look = look.Normalize();
        if (_saved.Count >= MaxSavedLooks)
        {
            _saved.RemoveAt(0);
        }

        var name = UniqueName(look.Name);
        var saved = look with { Id = NewId(name), Name = name };
        _saved.Add(saved);
        if (ActiveId == CustomId)
        {
            Custom = null;
        }

        ActiveId = saved.Id;
        return saved;
    }

    public void Rename(string id, string name)
    {
        var index = _saved.FindIndex(s => s.Id == id);
        if (index >= 0)
        {
            var current = _saved[index];
            var cleaned = (current with { Name = name }).Normalize().Name;
            _saved[index] = current with { Name = UniqueName(cleaned, except: id) };
        }
    }

    /// <summary>Removes a saved look (or the custom look); if it was in use, the default preset takes over.</summary>
    public void Delete(string id)
    {
        if (id == CustomId)
        {
            Custom = null;
        }

        _saved.RemoveAll(s => s.Id == id);
        if (ActiveId == id)
        {
            Select(ThemePresets.Default.Id);
        }
    }

    private string UniqueName(string name, string? except = null)
    {
        var taken = _saved
            .Where(s => s.Id != except)
            .Select(s => s.Name)
            .Concat(ThemePresets.All.Select(p => p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{name} {n}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string NewId(string name)
    {
        var slug = ThemeJson.Slug(name);
        var id = "look-" + (slug.Length > 0 ? slug : "untitled");
        var unique = id;
        for (var n = 2; Find(unique) is not null; n++)
        {
            unique = string.Create(CultureInfo.InvariantCulture, $"{id}-{n}");
        }

        return unique;
    }
}

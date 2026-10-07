using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>
/// The classic player's skins: the built-in one and those the user added
/// (copied into <see cref="AppPaths.SkinsFolder"/>). Loads the chosen skin off
/// the interface thread and says when it changes.
/// </summary>
public sealed class SkinLibrary
{
    public SkinLibrary(AppSettings settings, string folder)
    {
        Settings = settings;
        Folder = new SkinFolder(folder);
    }

    /// <summary>Raised on the interface thread when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    public AppSettings Settings { get; }

    public SkinFolder Folder { get; }

    /// <summary>The skin in use; the built-in one until a chosen skin has loaded (or when it cannot be read).</summary>
    public Skin Current { get; private set; } = Skin.BuiltIn;

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

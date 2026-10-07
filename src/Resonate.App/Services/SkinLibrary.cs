using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Resonate.Themes.Skins;

namespace Resonate.App.Services;

/// <summary>A skin the classic player can use: the built-in one (no file) or one the user added.</summary>
public sealed record SkinChoice(string? FileName, string Label)
{
    public bool IsBuiltIn => FileName is null;
}

/// <summary>
/// The classic player's skins: the built-in one and those the user added
/// (copied into <see cref="AppPaths.SkinsFolder"/>). Loads the chosen skin off
/// the interface thread and says when it changes. It also keeps the classic
/// player's options (size, shade mode, visualiser, time display), so the
/// player and Settings always agree. Use it on the interface thread.
/// </summary>
public sealed class SkinLibrary
{
    /// <summary>What the built-in skin is called in lists (drawing the skin just to read its name would cost time).</summary>
    public const string BuiltInLabel = "Resonate Classic";

    private Skin? _current;
    private IReadOnlyList<SkinChoice>? _choices;
    private bool _started;
    private int _loadVersion;
    private int _listVersion;

    public SkinLibrary(AppSettings settings, string folder)
    {
        Settings = settings;
        Folder = new SkinFolder(folder);
    }

    /// <summary>Raised on the interface thread when <see cref="Current"/> or the list of skins changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised on the interface thread when one of the classic player's options changes (including whether it is used at all).</summary>
    public event EventHandler? OptionsChanged;

    public AppSettings Settings { get; }

    public SkinFolder Folder { get; }

    /// <summary>The skin in use; the built-in one until a chosen skin has loaded (or when it cannot be read).</summary>
    public Skin Current
    {
        // Resonate's own skin is drawn in code: only once it is needed, never at start-up.
        get => _current ?? Skin.BuiltIn;
        private set => _current = value;
    }

    /// <summary>
    /// The skin to draw has loaded (see <see cref="EnsureLoaded"/>). Until
    /// then the classic player leaves its skin area empty rather than wait.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>The chosen skin's file name in the skins folder, or null for the built-in skin.</summary>
    public string? CurrentFile => Settings.ClassicSkin;

    /// <summary>
    /// The built-in skin first, then the user's skins by name. Until the
    /// folder has been read (<see cref="RefreshChoices"/>), the chosen skin
    /// stands in for the rest.
    /// </summary>
    public IReadOnlyList<SkinChoice> Choices => _choices ??= Settings.ClassicSkin is { } file
        ? [new SkinChoice(null, BuiltInLabel), new SkinChoice(file, SkinFolder.Label(file))]
        : [new SkinChoice(null, BuiltInLabel)];

    /// <summary>The classic player is shown in place of the player bar.</summary>
    public bool UsesClassicPlayer
    {
        get => Settings.UsesClassicPlayer;
        set => SetOption(Settings.UsesClassicPlayer, value, on => Settings.PlayerStyle = on ? "classic" : "modern");
    }

    /// <summary>Winamp's double size: every skin pixel twice as large.</summary>
    public bool DoubleSize
    {
        get => Settings.ClassicDoubleSize;
        set => SetOption(Settings.ClassicDoubleSize, value, on => Settings.ClassicDoubleSize = on);
    }

    /// <summary>Shade mode: only the skin's 14-pixel title strip.</summary>
    public bool Shaded
    {
        get => Settings.ClassicShaded;
        set => SetOption(Settings.ClassicShaded, value, on => Settings.ClassicShaded = on);
    }

    public VisualiserMode Visualiser
    {
        get => Settings.ParsedClassicVisualiser;
        set => SetOption(Settings.ParsedClassicVisualiser, value, mode => Settings.ClassicVisualiser = mode.ToString());
    }

    /// <summary>The time display counts down (time left) instead of up.</summary>
    public bool ShowRemaining
    {
        get => Settings.ClassicShowRemaining;
        set => SetOption(Settings.ClassicShowRemaining, value, on => Settings.ClassicShowRemaining = on);
    }

    /// <summary>
    /// The classic player is about to show: draws the built-in skin and loads
    /// the chosen one in the background (once), then raises <see cref="Changed"/>.
    /// </summary>
    public void EnsureLoaded()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RefreshChoices();
        _ = LoadAsync(Settings.ClassicSkin);
    }

    /// <summary>Reads the skins folder again in the background (Settings does when it opens).</summary>
    public void RefreshChoices() => _ = RefreshChoicesAsync();

    /// <summary>Uses a skin from <see cref="Choices"/> (null for the built-in one). It shows once it has loaded.</summary>
    public void Select(string? fileName)
    {
        if (fileName == Settings.ClassicSkin && (!_started || IsReady))
        {
            return;
        }

        Settings.ClassicSkin = fileName;
        Save();
        if (_started)
        {
            _ = LoadAsync(fileName);
        }
        else
        {
            // The classic player is not showing; it loads the skin when it next does.
            RaiseChanged();
        }
    }

    /// <summary>Lets the user pick a .wsz file, then adds and uses it.</summary>
    public async Task PickAndImportAsync()
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        string? path;
        try
        {
            // The Windows App SDK's picker works without package identity (Resonate is installed by Velopack).
            var picker = new FileOpenPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".wsz");
            picker.FileTypeFilter.Add(".zip");
            path = (await picker.PickSingleFileAsync())?.Path;
        }
        catch (COMException)
        {
            window.ShowMessage("Windows could not open the file picker.", InfoBarSeverity.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(path))
        {
            await ImportAsync(path);
        }
    }

    /// <summary>Copies a skin file into the skins folder (checking it first) and uses it. False when it could not be added.</summary>
    public async Task<bool> ImportAsync(string path)
    {
        string fileName;
        try
        {
            fileName = await Task.Run(() => Folder.Import(path));
        }
        catch (Exception ex)
        {
            // Skin files are untrusted input: whatever is wrong with one, it is simply not added.
            Tell($"That file can't be used as a skin. {Reason(ex)}", InfoBarSeverity.Warning);
            return false;
        }

        await RefreshChoicesAsync();
        Select(fileName);
        Tell($"Added the skin {SkinFolder.Label(fileName)}.", InfoBarSeverity.Success);
        return true;
    }

    /// <summary>Deletes a skin the user added; the built-in skin takes over if it was in use.</summary>
    public void Remove(string fileName) => _ = RemoveAsync(fileName);

    /// <summary>The player could not draw <paramref name="skin"/>: back to the built-in skin, and say so.</summary>
    internal void ReportBroken(Skin skin, Exception error)
    {
        if (skin.IsBuiltIn || !ReferenceEquals(skin, _current))
        {
            return;
        }

        var name = Settings.ClassicSkin is { } file ? SkinFolder.Label(file) : skin.Name;
        _loadVersion++;
        Settings.ClassicSkin = null;
        Save();
        _current = null;
        IsReady = true;
        Tell($"The skin {name} couldn't be drawn, so Resonate's own skin is showing. {Reason(error)}", InfoBarSeverity.Warning);
        RaiseChanged();
    }

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private async Task LoadAsync(string? fileName)
    {
        var version = ++_loadVersion;
        Skin? skin = null;
        Exception? failure = null;
        try
        {
            skin = await Task.Run(() =>
            {
                // Drawing the built-in skin here, off the interface thread, also readies whatever a user's skin lacks.
                var builtIn = Skin.BuiltIn;
                return fileName is null ? builtIn : Folder.Load(fileName);
            });
        }
        catch (Exception ex) when (fileName is not null)
        {
            // A skin is untrusted input: whatever goes wrong reading it, the built-in skin takes over.
            failure = ex;
        }
        catch (Exception)
        {
            // The built-in skin failing is a bug: the player draws it again on the interface thread,
            // where the error shows (and fails CI's screenshot tour).
        }

        if (version != _loadVersion)
        {
            // A newer choice is on its way.
            return;
        }

        if (failure is not null && fileName is not null)
        {
            Settings.ClassicSkin = null;
            Save();
            Tell($"The skin {SkinFolder.Label(fileName)} couldn't be used, so Resonate's own skin is showing. {Reason(failure)}", InfoBarSeverity.Warning);
        }

        _current = skin is { IsBuiltIn: false } ? skin : null;
        IsReady = true;
        RaiseChanged();
    }

    private async Task RefreshChoicesAsync()
    {
        var version = ++_listVersion;
        IReadOnlyList<string> files;
        try
        {
            files = await Task.Run(Folder.List);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        if (version != _listVersion)
        {
            return;
        }

        _choices = [new SkinChoice(null, BuiltInLabel), .. files.Select(f => new SkinChoice(f, SkinFolder.Label(f)))];
        RaiseChanged();
    }

    private async Task RemoveAsync(string fileName)
    {
        if (fileName == Settings.ClassicSkin)
        {
            Select(null);
        }

        try
        {
            await Task.Run(() => Folder.Remove(fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Tell($"The skin {SkinFolder.Label(fileName)} could not be removed. {Reason(ex)}", InfoBarSeverity.Warning);
        }

        await RefreshChoicesAsync();
    }

    private void SetOption<T>(T current, T value, Action<T> store)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        store(value);
        Save();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // The library belongs to the app, so it saves and speaks through the app's own window and settings.
    private static void Save() => App.Services.SaveSettings();

    private static void Tell(string message, InfoBarSeverity severity) => App.MainWindow?.ShowMessage(message, severity);

    /// <summary>Why a skin could not be used, in words for the user.</summary>
    private static string Reason(Exception? error) => error switch
    {
        SkinFormatException format => format.Message,
        FileNotFoundException or DirectoryNotFoundException => "The file is no longer there.",
        IOException or UnauthorizedAccessException => "Windows would not let Resonate read the file.",
        _ => "It may be damaged, or not a classic Winamp 2 skin.",
    };
}

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.LocalFiles;

namespace Resonate.App.Controls;

/// <summary>Settings, Local Files: which folders are looked in, adding and removing them (the sidebar entry is under Layout).</summary>
public sealed partial class LocalFoldersPanel : UserControl
{
    private readonly LocalFilesService _localFiles = App.Services.LocalFiles;
    private int _statusQueued;

    public LocalFoldersPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<LocalFolderItem> Folders { get; } = [];

    private bool IsDemo => _localFiles.DemoTracks is not null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowFolders();
        AddFolderButton.IsEnabled = !IsDemo;
        ShowStatus();

        // Loaded can come twice in a row; the handler is held once.
        _localFiles.Library.Changed -= OnLibraryChanged;
        _localFiles.Library.Changed += OnLibraryChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _localFiles.Library.Changed -= OnLibraryChanged;

    private void ShowFolders()
    {
        Folders.Clear();
        foreach (var folder in _localFiles.Folders)
        {
            Folders.Add(new LocalFolderItem(folder, !IsDemo, RemoveFolder));
        }

    }

    private void RemoveFolder(LocalFolderItem item)
    {
        _localFiles.RemoveFolder(item.Path);
        ShowFolders();
    }

    private void OnLibraryChanged(object? sender, LocalLibraryChange change)
    {
        // Scans report often; the text follows at most once per frame.
        if (Interlocked.Exchange(ref _statusQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _statusQueued, 0);
                ShowStatus();
            });
        }
    }

    private void ShowStatus()
    {
        var library = _localFiles.Library;
        var status = library.Status;
        StatusText.Text = IsDemo
            ? "Demo mode: made-up songs; no folders are read."
            : _localFiles.Folders.Count == 0
                ? "No folders yet. Add one to see your music files."
                : status.IsScanning
                    ? status.ToRead > 0
                        ? $"Reading {status.Read:N0} of {status.ToRead:N0} new or changed files…"
                        : "Looking through the folders…"
                    : status.HasScanned
                        ? $"{Format.SongCount(library.Files.Count)} found."
                        : "Resonate looks through the folders a moment after it starts.";
        RescanButton.IsEnabled = !IsDemo && !status.IsScanning;
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        string? path;
        try
        {
            // The Windows App SDK's picker works without package identity (Resonate is installed by Velopack).
            var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            path = (await picker.PickSingleFolderAsync())?.Path;
        }
        catch (COMException)
        {
            window.ShowMessage("Windows could not open the folder picker.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (_localFiles.Library.IsExcluded(path))
        {
            window.ShowMessage("That folder belongs to the Spotify app. Resonate never looks inside Spotify's own folders.", InfoBarSeverity.Informational);
            return;
        }

        _localFiles.AddFolder(path);
        ShowFolders();
        ShowStatus();
    }

    private void OnRescanClick(object sender, RoutedEventArgs e)
    {
        _localFiles.Rescan();
        RescanButton.IsEnabled = false;
    }


}

/// <summary>One folder in the Local Files settings.</summary>
public sealed partial class LocalFolderItem
{
    private readonly Action<LocalFolderItem> _remove;

    public LocalFolderItem(string path, bool canRemove, Action<LocalFolderItem> remove)
    {
        Path = path;
        CanRemove = canRemove;
        _remove = remove;
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        Name = name.Length > 0 ? name : path;
        RemoveLabel = $"Stop looking in {Name}";
    }

    public string Path { get; }

    public string Name { get; }

    public bool CanRemove { get; }

    public string RemoveLabel { get; }

    public void Remove() => _remove(this);
}

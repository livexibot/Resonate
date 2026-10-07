using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.Themes.Skins;

namespace Resonate.App.Controls;

/// <summary>
/// Settings, Classic player: use it or the player bar, pick, add and remove
/// skins, double size and the visualiser. Opening Settings does no skin work:
/// only the skins folder is listed, in the background.
/// </summary>
public sealed partial class ClassicPlayerPanel : UserControl
{
    private readonly SkinLibrary _skins = App.Services.Skins;

    /// <summary>The file behind each entry of the skin list (null for the built-in skin).</summary>
    private readonly List<string?> _skinFiles = [];

    // True while the controls are set from the settings, so that is not taken as the user's change.
    private bool _loading;

    public ClassicPlayerPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _skins.Changed += OnSkinsChanged;
        _skins.OptionsChanged += OnOptionsChanged;
        AddSkinButton.IsEnabled = !App.Services.IsDemo;
        ShowOptions();
        ShowSkins();
        _skins.RefreshChoices();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _skins.Changed -= OnSkinsChanged;
        _skins.OptionsChanged -= OnOptionsChanged;
    }

    private void OnSkinsChanged(object? sender, EventArgs e) => ShowSkins();

    private void OnOptionsChanged(object? sender, EventArgs e) => ShowOptions();

    private void ShowOptions()
    {
        _loading = true;
        try
        {
            UseSwitch.IsOn = _skins.UsesClassicPlayer;
            DoubleSizeSwitch.IsOn = _skins.DoubleSize;
            VisualiserChoice.SelectedIndex = _skins.Visualiser switch
            {
                VisualiserMode.Oscilloscope => 1,
                VisualiserMode.Off => 2,
                _ => 0,
            };
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowSkins()
    {
        _loading = true;
        try
        {
            // The list is only filled again when the skins themselves changed (not while the user picks one).
            var choices = _skins.Choices;
            if (!choices.Select(c => c.FileName).SequenceEqual(_skinFiles))
            {
                SkinList.Items.Clear();
                _skinFiles.Clear();
                foreach (var choice in choices)
                {
                    SkinList.Items.Add(choice.Label);
                    _skinFiles.Add(choice.FileName);
                }
            }

            var current = _skins.CurrentFile;
            SkinList.SelectedIndex = Math.Max(0, _skinFiles.IndexOf(current));
            RemoveSkinButton.IsEnabled = current is not null && !App.Services.IsDemo;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnUseToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _skins.UsesClassicPlayer = UseSwitch.IsOn;
        }
    }

    private void OnSkinChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = SkinList.SelectedIndex;
        if (_loading || index < 0 || index >= _skinFiles.Count)
        {
            return;
        }

        var file = _skinFiles[index];
        RemoveSkinButton.IsEnabled = file is not null && !App.Services.IsDemo;
        _skins.Select(file);
    }

    private void OnDoubleSizeToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _skins.DoubleSize = DoubleSizeSwitch.IsOn;
        }
    }

    private void OnVisualiserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VisualiserChoice.SelectedIndex < 0)
        {
            return;
        }

        _skins.Visualiser = VisualiserChoice.SelectedIndex switch
        {
            1 => VisualiserMode.Oscilloscope,
            2 => VisualiserMode.Off,
            _ => VisualiserMode.Spectrum,
        };
    }

    private async void OnAddSkinClick(object sender, RoutedEventArgs e)
    {
        AddSkinButton.IsEnabled = false;
        try
        {
            await _skins.PickAndImportAsync();
        }
        finally
        {
            AddSkinButton.IsEnabled = !App.Services.IsDemo;
        }
    }

    private void OnRemoveSkinClick(object sender, RoutedEventArgs e)
    {
        if (_skins.CurrentFile is { } file)
        {
            RemoveSkinButton.IsEnabled = false;
            _skins.Remove(file);
        }
    }
}

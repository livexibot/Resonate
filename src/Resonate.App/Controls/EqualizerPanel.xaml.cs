using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Audio;

namespace Resonate.App.Controls;

/// <summary>
/// The equalizer in Settings: on or off, a preset, and the Spotify app's six
/// bands as sliders. Every change shows at once; <see cref="EqualizerService"/>
/// takes it from there.
/// </summary>
public sealed partial class EqualizerPanel : UserControl
{
    private const string CustomPreset = "Custom";
    private const double SliderHeight = 160;

    private readonly EqualizerService _equalizer = App.Services.Equalizer;
    private readonly Slider[] _sliders = new Slider[EqualizerSettings.Bands.Count];
    private readonly TextBlock[] _gains = new TextBlock[EqualizerSettings.Bands.Count];

    /// <summary>True while the panel sets its own controls, so their change events are not the user's.</summary>
    private bool _showing;

    public EqualizerPanel()
    {
        InitializeComponent();
        BuildBands();
        foreach (var preset in EqualizerPresets.All)
        {
            PresetBox.Items.Add(preset.Name);
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>"+4.5 dB", "0 dB", "−3 dB".</summary>
    internal static string FormatGain(double gainDb) =>
        Math.Abs(gainDb) < 0.005
            ? "0 dB"
            : gainDb.ToString("+0.##;−0.##", CultureInfo.CurrentCulture) + " dB";

    private void BuildBands()
    {
        var captionStyle = (Style)Application.Current.Resources["ResonateCaptionTextStyle"];
        var secondaryStyle = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"];
        for (var band = 0; band < EqualizerSettings.Bands.Count; band++)
        {
            var label = EqualizerSettings.Bands[band].Label;
            BandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gain = new TextBlock
            {
                Style = captionStyle,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = FormatGain(0),
            };
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Height = SliderHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                Minimum = -EqualizerSettings.MaxGainDb,
                Maximum = EqualizerSettings.MaxGainDb,
                StepFrequency = 0.5,
                SmallChange = 0.5,
                LargeChange = 3,
                TickFrequency = EqualizerSettings.MaxGainDb,
                TickPlacement = TickPlacement.Outside,
                Tag = band,
            };
            AutomationProperties.SetName(slider, label);
            slider.ValueChanged += OnBandChanged;

            // The slider handles taps itself; the reset must still hear them.
            slider.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(OnBandDoubleTapped), handledEventsToo: true);

            var column = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(gain);
            column.Children.Add(slider);
            column.Children.Add(new TextBlock
            {
                Style = secondaryStyle,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = label,
            });
            Grid.SetColumn(column, band);
            BandsGrid.Children.Add(column);

            _sliders[band] = slider;
            _gains[band] = gain;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _equalizer.Changed += OnEqualizerChanged;
        Show();

        // Spotify's equalizer may have changed in the Spotify app since.
        _ = _equalizer.RefreshAsync();
        _ = _equalizer.StartWatchingAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _equalizer.Changed -= OnEqualizerChanged;
        _equalizer.StopWatching();
    }

    private void OnEqualizerChanged(object? sender, EventArgs e) => Show();

    /// <summary>Puts the service's settings and status on screen.</summary>
    private void Show()
    {
        _showing = true;
        try
        {
            var settings = _equalizer.Current;
            EnabledSwitch.IsOn = settings.Enabled;
            for (var band = 0; band < _sliders.Length; band++)
            {
                var gain = band < settings.GainsDb.Count ? settings.GainsDb[band] : 0;
                _sliders[band].Value = gain;
                _sliders[band].IsEnabled = settings.Enabled;
                _gains[band].Text = FormatGain(gain);
            }

            ShowPreset(settings.Preset);
            PresetBox.IsEnabled = settings.Enabled;
            ResetButton.IsEnabled = settings.GainsDb.Any(g => Math.Abs(g) >= 0.005);

            StatusText.Text = _equalizer.Status;
            StatusText.Visibility = _equalizer.Status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            RestartSpotifyButton.Visibility = _equalizer.CanRestartSpotify ? Visibility.Visible : Visibility.Collapsed;
            RestartSpotifyButton.IsEnabled = !_equalizer.IsRestarting;

            // With "Spotify Web API only" Resonate leaves the Spotify app alone, so it offers nothing that opens it.
            var usesApp = App.Services.UsesSpotifyApp;
            OpenSpotifySettingsButton.Visibility = usesApp ? Visibility.Visible : Visibility.Collapsed;
            LosslessWarning.IsOpen = usesApp && _equalizer.SpotifyLossless == false;
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Selects the matching preset, or "Custom" (listed only while no preset matches).</summary>
    private void ShowPreset(EqualizerPreset? preset)
    {
        var hasCustom = PresetBox.Items.Count > EqualizerPresets.All.Count;
        if (preset is null)
        {
            if (!hasCustom)
            {
                PresetBox.Items.Add(CustomPreset);
            }

            PresetBox.SelectedIndex = PresetBox.Items.Count - 1;
            return;
        }

        for (var i = 0; i < EqualizerPresets.All.Count; i++)
        {
            if (ReferenceEquals(EqualizerPresets.All[i], preset))
            {
                PresetBox.SelectedIndex = i;
                break;
            }
        }

        if (hasCustom)
        {
            PresetBox.Items.RemoveAt(PresetBox.Items.Count - 1);
        }
    }

    private void Change(EqualizerSettings settings)
    {
        _equalizer.Set(settings);
        Show();
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            Change(_equalizer.Current with { Enabled = EnabledSwitch.IsOn });
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = PresetBox.SelectedIndex;
        if (!_showing && index >= 0 && index < EqualizerPresets.All.Count)
        {
            Change(_equalizer.Current.WithPreset(EqualizerPresets.All[index]));
        }
    }

    private void OnBandChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_showing && sender is Slider { Tag: int band })
        {
            Change(_equalizer.Current.WithGain(band, e.NewValue));
        }
    }

    private void OnBandDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is Slider { Tag: int band, IsEnabled: true })
        {
            Change(_equalizer.Current.WithGain(band, 0));
            e.Handled = true;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) =>
        Change(EqualizerSettings.Flat with { Enabled = _equalizer.Current.Enabled });

    private async void OnRestartSpotifyClick(object sender, RoutedEventArgs e)
    {
        RestartSpotifyButton.IsEnabled = false;
        await _equalizer.RestartSpotifyAsync();
    }

    /// <summary>Opens the Spotify app on its settings page (for Lossless, or its own equalizer).</summary>
    private async void OnOpenSpotifySettingsClick(object sender, RoutedEventArgs e) => await SpotifySettingsLink.OpenAsync();
}

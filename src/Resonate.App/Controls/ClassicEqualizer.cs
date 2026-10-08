using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Pages;
using Resonate.App.Services;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Playback;
using Resonate.Themes.Skins;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The mini player's equalizer window, drawn from the skin as Winamp 2's:
/// ON, AUTO (lays the bands flat), the presets, the curve, the preamp and ten
/// sliders. It sets the same six bands as Settings (Spotify's own
/// equalizer, and local files at once); sliders that share a band move
/// together. Rolled up, it is a bar with little volume and balance sliders.
/// Built in code; it works only while it is in the mini player.
/// </summary>
internal sealed partial class ClassicEqualizer : Grid
{
    /// <summary>Gains move in half decibels, as the sliders in Settings do.</summary>
    private const double GainStep = 0.5;

    private readonly MiniPlayerWindow _mini;
    private readonly AppServices _services = App.Services;
    private readonly SkinLibrary _skins;
    private readonly EqualizerService _equalizer;
    private readonly PlayerRouter _player;
    private readonly Image _view = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SkinSurface _surface;

    private bool _loaded;
    private bool _windowActive = true;
    private bool _renderQueued;
    private int _updateQueued;
    private EqualizerView? _drawn;

    // The pointer
    private EqualizerControl _pressed;
    private int _pressedBand = -1;
    private bool _pressedOver;
    private uint _pointerId;
    private bool _moving;
    private double _grabOffset;
    private double? _dragVolume;
    private int _sentVolume = -1;
    private (EqualizerControl Control, int Band) _hovered = (EqualizerControl.None, -1);

    public ClassicEqualizer(MiniPlayerWindow mini)
    {
        _mini = mini;
        _skins = _services.Skins;
        _equalizer = _services.Equalizer;
        _player = _services.Player;
        _surface = new SkinSurface(_view);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        AutomationPropertiesHelper.SetName(this, "Equalizer");
        Children.Add(_view);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCanceled;
        PointerExited += OnPointerExited;
        DoubleTapped += OnDoubleTapped;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    private bool Shaded => _skins.MiniEqualizerShaded;

    /// <summary>Sizes the window for the mini player's scale (the mini player calls it whenever its size or the display changes).</summary>
    public void SetScale(int scale, double raster)
    {
        if (_surface.Resize(EqualizerLayout.Width, Shaded ? EqualizerLayout.ShadeHeight : EqualizerLayout.Height, scale, raster))
        {
            _drawn = null;
            Invalidate();
        }
    }

    /// <summary>Whether the mini player has focus: the title bar is drawn lit.</summary>
    public void SetWindowActive(bool active)
    {
        if (_windowActive != active)
        {
            _windowActive = active;
            Invalidate();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _skins.Changed += OnSkinsChanged;
        _equalizer.Changed += OnEqualizerChanged;
        _player.StateChanged += OnStateChanged;

        // Spotify's equalizer may have changed in the Spotify app since.
        _ = _equalizer.RefreshAsync();
        _drawn = null;
        Invalidate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        _skins.Changed -= OnSkinsChanged;
        _equalizer.Changed -= OnEqualizerChanged;
        _player.StateChanged -= OnStateChanged;
        _pressed = EqualizerControl.None;
        _moving = false;
    }

    private void OnSkinsChanged(object? sender, EventArgs e)
    {
        _drawn = null;
        Invalidate();
    }

    private void OnEqualizerChanged(object? sender, EventArgs e) => Invalidate();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Only the rolled-up bar shows anything of the player (its volume); state arrives on background threads.
        if (!Shaded || Interlocked.Exchange(ref _updateQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            Invalidate();
        });
    }

    // Drawing

    private void Invalidate()
    {
        if (_renderQueued || !_loaded)
        {
            return;
        }

        _renderQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, Render);
    }

    private void Render()
    {
        _renderQueued = false;
        if (!_loaded || !_skins.IsReady || _surface.Frame is not { } frame)
        {
            return;
        }

        var view = BuildView();
        if (view == _drawn || frame.Height != (view.Shaded ? EqualizerLayout.ShadeHeight : EqualizerLayout.Height))
        {
            return;
        }

        var skin = _skins.Current;
        try
        {
            EqualizerRenderer.Render(skin, view, frame);
        }
        catch (Exception ex) when (!skin.IsBuiltIn)
        {
            // A skin the user added is untrusted: back to the built-in one rather than fail.
            _skins.ReportBroken(skin, ex);
            return;
        }

        _surface.Present();
        _drawn = view;
    }

    private EqualizerView BuildView()
    {
        var settings = _equalizer.Current;
        return new EqualizerView
        {
            On = settings.Enabled,
            Bands = EqualizerSliders.FromBands(settings.GainsDb),
            Preamp = settings.PreampDb,
            // Sliders stay pressed wherever the pointer goes; buttons only while it is over them.
            Pressed = _pressed is EqualizerControl.Band or EqualizerControl.ShadeVolume or EqualizerControl.ShadeBalance || _pressedOver ? _pressed : EqualizerControl.None,
            PressedBand = _pressed == EqualizerControl.Band ? _pressedBand : -1,
            WindowActive = _windowActive,
            Shaded = Shaded,
            Volume = _dragVolume ?? _player.State.Volume,
            Balance = 0,
        };
    }

    // The pointer

    private (EqualizerControl Control, int Band) HitTest(Point position)
    {
        var (x, y) = _surface.SkinPoint(position);
        return _skins.IsReady ? EqualizerLayout.HitTest((int)Math.Floor(x), (int)Math.Floor(y), Shaded) : (EqualizerControl.None, -1);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view);
        var (control, band) = HitTest(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ShowPresetsMenu(e.GetCurrentPoint(this).Position);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || _pressed != EqualizerControl.None || _moving)
        {
            return;
        }

        e.Handled = true;
        _pointerId = e.Pointer.PointerId;
        CapturePointer(e.Pointer);
        if (control is EqualizerControl.TitleBar or EqualizerControl.None or EqualizerControl.Graph or EqualizerControl.Preamp)
        {
            // The title bar, and whatever does nothing, moves the mini player.
            _moving = true;
            _mini.BeginMove();
            return;
        }

        _pressed = control;
        _pressedBand = band;
        _pressedOver = true;
        var (x, y) = _surface.SkinPoint(point.Position);
        if (control == EqualizerControl.Band)
        {
            // Grabbing the thumb keeps it under the pointer where it was taken; pressing the track brings its middle there.
            var gain = GainOf(band);
            var top = EqualizerLayout.ThumbTop(gain);
            _grabOffset = y >= top && y < top + EqualizerLayout.ThumbSize ? y - top : EqualizerLayout.ThumbSize / 2;
            DragBand(y);
        }
        else if (control == EqualizerControl.ShadeVolume)
        {
            _grabOffset = 1;
            DragVolume(x);
        }

        Invalidate();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_view).Position;
        if (_moving && e.Pointer.PointerId == _pointerId)
        {
            e.Handled = true;
            _mini.Move();
            return;
        }

        if (_pressed != EqualizerControl.None && e.Pointer.PointerId == _pointerId)
        {
            e.Handled = true;
            var (x, y) = _surface.SkinPoint(position);
            switch (_pressed)
            {
                case EqualizerControl.Band:
                    DragBand(y);
                    break;
                case EqualizerControl.ShadeVolume:
                    DragVolume(x);
                    break;
                default:
                    _pressedOver = HitTest(position).Control == _pressed;
                    break;
            }

            Invalidate();
            return;
        }

        var hovered = HitTest(position);
        if (hovered != _hovered)
        {
            _hovered = hovered;
            ToolTipService.SetToolTip(this, TipFor(hovered.Control, hovered.Band));
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        if (_moving)
        {
            e.Handled = true;
            _moving = false;
            ReleasePointerCapture(e.Pointer);
            _mini.EndMove();
            return;
        }

        if (_pressed == EqualizerControl.None)
        {
            return;
        }

        e.Handled = true;
        var position = e.GetCurrentPoint(this).Position;
        var control = _pressed;
        var over = HitTest(e.GetCurrentPoint(_view).Position).Control == control;

        // Cleared before letting go, so the capture-lost event that follows has nothing to undo.
        _pressed = EqualizerControl.None;
        _pressedBand = -1;
        _dragVolume = null;
        _sentVolume = -1;
        ReleasePointerCapture(e.Pointer);
        if (over)
        {
            Act(control, position);
        }

        Invalidate();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        if (_moving)
        {
            _moving = false;
            _mini.EndMove();
        }

        if (_pressed != EqualizerControl.None)
        {
            _pressed = EqualizerControl.None;
            _pressedBand = -1;
            _dragVolume = null;
            _sentVolume = -1;
            Invalidate();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_hovered.Control != EqualizerControl.None)
        {
            _hovered = (EqualizerControl.None, -1);
            ToolTipService.SetToolTip(this, null);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var (control, band) = HitTest(e.GetPosition(_view));
        if (control == EqualizerControl.TitleBar)
        {
            // Double-clicking the title bar rolls the window up, and back.
            e.Handled = true;
            _skins.MiniEqualizerShaded = !_skins.MiniEqualizerShaded;
        }
        else if (control == EqualizerControl.Band)
        {
            // As in Settings, a double-click puts a band back to 0 dB.
            e.Handled = true;
            SetGain(band, 0);
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Over a slider, the wheel moves it a step a notch (upwards louder).
        var point = e.GetCurrentPoint(_view);
        var (control, band) = HitTest(point.Position);
        var delta = point.Properties.IsHorizontalMouseWheel ? 0 : point.Properties.MouseWheelDelta;
        if (control != EqualizerControl.Band || delta == 0)
        {
            return;
        }

        e.Handled = true;
        SetGain(band, GainOf(band) + (Math.Sign(delta) * GainStep));
    }

    private double GainOf(int slider)
    {
        var gains = _equalizer.Current.GainsDb;
        var band = EqualizerSliders.BandFor(slider);
        return band < gains.Count ? gains[band] : 0;
    }

    private void DragBand(double y)
    {
        if (_pressedBand >= 0)
        {
            SetGain(_pressedBand, EqualizerLayout.GainAt(y - _grabOffset));
        }
    }

    private void SetGain(int slider, double gain)
    {
        var rounded = Math.Clamp(Math.Round(gain / GainStep) * GainStep, -EqualizerSettings.MaxGainDb, EqualizerSettings.MaxGainDb);
        _equalizer.Set(_equalizer.Current.WithGain(EqualizerSliders.BandFor(slider), rounded));
        Invalidate();
    }

    private void DragVolume(double x)
    {
        // Volume follows the drag (the player sends only the newest value).
        var value = EqualizerLayout.ShadeVolumeAt(x - _grabOffset);
        _dragVolume = value;
        var percent = (int)Math.Round(value * 100);
        if (percent != _sentVolume)
        {
            _sentVolume = percent;
            _ = _player.SetVolumeAsync(value);
        }
    }

    private void Act(EqualizerControl control, Point at)
    {
        switch (control)
        {
            case EqualizerControl.On:
                _equalizer.Set(_equalizer.Current with { Enabled = !_equalizer.Current.Enabled });
                break;
            case EqualizerControl.Auto:
                // Winamp's AUTO loaded presets by itself; here it lays the bands flat in one click.
                _equalizer.Set(EqualizerSettings.Flat with { Enabled = _equalizer.Current.Enabled });
                break;
            case EqualizerControl.Presets:
                ShowPresetsMenu(at);
                break;
            case EqualizerControl.Shade:
                _skins.MiniEqualizerShaded = !_skins.MiniEqualizerShaded;
                break;
            case EqualizerControl.Close:
                _skins.MiniEqualizer = false;
                break;
            default:
                break;
        }

        Invalidate();
    }

    private string? TipFor(EqualizerControl control, int band) => control switch
    {
        EqualizerControl.On => "Equalizer on or off",
        EqualizerControl.Auto => "Flat: every band to 0 dB",
        EqualizerControl.Presets => "Presets",
        EqualizerControl.Preamp => "Preamp: set by itself, so boosts can't clip",
        EqualizerControl.Graph => "The curve the bands make",
        EqualizerControl.Band => $"{EqualizerLayout.BandLabels[band]} Hz: {EqualizerPanel.FormatGain(GainOf(band))} (Spotify's {EqualizerSettings.Bands[EqualizerSliders.BandFor(band)].Label} band)",
        EqualizerControl.Shade => Shaded ? "Full size" : "Shade: just the title strip",
        EqualizerControl.Close => "Close the equalizer",
        EqualizerControl.ShadeVolume => "Volume",
        EqualizerControl.ShadeBalance => "Balance (always in the middle)",
        _ => null,
    };

    private void ShowPresetsMenu(Point at)
    {
        var menu = new MenuFlyout();
        var current = _equalizer.Current;
        var on = new ToggleMenuFlyoutItem { Text = "Equalizer on", IsChecked = current.Enabled };
        on.Click += (_, _) => _equalizer.Set(_equalizer.Current with { Enabled = !_equalizer.Current.Enabled });
        menu.Items.Add(on);
        menu.Items.Add(new MenuFlyoutSeparator());

        var match = current.Preset;
        foreach (var preset in EqualizerPresets.All)
        {
            var chosen = preset;
            var item = new RadioMenuFlyoutItem { Text = preset.Name, GroupName = "classic-eq-presets", IsChecked = ReferenceEquals(preset, match) };
            item.Click += (_, _) => _equalizer.Set(_equalizer.Current.WithPreset(chosen));
            menu.Items.Add(item);
        }

        if (_equalizer.CanRestartSpotify)
        {
            // Spotify reads its equalizer when it starts; local files have it already.
            menu.Items.Add(new MenuFlyoutSeparator());
            var restart = new MenuFlyoutItem { Text = "Restart Spotify to hear it", IsEnabled = !_equalizer.IsRestarting };
            restart.Click += (_, _) => _ = _equalizer.RestartSpotifyAsync();
            menu.Items.Add(restart);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var settings = new MenuFlyoutItem { Text = "Equalizer settings" };
        settings.Click += (_, _) =>
        {
            _mini.Leave();
            App.MainWindow?.OpenSettings(SettingsSection.Equalizer);
        };
        menu.Items.Add(settings);
        menu.ShowAt(this, new FlyoutShowOptions { Position = at });
    }
}

using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Services;
using Resonate.Themes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Resonate.App.Themes;

/// <summary>
/// Shows the look in use. Colours go into the shared brushes of
/// Themes/Tokens.xaml (changing their Color reaches every control at once,
/// and lets colours slide smoothly from one look to the next). Corners,
/// outlines, spacing and fonts go into the theme dictionary, which the window
/// re-reads by switching its theme away and back. Switching looks plays the
/// transition the user picked (see <see cref="ThemeTransitions"/>).
/// </summary>
public sealed class ThemeService
{
    private static readonly TimeSpan MorphDuration = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan QuickMorphDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan AccentMorphDuration = TimeSpan.FromMilliseconds(700);

    // Heights of the themed buttons (Tokens.xaml); a round button's corner is
    // half its height, since a larger corner draws an oval instead of a pill.
    private const double ButtonHeight = 36;
    private const double PlayButtonSize = 40;

    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly List<ColorSlot> _slots = [];
    private readonly UISettings _systemSettings = new();
    private ResourceDictionary? _tokens;
    private LinearGradientBrush? _backgroundGradient;
    private Application? _application;
    private Window? _window;
    private FrameworkElement? _root;
    private ThemeTransitions? _transitions;
    private ThemeDefinition? _applied;
    private bool _shownIsLight;
    private ThemeColor? _artworkAccent;
    private long _morphStart;
    private TimeSpan _morphDuration;
    private bool _morphing;
    private int _version;
    private Func<ThemeDefinition, ThemeDefinition>? _pendingEdit;
    private DispatcherQueueTimer? _saveTimer;

    public ThemeService(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        Library = new ThemeLibrary(settings.ThemeId, settings.CustomLook, settings.SavedLooks);
        Palette = ThemePalette.From(Current);
    }

    /// <summary>Raised after the look in use, or its colours, change.</summary>
    public event EventHandler? Changed;

    /// <summary>The presets, the saved looks and which one is in use.</summary>
    public ThemeLibrary Library { get; }

    public ThemeDefinition Current => Library.Active;

    /// <summary>The colours and sizes of the look in use (with the cover's accent when it follows the cover).</summary>
    public ThemePalette Palette { get; private set; }

    /// <summary>How switching looks animates.</summary>
    public ThemeTransitionKind Transition
    {
        get => _settings.ThemeTransition;
        set
        {
            _settings.ThemeTransition = value;
            SaveSoon();
        }
    }

    /// <summary>Whether Windows' "Animation effects" setting allows motion.</summary>
    public bool AnimationsEnabled => _systemSettings.AnimationsEnabled;

    /// <summary>
    /// Call once at start-up, before any window exists, so everything created
    /// afterwards starts in the right look.
    /// </summary>
    public void Initialize(Application application)
    {
        _application = application;
        _tokens = application.Resources.MergedDictionaries.FirstOrDefault(d => d.ContainsKey("ResonateAccentBrush"));
        if (_tokens is not null)
        {
            CreateColorSlots(_tokens);
        }

        ApplyNow(Current, Palette);
    }

    /// <summary>
    /// Connects the window: <paramref name="root"/> carries the light or dark
    /// theme, <paramref name="capture"/> is what transitions take a picture
    /// of, and <paramref name="overlay"/> (above it) is where they play.
    /// </summary>
    public void AttachWindow(Window window, FrameworkElement root, FrameworkElement capture, Panel overlay)
    {
        _window = window;
        _root = root;
        _transitions = new ThemeTransitions(capture, overlay);
        _applied = null;
        ApplyNow(Current, Palette);
    }

    /// <summary>Switches to a preset or saved look, with the chosen transition starting at <paramref name="origin"/>.</summary>
    public void Select(string id, Point? origin = null, ThemeTransitionKind? transition = null)
    {
        if (id == Library.ActiveId && Current == _applied)
        {
            return;
        }

        Library.Select(id);
        Persist();
        _ = ShowAsync(transition ?? Transition, origin, MorphDuration);
    }

    /// <summary>
    /// Changes the look in use (a preset is copied into a custom look first).
    /// With <paramref name="smooth"/> the change animates briefly; without it,
    /// changes are gathered and applied once per frame, for sliders and
    /// colour pickers that change continuously.
    /// </summary>
    public void Edit(Func<ThemeDefinition, ThemeDefinition> change, bool smooth = false)
    {
        if (smooth)
        {
            FlushPendingEdit();
            Library.Edit(change);
            Persist();
            _ = ShowAsync(ThemeTransitionKind.Morph, null, QuickMorphDuration);
            return;
        }

        var queued = _pendingEdit is not null;
        var earlier = _pendingEdit;
        _pendingEdit = earlier is null ? change : look => change(earlier(look));
        if (!queued)
        {
            DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, FlushPendingEdit);
        }
    }

    public ThemeDefinition SaveAs(string name)
    {
        FlushPendingEdit();
        var saved = Library.SaveAs(name);
        Persist();
        ApplyNow(Current, PaletteFor(Current));
        return saved;
    }

    /// <summary>Adds a look (for example one pasted as text) and switches to it.</summary>
    public void Add(ThemeDefinition look, Point? origin = null)
    {
        Library.Add(look);
        Persist();
        _ = ShowAsync(Transition, origin, MorphDuration);
    }

    public void Rename(string id, string name)
    {
        Library.Rename(id, name);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        var wasActive = Library.ActiveId == id;
        Library.Delete(id);
        Persist();
        if (wasActive)
        {
            _ = ShowAsync(Transition, null, MorphDuration);
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The playing song's accent colour (null without a colourful cover), for looks that follow the cover.</summary>
    public void SetArtworkAccent(ThemeColor? accent)
    {
        if (_artworkAccent == accent)
        {
            return;
        }

        _artworkAccent = accent;
        if (Current.AdaptiveAccent && _applied is not null)
        {
            Palette = PaletteFor(Current);
            ApplyThemeAccents(Palette);
            StartMorph(Palette, AccentMorphDuration);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A token's brush, for code that builds visuals itself.</summary>
    public Brush GetBrush(string key) =>
        _tokens is not null && _tokens.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private ThemePalette PaletteFor(ThemeDefinition look) =>
        ThemePalette.From(look, look.AdaptiveAccent ? _artworkAccent : null);

    private void FlushPendingEdit()
    {
        if (_pendingEdit is not { } change)
        {
            return;
        }

        _pendingEdit = null;
        Library.Edit(change);
        Persist();
        _ = ShowAsync(ThemeTransitionKind.None, null, TimeSpan.Zero);
    }

    private async Task ShowAsync(ThemeTransitionKind kind, Point? origin, TimeSpan morphDuration)
    {
        var version = ++_version;
        var next = Current;
        var palette = PaletteFor(next);
        var structural = _applied is null || !_applied.HasSameStructure(next) || palette.IsLight != _shownIsLight;

        if (kind == ThemeTransitionKind.Random)
        {
            ThemeTransitionKind[] choices = [ThemeTransitionKind.Morph, ThemeTransitionKind.Ripple, ThemeTransitionKind.Split, ThemeTransitionKind.Blinds, ThemeTransitionKind.Wipe];
            kind = choices[Random.Shared.Next(choices.Length)];
        }

        if (_transitions is null || !AnimationsEnabled || morphDuration <= TimeSpan.Zero)
        {
            kind = ThemeTransitionKind.None;
        }

        switch (kind)
        {
            case ThemeTransitionKind.None:
                ApplyNow(next, palette);
                break;

            case ThemeTransitionKind.Morph:
                // Colours flow; shapes, fonts and light or dark cross-fade from a picture.
                var covered = structural && await _transitions!.CoverAsync();
                if (version != _version)
                {
                    // A newer switch that took no picture of its own is already
                    // showing; this picture would stay frozen over it.
                    if (covered)
                    {
                        _transitions!.Clear();
                    }

                    return;
                }

                ApplyStructure(next, palette);
                StartMorph(palette, morphDuration);
                Changed?.Invoke(this, EventArgs.Empty);
                if (covered)
                {
                    await _transitions!.RevealAsync(ThemeTransitionKind.Morph, origin, palette, morphDuration);
                }

                break;

            default:
                if (!await _transitions!.CoverAsync())
                {
                    // Nothing to picture (minimised), or a newer switch took over.
                    if (version == _version)
                    {
                        ApplyNow(next, palette);
                    }

                    break;
                }

                if (version != _version)
                {
                    _transitions.Clear();
                    return;
                }

                ApplyNow(next, palette);
                await _transitions.RevealAsync(kind, origin, palette, morphDuration);
                break;
        }
    }

    /// <summary>Everything at once, no animation.</summary>
    private void ApplyNow(ThemeDefinition look, ThemePalette palette)
    {
        StopMorph();
        ApplyStructure(look, palette);
        foreach (var slot in _slots)
        {
            slot.Land(slot.Pick(palette));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shapes, fonts, spacing, materials and light or dark.</summary>
    private void ApplyStructure(ThemeDefinition look, ThemePalette palette)
    {
        Palette = palette;
        var sameStructure = _applied is not null && _applied.HasSameStructure(look);
        var sameMode = _applied is not null && _shownIsLight == palette.IsLight;
        ApplyThemeAccents(palette);

        if (!sameStructure && _tokens is not null)
        {
            var floating = look.PlayerLayout == PlayerLayout.Floating;
            var gap = palette.PanelGap;
            var floatGap = Math.Max(gap, 8);
            foreach (var dictionary in _tokens.ThemeDictionaries.Values.OfType<ResourceDictionary>())
            {
                dictionary["ResonateCornerSmall"] = new CornerRadius(palette.CornerSmall);
                dictionary["ResonateCornerMedium"] = new CornerRadius(palette.CornerMedium);
                dictionary["ResonateCornerLarge"] = new CornerRadius(palette.CornerLarge);
                dictionary["ResonateCornerButton"] = new CornerRadius(Math.Min(palette.CornerButton, ButtonHeight / 2));
                dictionary["ResonatePlayButtonCorner"] = new CornerRadius(Math.Min(palette.CornerButton, PlayButtonSize / 2));
                dictionary["ControlCornerRadius"] = new CornerRadius(Math.Min(palette.CornerSmall, 8));
                dictionary["OverlayCornerRadius"] = new CornerRadius(Math.Min(palette.CornerMedium, 12));
                dictionary["ResonatePanelBorderThickness"] = new Thickness(palette.BorderWidth);
                dictionary["ResonatePlayButtonBorderThickness"] = new Thickness(palette.PlayButtonBorderWidth);
                dictionary["ResonatePanelGap"] = gap;
                dictionary["ResonateShellPadding"] = new Thickness(gap, 0, gap, gap);
                dictionary["ResonatePlayerMargin"] = floating ? new Thickness(floatGap, 0, floatGap, floatGap) : new Thickness(0);
                dictionary["ResonatePlayerBorderThickness"] = floating ? new Thickness(palette.BorderWidth) : new Thickness(0, palette.BorderWidth, 0, 0);
                dictionary["ResonatePlayerCorner"] = floating ? new CornerRadius(Math.Max(palette.CornerLarge, 4)) : new CornerRadius(0);
                dictionary["ResonateDisplayFont"] = new FontFamily(look.DisplayFont);
                dictionary["ResonateTextFont"] = new FontFamily(look.TextFont);
            }
        }

        if (_backgroundGradient is not null)
        {
            (_backgroundGradient.StartPoint, _backgroundGradient.EndPoint) = ThemeColorExtensions.GradientPoints(look.GradientAngle);
        }

        if (_window is not null && _applied?.Backdrop != look.Backdrop)
        {
            _window.SystemBackdrop = look.Backdrop switch
            {
                WindowBackdrop.Mica => new MicaBackdrop(),
                WindowBackdrop.Acrylic => new DesktopAcrylicBackdrop(),
                _ => null,
            };
        }

        if (_root is not null && (!sameStructure || !sameMode))
        {
            // Theme resources are read again when the theme changes, so switch
            // away and back: corners, fonts and built-in controls all update.
            var target = palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;
            _root.RequestedTheme = target == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
            _root.RequestedTheme = target;
        }

        _applied = look;
        _shownIsLight = palette.IsLight;
    }

    /// <summary>The accent colours Windows' own controls read when they are created or re-themed.</summary>
    private void ApplyThemeAccents(ThemePalette palette)
    {
        if (_application is null)
        {
            return;
        }

        var resources = _application.Resources;
        resources["SystemAccentColor"] = palette.Accent.ToColor();
        resources["SystemAccentColorLight1"] = palette.AccentHover.ToColor();
        resources["SystemAccentColorLight2"] = palette.Accent.ToColor();
        resources["SystemAccentColorLight3"] = palette.AccentHover.ToColor();
        resources["SystemAccentColorDark1"] = palette.AccentPressed.ToColor();
        resources["SystemAccentColorDark2"] = palette.AccentPressed.ToColor();
        resources["SystemAccentColorDark3"] = palette.AccentPressed.ToColor();
    }

    private void StartMorph(ThemePalette to, TimeSpan duration)
    {
        foreach (var slot in _slots)
        {
            slot.From = slot.Current;
            slot.To = slot.Pick(to);
        }

        if (duration <= TimeSpan.Zero || !AnimationsEnabled)
        {
            StopMorph();
            return;
        }

        _morphStart = Stopwatch.GetTimestamp();
        _morphDuration = duration;
        if (!_morphing)
        {
            _morphing = true;
            CompositionTarget.Rendering += OnMorphFrame;
        }
    }

    private void OnMorphFrame(object? sender, object e)
    {
        var progress = Stopwatch.GetElapsedTime(_morphStart) / _morphDuration;
        if (progress >= 1)
        {
            StopMorph();
            return;
        }

        // Ease in and out, so colours leave gently and settle gently.
        var eased = progress < 0.5 ? 4 * progress * progress * progress : 1 - (Math.Pow((-2 * progress) + 2, 3) / 2);
        foreach (var slot in _slots)
        {
            slot.Show(slot.From.Mix(slot.To, eased));
        }
    }

    /// <summary>Ends a colour slide, landing on its final colours.</summary>
    private void StopMorph()
    {
        if (_morphing)
        {
            _morphing = false;
            CompositionTarget.Rendering -= OnMorphFrame;
            foreach (var slot in _slots)
            {
                slot.Show(slot.To);
            }
        }
    }

    /// <summary>Copies the library into the settings, and writes them shortly (sliders change a lot).</summary>
    private void Persist()
    {
        _settings.ThemeId = Library.ActiveId;
        _settings.CustomLook = Library.Custom;
        _settings.SavedLooks = [.. Library.Saved];
        SaveSoon();
    }

    private void SaveSoon()
    {
        if (_saveTimer is null)
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                _save();
                return;
            }

            _saveTimer = queue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => _save();
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void CreateColorSlots(ResourceDictionary tokens)
    {
        void Solid(string key, Func<ThemePalette, ThemeColor> pick)
        {
            if (tokens.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            {
                _slots.Add(new ColorSlot(pick, color => brush.Color = color));
            }
        }

        void Gradient(string key, Func<ThemePalette, ThemeColor> first, Func<ThemePalette, ThemeColor> second)
        {
            if (tokens.TryGetValue(key, out var value) && value is LinearGradientBrush brush && brush.GradientStops.Count == 2)
            {
                var (start, end) = (brush.GradientStops[0], brush.GradientStops[1]);
                _slots.Add(new ColorSlot(first, color => start.Color = color));
                _slots.Add(new ColorSlot(second, color => end.Color = color));
            }
        }

        Solid("ResonateBackgroundBrush", p => p.Background);
        Solid("ResonateBackdropTintBrush", p => p.BackdropTint);
        Gradient("ResonateBackgroundGradientBrush", p => p.Background, p => p.Background2);
        Solid("ResonateSidebarBrush", p => p.Sidebar);
        Solid("ResonateSurfaceBrush", p => p.Surface);
        Solid("ResonatePlayerBrush", p => p.Player);
        Solid("ResonateSurfaceHoverBrush", p => p.Hover);
        Solid("ResonateSurfacePressedBrush", p => p.Pressed);
        Solid("ResonateControlBrush", p => p.Control);
        Solid("ResonateBorderBrush", p => p.Border);
        Solid("ResonateShadowBrush", p => p.PanelShadow.IsVisible ? p.PanelShadow.Color.Opaque : p.TextPrimary);
        Solid("ResonateTextPrimaryBrush", p => p.TextPrimary);
        Solid("ResonateTextSecondaryBrush", p => p.TextSecondary);
        Solid("ResonateTextTertiaryBrush", p => p.TextTertiary);
        Solid("ResonateAccentBrush", p => p.Accent);
        Solid("ResonateAccentHoverBrush", p => p.AccentHover);
        Solid("ResonateAccentPressedBrush", p => p.AccentPressed);
        Solid("ResonateOnAccentBrush", p => p.OnAccent);
        Solid("ResonateAccent2Brush", p => p.Accent2);
        Gradient("ResonateAccentGradientBrush", p => p.Accent, p => p.Accent2);
        Solid("ResonateTrackBrush", p => p.Track);
        Solid("ResonatePlayButtonBackgroundBrush", p => p.PlayButtonBackground);
        Solid("ResonatePlayButtonHoverBrush", p => p.PlayButtonHover);
        Solid("ResonatePlayButtonForegroundBrush", p => p.PlayButtonForeground);
        Solid("ResonatePlayButtonBorderBrush", p => p.PlayButtonBorder);

        _backgroundGradient = tokens.TryGetValue("ResonateBackgroundGradientBrush", out var gradient) ? gradient as LinearGradientBrush : null;
    }

    /// <summary>One colour the theme sets: where it comes from in the palette, and the brush (or gradient stop) it goes to.</summary>
    private sealed class ColorSlot(Func<ThemePalette, ThemeColor> pick, Action<Color> set)
    {
        public Func<ThemePalette, ThemeColor> Pick { get; } = pick;

        public ThemeColor Current { get; private set; }

        public ThemeColor From { get; set; }

        public ThemeColor To { get; set; }

        /// <summary>Draws a colour on the way to <see cref="To"/>.</summary>
        public void Show(ThemeColor color)
        {
            Current = color;
            set(color.ToColor());
        }

        /// <summary>Draws a final colour.</summary>
        public void Land(ThemeColor color)
        {
            To = color;
            Show(color);
        }
    }
}

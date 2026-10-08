using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
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
    // When the playing song's colours change the accent; switching looks times itself (ThemeTransitionCatalog).
    private static readonly ThemeTransitionSpec AccentSlide = new(TimeSpan.FromMilliseconds(700), ThemeTransitionCatalog.Emphasized);

    // Heights of the themed buttons (Tokens.xaml); a round button's corner is
    // half its height, since a larger corner draws an oval instead of a pill.
    private const double ButtonHeight = 36;
    private const double PlayButtonSize = 40;
    private const double InputHeight = 40;

    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly List<ColorSlot> _slots = [];
    private readonly UISettings _systemSettings = new();
    private ResourceDictionary? _tokens;
    private LinearGradientBrush? _backgroundGradient;
    private Application? _application;
    private MainWindow? _window;
    private FrameworkElement? _root;
    private ThemeTransitions? _transitions;
    private ThemeDefinition? _applied;
    private bool _shownIsLight;
    private ThemeColor? _artworkAccent;
    private long _morphStart;
    private ThemeTransitionSpec _morphSpec;
    private bool _morphing;
    private Action<long>? _morphMoving;
    private TaskCompletionSource? _morphDone;
    private TaskCompletionSource<long> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;
    private Func<ThemeDefinition, ThemeDefinition>? _pendingEdit;
    private DispatcherQueueTimer? _saveTimer;

    public ThemeService(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        Library = new ThemeLibrary(settings.ThemeId, settings.CustomLook, settings.SavedLooks);
        Palette = ThemePalette.From(Current);
        _started.SetResult(0);
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

    /// <summary>The latest switch of looks: finishes when its animation has (for the speed test and the screenshot tour).</summary>
    internal Task TransitionTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Finishes when the latest switch first shows on screen: the Stopwatch
    /// time of the first frame drawn after it (or of the switch itself, when
    /// it shows at once).
    /// </summary>
    internal Task<long> TransitionStarted => _started.Task;

    /// <summary>The switching animation on screen right now, or null.</summary>
    internal ThemeTransitionKind? TransitionShowing => _transitions?.Showing;

    /// <summary>Why the last switching animation that failed could not play (the look then switched at once), or null.</summary>
    internal string? TransitionFailure => _transitions?.Failure;

    /// <summary>For the screenshot tour: switches animate even with Windows' animations off, as they may be on CI's machine.</summary>
    internal bool AnimateRegardless { get; set; }

    /// <summary>Whether a switch may animate now: Windows allows motion, and somebody can see the window.</summary>
    private bool MayAnimate => (AnimationsEnabled || AnimateRegardless) && _window is { IsShown: true };

    /// <summary>
    /// The user allows the now-playing cover to turn like a record while a
    /// song plays (in every look). Off until they switch it on.
    /// </summary>
    public bool SpinningCover
    {
        get => _settings.SpinningCover;
        set
        {
            if (_settings.SpinningCover != value)
            {
                _settings.SpinningCover = value;
                SaveSoon();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Looks with the song cover backdrop show the cover itself, blurred (on
    /// unless the user switches it off; such looks then show a soft wash of
    /// the cover's colours instead).
    /// </summary>
    public bool BlurredCoverBackground
    {
        get => _settings.BlurredCoverBackground;
        set
        {
            if (_settings.BlurredCoverBackground != value)
            {
                _settings.BlurredCoverBackground = value;
                SaveSoon();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// The library sidebar runs to the bottom of the window and the player
    /// sits under the page (and the queue) only. The user's own window
    /// arrangement, kept whatever look is in use; off until they switch it on.
    /// </summary>
    public bool SidebarFullHeight
    {
        get => _settings.SidebarFullHeight;
        set
        {
            if (_settings.SidebarFullHeight != value)
            {
                _settings.SidebarFullHeight = value;
                SaveSoon();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Whether the now-playing cover is drawn as a record (round, with a
    /// centre): in looks with the vinyl cover style, and in every look while
    /// the user lets covers spin.
    /// </summary>
    public bool CoverIsRecord => Current.Cover == CoverStyle.Vinyl || SpinningCover;

    /// <summary>
    /// Whether a now-playing cover may turn at all: only with the user's
    /// Spinning cover switch on and Windows' animations allowed. It turns
    /// only while a song plays.
    /// </summary>
    public bool CoverMaySpin => SpinningCover && AnimationsEnabled;

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
    /// Connects the window: <paramref name="host"/> carries the light or dark
    /// theme, and holds the layers where switching looks animates.
    /// </summary>
    internal void AttachWindow(MainWindow window, ThemeHost host)
    {
        _window = window;
        _root = host;
        _transitions = new ThemeTransitions(host);
        window.ShownChanged += OnWindowShownChanged;
        _applied = null;
        ApplyNow(Current, Palette);
    }

    /// <summary>For the screenshot tour: holds the switching animation on screen at <paramref name="progress"/> (0 to 1). False when none plays.</summary>
    internal bool FreezeTransition(double progress) => _transitions?.Freeze(progress) ?? false;

    /// <summary>Lets a held switching animation finish.</summary>
    internal void ResumeTransition() => _transitions?.Resume();

    /// <summary>Switches to a preset or saved look, with the chosen transition starting at <paramref name="origin"/>.</summary>
    public void Select(string id, Point? origin = null, ThemeTransitionKind? transition = null)
    {
        if (id == Library.ActiveId && Current == _applied)
        {
            return;
        }

        Library.Select(id);
        Persist();
        Switch(transition ?? Transition, origin);
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
            Switch(ThemeTransitionKind.Fade, null, ThemeTransitionCatalog.QuickEdit);
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
        Switch(Transition, origin);
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
            Switch(Transition, null);
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
            StartMorph(Palette, AccentSlide);
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
        Switch(ThemeTransitionKind.None, null);
    }

    /// <summary>Starts switching to the look in use; a newer switch takes over from this one.</summary>
    private void Switch(ThemeTransitionKind kind, Point? origin, ThemeTransitionSpec? spec = null)
    {
        _started.TrySetResult(Stopwatch.GetTimestamp());
        var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        _started = started;
        TransitionTask = ShowAsync(kind, origin, spec, time => started.TrySetResult(time));
    }

    private async Task ShowAsync(ThemeTransitionKind kind, Point? origin, ThemeTransitionSpec? quick, Action<long> moving)
    {
        var version = ++_version;
        var next = Current;
        var palette = PaletteFor(next);
        var previous = _applied;
        var structural = previous is null || !previous.HasSameStructure(next) || palette.IsLight != _shownIsLight;

        kind = ThemeTransitionCatalog.Resolve(kind, Random.Shared.Next);
        if (ThemeTransitionCatalog.RevealsLive(kind) && (IsSeeThrough(previous) || IsSeeThrough(next)))
        {
            // Mica and acrylic leave the window see-through, so the old look
            // would show through the new one as it grows; fade instead.
            kind = ThemeTransitionKind.Fade;
        }

        if (_transitions is null || kind == ThemeTransitionKind.None || !MayAnimate)
        {
            _transitions?.Clear();
            ApplyNow(next, palette);
            moving(Stopwatch.GetTimestamp());
            return;
        }

        var spec = quick ?? ThemeTransitionCatalog.Spec(kind);
        if (kind == ThemeTransitionKind.Morph && !structural)
        {
            // Only colours change: they flow, with no picture.
            ApplyStructure(next, palette);
            StartMorph(palette, spec, moving);
            Changed?.Invoke(this, EventArgs.Empty);
            await MorphDone;
            return;
        }

        if (!await _transitions.CoverAsync() || !MayAnimate)
        {
            // Nothing to picture, the window was hidden meanwhile, or a newer switch took over.
            if (version == _version)
            {
                _transitions.Clear();
                ApplyNow(next, palette);
                moving(Stopwatch.GetTimestamp());
            }

            return;
        }

        if (version != _version)
        {
            // A newer switch that needs no picture is already showing.
            return;
        }

        if (kind == ThemeTransitionKind.Morph)
        {
            // Colours flow; shapes, fonts and light or dark cross-fade from the picture.
            await _transitions.PlayAsync(kind, origin, palette, spec, () =>
            {
                ApplyStructure(next, palette);
                StartMorph(palette, spec, null);
                Changed?.Invoke(this, EventArgs.Empty);
            }, moving);
            if (version == _version)
            {
                await MorphDone;
            }
        }
        else
        {
            await _transitions.PlayAsync(kind, origin, palette, spec, () => ApplyNow(next, palette), moving);
        }
    }

    private static bool IsSeeThrough(ThemeDefinition? look) => look?.Backdrop is WindowBackdrop.Mica or WindowBackdrop.Acrylic;

    /// <summary>Nobody can see the window: a switch in progress ends at once rather than animate.</summary>
    private void OnWindowShownChanged(object? sender, EventArgs e)
    {
        if (_window is { IsShown: false })
        {
            _transitions?.Clear();
            StopMorph();
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
            var gap = palette.PanelGap;
            var layout = look.PlayerLayout;
            var playerMargin = PlayerPlacement.Margin(layout, gap);
            var playerOutline = PlayerPlacement.Outline(layout, palette.BorderWidth);
            var playerCorner = PlayerPlacement.Corner(layout, look.Buttons, palette.CornerLarge, PlayerPlacement.BarHeight);
            foreach (var dictionary in _tokens.ThemeDictionaries.Values.OfType<ResourceDictionary>())
            {
                dictionary["ResonateCornerSmall"] = new CornerRadius(palette.CornerSmall);
                dictionary["ResonateCornerMedium"] = new CornerRadius(palette.CornerMedium);
                dictionary["ResonateCornerLarge"] = new CornerRadius(palette.CornerLarge);
                dictionary["ResonateCornerButton"] = new CornerRadius(Math.Min(palette.CornerButton, ButtonHeight / 2));
                dictionary["ResonatePlayButtonCorner"] = new CornerRadius(Math.Min(palette.CornerButton, PlayButtonSize / 2));
                dictionary["ControlCornerRadius"] = new CornerRadius(Math.Min(palette.CornerSmall, 8));
                dictionary["OverlayCornerRadius"] = new CornerRadius(Math.Min(palette.CornerMedium, 12));
                dictionary["ListViewItemCornerRadius"] = new CornerRadius(Math.Min(palette.CornerMedium, 8));
                dictionary["GridViewItemCornerRadius"] = new CornerRadius(Math.Min(palette.CornerLarge, 14));
                dictionary["ResonateCornerInput"] = new CornerRadius(Math.Min(palette.CornerButton, InputHeight / 2));
                dictionary["ResonatePanelBorderThickness"] = new Thickness(palette.BorderWidth);
                dictionary["ResonatePlayButtonBorderThickness"] = new Thickness(palette.PlayButtonBorderWidth);
                dictionary["ResonatePanelGap"] = gap;
                dictionary["ResonateShellPadding"] = new Thickness(gap, 0, gap, gap);
                dictionary["ResonatePlayerMargin"] = playerMargin.ToThickness();
                dictionary["ResonatePlayerBorderThickness"] = playerOutline.ToThickness();
                dictionary["ResonatePlayerCorner"] = new CornerRadius(playerCorner);
                dictionary["ResonatePlayerMaxWidth"] = PlayerPlacement.MaxWidth(layout);
                // Fonts that come with Resonate load from its own folder (see BundledFonts).
                dictionary["ResonateDisplayFont"] = new FontFamily(BundledFonts.Resolve(look.DisplayFont));
                dictionary["ResonateTextFont"] = new FontFamily(BundledFonts.Resolve(look.TextFont));
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

    /// <summary>
    /// Slides every colour to <paramref name="to"/>. <paramref name="moving"/>
    /// gets the time of the first frame that shows the colours moving.
    /// </summary>
    private void StartMorph(ThemePalette to, ThemeTransitionSpec spec, Action<long>? moving = null)
    {
        foreach (var slot in _slots)
        {
            slot.From = slot.Current;
            slot.To = slot.Pick(to);
        }

        _morphMoving?.Invoke(Stopwatch.GetTimestamp());
        _morphMoving = moving;
        if (spec.Duration <= TimeSpan.Zero || !MayAnimate)
        {
            StopMorph();
            return;
        }

        // The clock starts with the first frame, after the work of switching,
        // so the slide never begins part of the way along.
        _morphStart = 0;
        _morphSpec = spec;
        _morphDone ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_morphing)
        {
            _morphing = true;
            CompositionTarget.Rendering += OnMorphFrame;
        }
    }

    /// <summary>Finishes when the colour slide in progress (if any) has landed.</summary>
    private Task MorphDone => _morphDone?.Task ?? Task.CompletedTask;

    private void OnMorphFrame(object? sender, object e)
    {
        var now = Stopwatch.GetTimestamp();
        if (_morphStart == 0)
        {
            _morphStart = now;
            return;
        }

        var progress = Stopwatch.GetElapsedTime(_morphStart, now) / _morphSpec.Duration;
        if (progress >= 1)
        {
            StopMorph();
            return;
        }

        var moving = _morphMoving;
        _morphMoving = null;
        moving?.Invoke(now);

        // The same curve as the compositor's part of the switch, so they move as one.
        var eased = _morphSpec.Curve.Evaluate(progress);
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

        var moving = _morphMoving;
        _morphMoving = null;
        moving?.Invoke(Stopwatch.GetTimestamp());
        var done = _morphDone;
        _morphDone = null;
        done?.TrySetResult();
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
        Solid("ResonateAccentSoftBrush", p => p.AccentSoft);
        Gradient("ResonateHeroGradientBrush", p => p.HeroTint(p.Accent), p => p.HeroTint(p.Accent).WithAlpha(0));
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

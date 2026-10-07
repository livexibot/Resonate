using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Resonate.App.Themes;

/// <summary>
/// Applies a <see cref="ThemePreset"/> by changing the colour of the shared
/// brushes in Themes/Tokens.xaml. Every control that uses a token updates at
/// once, so switching looks is instant and needs no reload.
/// </summary>
public sealed class ThemeService
{
    private static readonly (string Key, Func<ThemePreset, Color> Pick)[] BrushTokens =
    [
        ("ResonateBackgroundBrush", p => p.Background),
        ("ResonateSidebarBrush", p => p.Sidebar),
        ("ResonateSurfaceBrush", p => p.Surface),
        ("ResonateSurfaceHoverBrush", p => p.SurfaceHover),
        ("ResonateSurfacePressedBrush", p => p.SurfacePressed),
        ("ResonateBorderBrush", p => p.Border),
        ("ResonateTextPrimaryBrush", p => p.TextPrimary),
        ("ResonateTextSecondaryBrush", p => p.TextSecondary),
        ("ResonateTextTertiaryBrush", p => p.TextTertiary),
        ("ResonateAccentBrush", p => p.Accent),
        ("ResonateAccentHoverBrush", p => p.AccentHover),
        ("ResonateAccentPressedBrush", p => p.AccentPressed),
        ("ResonateOnAccentBrush", p => p.OnAccent),
    ];

    private ResourceDictionary? _tokens;
    private FrameworkElement? _root;

    public ThemePreset Current { get; private set; } = ThemePreset.Midnight;

    /// <summary>
    /// Call once at start-up, before any window exists, so the built-in
    /// controls also pick up the accent colour.
    /// </summary>
    public void Initialize(Application application, ThemePreset preset)
    {
        _tokens = application.Resources.MergedDictionaries.FirstOrDefault(d => d.ContainsKey("ResonateAccentBrush"));

        // Built-in controls (text boxes, toggles, focus rings) read the system
        // accent colours when they are first created.
        var resources = application.Resources;
        resources["SystemAccentColor"] = preset.Accent;
        resources["SystemAccentColorLight1"] = preset.AccentHover;
        resources["SystemAccentColorLight2"] = preset.Accent;
        resources["SystemAccentColorLight3"] = preset.AccentHover;
        resources["SystemAccentColorDark1"] = preset.AccentPressed;
        resources["SystemAccentColorDark2"] = preset.AccentPressed;
        resources["SystemAccentColorDark3"] = preset.AccentPressed;

        Apply(preset);
    }

    /// <summary>The element whose light or dark mode follows the theme (the window's root).</summary>
    public void AttachRoot(FrameworkElement root)
    {
        _root = root;
        Apply(Current);
    }

    public void Apply(ThemePreset preset)
    {
        Current = preset;
        if (_tokens is not null)
        {
            foreach (var (key, pick) in BrushTokens)
            {
                if (_tokens.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                {
                    brush.Color = pick(preset);
                }
            }
        }

        if (_root is not null)
        {
            _root.RequestedTheme = preset.Mode == ThemeMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        }
    }

    /// <summary>A token's brush, for code that builds visuals itself.</summary>
    public Brush GetBrush(string key) =>
        _tokens is not null && _tokens.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}

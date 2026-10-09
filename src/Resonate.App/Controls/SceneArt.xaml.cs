using Microsoft.UI.Xaml.Controls;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The scenery of a special look (<see cref="ThemeDefinition.Scene"/>),
/// behind the panels: the Japan night or the snowy night. Each scene's
/// shapes are made only while it shows (<c>x:Load</c>) and let go of when
/// another look takes over. Its paths are written by
/// <c>tools/scenes/build_scene_art.py</c>. The weather over the window is
/// <see cref="SceneWeatherLayer"/>.
/// </summary>
public sealed partial class SceneArt : UserControl
{
    private readonly ThemeService _theme = App.Services.Theme;
    private ThemeScene _shown;

    public SceneArt()
    {
        InitializeComponent();

        // Only while in the window, so the theme never keeps it alive.
        Loaded += (_, _) =>
        {
            _theme.Changed -= OnThemeChanged;
            _theme.Changed += OnThemeChanged;
            Show();
        };
        Unloaded += (_, _) => _theme.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        var scene = _theme.Current.Scene;
        if (scene == _shown)
        {
            return;
        }

        if (JapanArt is not null && scene != ThemeScene.Japan)
        {
            UnloadObject(JapanArt);
        }

        if (SnowArt is not null && scene != ThemeScene.Snow)
        {
            UnloadObject(SnowArt);
        }

        _shown = scene;
        if (scene == ThemeScene.Japan)
        {
            FindName(nameof(JapanArt));
        }
        else if (scene == ThemeScene.Snow)
        {
            FindName(nameof(SnowArt));
        }
    }
}

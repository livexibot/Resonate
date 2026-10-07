using Microsoft.UI.Xaml;
using Resonate.App.Services;
using Resonate.App.Themes;

namespace Resonate.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Set in <see cref="OnLaunched"/>, before any window or page exists.</summary>
    public static AppServices Services { get; private set; } = null!;

    public static MainWindow? MainWindow => (Current as App)?._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = StartupOptions.Current.Demo ? AppServices.CreateDemo() : AppServices.Create();
        Services.Theme.Initialize(this, ThemePreset.ById(Services.Settings.ThemeId));

        _window = new MainWindow(Services);
        _window.Activate();
    }
}

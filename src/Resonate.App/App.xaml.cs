using Microsoft.UI.Xaml;
using Resonate.App.Services;

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
        if (StartupOptions.Current.PerformanceFolder is not null)
        {
            Demo.DemoWebApi.LikedCount = PerformanceTour.LikedSongs;
        }

        Services = StartupOptions.Current.Demo ? AppServices.CreateDemo() : AppServices.Create();
        Services.Theme.Initialize(this);

        _window = new MainWindow(Services);
        _window.Activate();
    }
}

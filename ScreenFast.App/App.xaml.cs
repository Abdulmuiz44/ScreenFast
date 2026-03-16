using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ScreenFast.App.Services;

namespace ScreenFast.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var preferencesService = Services.GetRequiredService<IAppPreferencesService>();
        preferencesService.InitializeAsync().GetAwaiter().GetResult();

        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();

        Services.GetRequiredService<IDesktopShellService>().ApplyStartupBehavior();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddScreenFast();
        return services.BuildServiceProvider();
    }
}

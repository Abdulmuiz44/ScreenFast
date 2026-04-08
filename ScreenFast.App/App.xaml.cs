using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ScreenFast.App.Services;
using ScreenFast.Core.Interfaces;

namespace ScreenFast.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private bool _servicesDisposed;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public static IServiceProvider Services { get; private set; } = default!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logger = Services.GetRequiredService<IScreenFastLogService>();
        logger.Info("app.startup", "ScreenFast is starting.");

        try
        {
            var preferencesService = Services.GetRequiredService<IAppPreferencesService>();
            logger.Info("app.startup_step", "Initializing preferences.");
            await preferencesService.InitializeAsync();

            var recoveryService = Services.GetRequiredService<IRecoveryService>();
            logger.Info("app.startup_step", "Initializing recovery.");
            await recoveryService.InitializeAsync();

            logger.Info("app.startup_step", "Resolving main window.");
            _window = Services.GetRequiredService<MainWindow>();
            logger.Info("app.startup_step", "Activating main window.");
            _window.Activate();

            logger.Info("app.startup_step", "Applying desktop shell startup behavior.");
            Services.GetRequiredService<IDesktopShellService>().ApplyStartupBehavior();
            logger.Info("app.startup_complete", "ScreenFast startup completed successfully.");
        }
        catch (Exception ex)
        {
            logger.Error(
                "app.startup_failed",
                "ScreenFast failed during startup.",
                new Dictionary<string, object?>
                {
                    ["error"] = ex.Message,
                    ["exception"] = ex.ToString()
                });
            DisposeServices();
            throw;
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddScreenFast();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Services.GetRequiredService<IScreenFastLogService>()
                .Error("app.unhandled_exception", "ScreenFast encountered an unhandled exception.", new Dictionary<string, object?> { ["error"] = e.Exception.Message });
        }
        catch
        {
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        DisposeServices();
    }

    private void DisposeServices()
    {
        if (_servicesDisposed)
        {
            return;
        }

        _servicesDisposed = true;

        try
        {
            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
        }
    }
}


using Microsoft.Extensions.DependencyInjection;
using ScreenFast.App.ViewModels;
using ScreenFast.Audio.Services;
using ScreenFast.Capture.Services;
using ScreenFast.Core.Interfaces;
using ScreenFast.Encoding.Services;
using ScreenFast.Infrastructure.Services;

namespace ScreenFast.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScreenFast(this IServiceCollection services)
    {
        services.AddSingleton<Direct3D11DeviceProvider>();
        services.AddSingleton<GraphicsCaptureSourceResolver>();
        services.AddSingleton<ICaptureSourcePickerService, WindowsGraphicsCaptureSourcePickerService>();
        services.AddSingleton<ICaptureItemResolver, GraphicsCaptureItemResolver>();
        services.AddSingleton<ICaptureSessionFactory, GraphicsCaptureSessionFactory>();
        services.AddSingleton<IOutputFolderPickerService, OutputFolderPickerService>();
        services.AddSingleton<IRecordingEncoderService, MediaFoundationRecordingEncoderService>();
        services.AddSingleton<ISystemAudioCaptureService, WasapiLoopbackCaptureService>();
        services.AddSingleton<IMicrophoneCaptureService, MicrophoneCaptureService>();
        services.AddSingleton<IRecorderOrchestrator, RecorderOrchestrator>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}

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
        services.AddSingleton<GraphicsCaptureSourceResolver>();
        services.AddSingleton<ICaptureSourcePickerService, WindowsGraphicsCaptureSourcePickerService>();
        services.AddSingleton<IOutputFolderPickerService, StubOutputFolderPickerService>();
        services.AddSingleton<IRecordingEncoderService, StubRecordingEncoderService>();
        services.AddSingleton<ISystemAudioCaptureService, NoOpSystemAudioCaptureService>();
        services.AddSingleton<IMicrophoneCaptureService, NoOpMicrophoneCaptureService>();
        services.AddSingleton<IRecorderOrchestrator, RecorderOrchestrator>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}

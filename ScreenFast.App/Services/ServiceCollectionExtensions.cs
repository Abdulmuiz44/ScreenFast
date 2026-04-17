using Microsoft.Extensions.DependencyInjection;
using ScreenFast.App;
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
        services.AddSingleton<IScreenFastLogService, FileScreenFastLogService>();
        services.AddSingleton<Direct3D11DeviceProvider>();
        services.AddSingleton<GraphicsCaptureSourceResolver>();
        services.AddSingleton<ICaptureSourcePickerService, WindowsGraphicsCaptureSourcePickerService>();
        services.AddSingleton<ICaptureItemResolver, GraphicsCaptureItemResolver>();
        services.AddSingleton<ICaptureSessionFactory, GraphicsCaptureSessionFactory>();
        services.AddSingleton<IOutputFolderPickerService, OutputFolderPickerService>();
        services.AddSingleton<IRecordingFileNameService, RecordingFileNameService>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IRecoveryStateStore, JsonRecoveryStateStore>();
        services.AddSingleton<IRecoveryService, RecoveryService>();
        services.AddSingleton<IRecordingHistoryService, RecordingHistoryService>();
        services.AddSingleton<IRecordingPreflightValidator, RecordingPreflightValidator>();
        services.AddSingleton<IRecordingTelemetryCaptureService, WindowsCursorTelemetryCaptureService>();
        services.AddSingleton<IRecordingMetadataSidecarService, RecordingMetadataSidecarService>();
        services.AddSingleton<IRecordingMetadataReader, RecordingMetadataReader>();
        services.AddSingleton<IRecordingEncoderService, MediaFoundationRecordingEncoderService>();
        services.AddSingleton<ISystemAudioCaptureService, WasapiLoopbackCaptureService>();
        services.AddSingleton<IMicrophoneCaptureService, MicrophoneCaptureService>();
        services.AddSingleton<IRecorderOrchestrator, RecorderOrchestrator>();
        services.AddSingleton<IAppPreferencesService, AppPreferencesService>();
        services.AddSingleton<IFileLauncherService, FileLauncherService>();
        services.AddSingleton<IDesktopShellService, DesktopShellService>();
        services.AddSingleton<IRecordingIndicatorOverlayService, RecordingIndicatorOverlayService>();
        services.AddSingleton<IDiagnosticsExportService, DiagnosticsExportService>();
        services.AddSingleton<IAppSmokeCheckService, AppSmokeCheckService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}

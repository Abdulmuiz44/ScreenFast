using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using ScreenFast.App.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using Windows.Graphics.Capture;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace ScreenFast.App.Services;

public sealed class AppSmokeCheckService : IAppSmokeCheckService
{
    private readonly IScreenFastLogService _logService;

    public AppSmokeCheckService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public SmokeCheckReport? CurrentReport { get; private set; }

    public Task<SmokeCheckReport> RunAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentReport is not null)
        {
            return Task.FromResult(CurrentReport);
        }

        var items = new List<SmokeCheckItem>
        {
            CheckWritableLocation("App data", ScreenFastPaths.RootFolderPath, "ScreenFast can write to its local app-data folder."),
            CheckWritableLocation("Logs", ScreenFastPaths.LogsFolderPath, "ScreenFast can write rolling log files."),
            CheckFolderPicker(ownerWindowHandle),
            CheckDiagnosticsWorkspace(),
            CheckTraySupport(),
            CheckHotkeySupport(ownerWindowHandle),
            CheckOverlaySupport(ownerWindowHandle),
            CheckCaptureSupport(),
            CheckEncodingSupport(),
            CheckRecoveryStorage()
        };

        var report = new SmokeCheckReport(DateTimeOffset.UtcNow, items);
        CurrentReport = report;
        LogReport(report);
        return Task.FromResult(report);
    }

    private SmokeCheckItem CheckWritableLocation(string name, string folderPath, string okMessage)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            var probePath = Path.Combine(folderPath, $"screenfast-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return new SmokeCheckItem(name, SmokeCheckSeverity.Ok, okMessage);
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem(name, SmokeCheckSeverity.Error, $"ScreenFast could not write to {folderPath}: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckFolderPicker(nint ownerWindowHandle)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            return new SmokeCheckItem("Folder picker", SmokeCheckSeverity.Warning, "ScreenFast has not finished window initialization, so the folder picker is not ready yet.");
        }

        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, ownerWindowHandle);
            return new SmokeCheckItem("Folder picker", SmokeCheckSeverity.Ok, "The native Windows folder picker is ready.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Folder picker", SmokeCheckSeverity.Warning, $"ScreenFast may not be able to open the folder picker: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckDiagnosticsWorkspace()
    {
        try
        {
            _ = typeof(ZipFile);
            var tempFolder = Path.Combine(Path.GetTempPath(), "ScreenFast", "SmokeCheck");
            Directory.CreateDirectory(tempFolder);
            var probePath = Path.Combine(tempFolder, $"diag-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return new SmokeCheckItem("Diagnostics export", SmokeCheckSeverity.Ok, "Temporary diagnostics files can be prepared successfully.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Diagnostics export", SmokeCheckSeverity.Warning, $"Diagnostics export may be limited: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckTraySupport()
    {
        try
        {
            using var menu = new Forms.ContextMenuStrip();
            using var notifyIcon = new Forms.NotifyIcon
            {
                ContextMenuStrip = menu,
                Visible = false
            };
            return new SmokeCheckItem("Tray integration", SmokeCheckSeverity.Ok, "Tray dependencies are available.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Tray integration", SmokeCheckSeverity.Warning, $"Tray integration may be unavailable: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckHotkeySupport(nint ownerWindowHandle)
    {
        if (!NativeLibrary.TryLoad("user32.dll", out var handle))
        {
            return new SmokeCheckItem("Global hotkeys", SmokeCheckSeverity.Error, "The Windows user32 hotkey APIs are unavailable.");
        }

        NativeLibrary.Free(handle);
        return ownerWindowHandle == nint.Zero
            ? new SmokeCheckItem("Global hotkeys", SmokeCheckSeverity.Warning, "Global hotkeys will be available after the main window is fully initialized.")
            : new SmokeCheckItem("Global hotkeys", SmokeCheckSeverity.Ok, "Global hotkey dependencies are available.");
    }

    private static SmokeCheckItem CheckOverlaySupport(nint ownerWindowHandle)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            return new SmokeCheckItem("Recording overlay", SmokeCheckSeverity.Warning, "The recording overlay will be available after the main window is fully initialized.");
        }

        try
        {
            _ = typeof(Microsoft.UI.Windowing.DisplayArea);
            _ = typeof(Window);
            return new SmokeCheckItem("Recording overlay", SmokeCheckSeverity.Ok, "Overlay dependencies are available.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Recording overlay", SmokeCheckSeverity.Warning, $"The overlay may be unavailable: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckCaptureSupport()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported()
                ? new SmokeCheckItem("Screen capture", SmokeCheckSeverity.Ok, "Windows.Graphics.Capture reports that screen capture is supported.")
                : new SmokeCheckItem("Screen capture", SmokeCheckSeverity.Error, "This Windows installation does not report support for Windows.Graphics.Capture.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Screen capture", SmokeCheckSeverity.Warning, $"ScreenFast could not confirm capture support yet: {ex.Message}");
        }
    }

    private static SmokeCheckItem CheckEncodingSupport()
    {
        var libraries = new[] { "mfplat.dll", "mfreadwrite.dll", "mf.dll" };
        var missing = new List<string>();

        foreach (var library in libraries)
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
            {
                missing.Add(library);
                continue;
            }

            NativeLibrary.Free(handle);
        }

        return missing.Count == 0
            ? new SmokeCheckItem("MP4 encoding", SmokeCheckSeverity.Ok, "Media Foundation libraries are available for MP4 encoding.")
            : new SmokeCheckItem("MP4 encoding", SmokeCheckSeverity.Error, $"Media Foundation libraries are missing: {string.Join(", ", missing)}");
    }

    private static SmokeCheckItem CheckRecoveryStorage()
    {
        try
        {
            Directory.CreateDirectory(ScreenFastPaths.RootFolderPath);
            var probePath = Path.Combine(ScreenFastPaths.RootFolderPath, $"recovery-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, ScreenFastPaths.RecoveryStateFilePath);
            File.Delete(probePath);
            return new SmokeCheckItem("Recovery state", SmokeCheckSeverity.Ok, "Interrupted-session recovery storage is writable.");
        }
        catch (Exception ex)
        {
            return new SmokeCheckItem("Recovery state", SmokeCheckSeverity.Warning, $"Recovery state may not persist correctly: {ex.Message}");
        }
    }

    private void LogReport(SmokeCheckReport report)
    {
        _logService.Info(
            "smoke_checks.completed",
            "ScreenFast completed startup smoke checks.",
            new Dictionary<string, object?>
            {
                ["warningCount"] = report.WarningCount,
                ["errorCount"] = report.ErrorCount,
                ["highestSeverity"] = report.HighestSeverity
            });

        foreach (var item in report.Items.Where(item => item.Severity != SmokeCheckSeverity.Ok))
        {
            _logService.Warning(
                "smoke_checks.issue",
                item.Message,
                new Dictionary<string, object?>
                {
                    ["name"] = item.Name,
                    ["severity"] = item.Severity
                });
        }
    }
}

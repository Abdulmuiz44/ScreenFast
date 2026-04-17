using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class DiagnosticsExportService : IDiagnosticsExportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOutputFolderPickerService _outputFolderPickerService;
    private readonly IRecordingHistoryService _recordingHistoryService;
    private readonly IRecoveryService _recoveryService;
    private readonly IRecordingMetadataReader _metadataReader;
    private readonly IScreenFastLogService _logService;

    public DiagnosticsExportService(
        IOutputFolderPickerService outputFolderPickerService,
        IRecordingHistoryService recordingHistoryService,
        IRecoveryService recoveryService,
        IRecordingMetadataReader metadataReader,
        IScreenFastLogService logService)
    {
        _outputFolderPickerService = outputFolderPickerService;
        _recordingHistoryService = recordingHistoryService;
        _recoveryService = recoveryService;
        _metadataReader = metadataReader;
        _logService = logService;
    }

    public async Task<OperationResult<string>> ExportAsync(
        nint ownerWindowHandle,
        AppSettings settings,
        RecorderStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _logService.Info("diagnostics.export_requested", "ScreenFast started a diagnostics export.");
        await _logService.FlushAsync(cancellationToken);

        var destinationFolderResult = await _outputFolderPickerService.PickOutputFolderAsync(ownerWindowHandle, cancellationToken);
        if (!destinationFolderResult.IsSuccess)
        {
            _logService.Warning(
                "diagnostics.export_destination_failed",
                "ScreenFast could not pick a diagnostics destination.",
                new Dictionary<string, object?> { ["error"] = destinationFolderResult.Error?.Message });
            return OperationResult<string>.Failure(destinationFolderResult.Error!);
        }

        if (string.IsNullOrWhiteSpace(destinationFolderResult.Value))
        {
            _logService.Info("diagnostics.export_cancelled", "Diagnostics export was cancelled.");
            return OperationResult<string>.Success(string.Empty);
        }

        var destinationFolder = destinationFolderResult.Value;
        var exportFilePath = Path.Combine(destinationFolder, $"ScreenFast-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var tempFolder = Path.Combine(Path.GetTempPath(), "ScreenFast", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempFolder);
            var logsFolder = Path.Combine(tempFolder, "logs");
            Directory.CreateDirectory(logsFolder);

            foreach (var logFile in await _logService.GetRecentLogFilesAsync(cancellationToken))
            {
                if (File.Exists(logFile))
                {
                    var target = Path.Combine(logsFolder, Path.GetFileName(logFile));
                    File.Copy(logFile, target, true);
                }
            }

            var history = await _recordingHistoryService.GetRecentAsync(cancellationToken);
            var recentHistory = history.Take(25).ToList();
            var metadataSidecars = await BuildMetadataSidecarSummariesAsync(recentHistory, cancellationToken);
            var manifest = new DiagnosticsManifest(
                DateTimeOffset.UtcNow,
                ResolveAppVersion(),
                Environment.OSVersion.VersionString,
                Environment.MachineName,
                settings,
                snapshot,
                _recoveryService.CurrentInterruptedSession,
                recentHistory,
                metadataSidecars);

            var manifestPath = Path.Combine(tempFolder, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, SerializerOptions), cancellationToken);

            if (File.Exists(exportFilePath))
            {
                File.Delete(exportFilePath);
            }

            ZipFile.CreateFromDirectory(tempFolder, exportFilePath, CompressionLevel.Fastest, false);
            _logService.Info(
                "diagnostics.export_succeeded",
                "ScreenFast exported diagnostics successfully.",
                new Dictionary<string, object?> { ["path"] = exportFilePath });
            return OperationResult<string>.Success(exportFilePath);
        }
        catch (Exception ex)
        {
            _logService.Error(
                "diagnostics.export_failed",
                "ScreenFast could not export diagnostics.",
                new Dictionary<string, object?>
                {
                    ["destinationFolder"] = destinationFolder,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(AppError.ShellActionFailed($"ScreenFast could not export diagnostics: {ex.Message}"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }
            }
            catch
            {
            }
        }
    }


    private async Task<IReadOnlyList<DiagnosticsMetadataSidecarSummary>> BuildMetadataSidecarSummariesAsync(
        IReadOnlyList<RecordingHistoryEntry> history,
        CancellationToken cancellationToken)
    {
        var summaries = new List<DiagnosticsMetadataSidecarSummary>();
        foreach (var entry in history.Where(x => x.IsSuccess && !string.IsNullOrWhiteSpace(x.FilePath)).Take(10))
        {
            try
            {
                summaries.Add(await _metadataReader.CreateDiagnosticsSummaryAsync(entry.FilePath, cancellationToken));
            }
            catch (Exception ex)
            {
                _logService.Warning(
                    "diagnostics.metadata_summary_failed",
                    "ScreenFast could not include metadata sidecar awareness for one recording.",
                    new Dictionary<string, object?>
                    {
                        ["fileName"] = entry.FileName,
                        ["error"] = ex.Message
                    });
            }
        }

        return summaries;
    }
    private static string ResolveAppVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
    }
}

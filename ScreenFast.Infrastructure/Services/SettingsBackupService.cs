using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class SettingsBackupService : ISettingsBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPresetLibraryService _presetLibraryService;
    private readonly IScreenFastLogService _logService;
    private readonly string _backupFolder;

    public SettingsBackupService(IPresetLibraryService presetLibraryService, IScreenFastLogService logService)
    {
        _presetLibraryService = presetLibraryService;
        _logService = logService;
        _backupFolder = Path.Combine(ScreenFastPaths.RootFolderPath, "SettingsBackups");
    }

    public async Task<OperationResult<string>> ExportAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            var normalizedPresets = _presetLibraryService.NormalizePresets(settings.Presets);
            var normalizedProfiles = _presetLibraryService.NormalizeExportProfiles(settings.ExportProfiles);
            var normalizedSettings = settings with
            {
                Presets = normalizedPresets,
                ExportProfiles = normalizedProfiles,
                PresetSelection = _presetLibraryService.NormalizeSelection(settings.PresetSelection, normalizedPresets, normalizedProfiles)
            };
            var bundle = new SettingsBackupBundle(1, DateTimeOffset.UtcNow, "ScreenFast", normalizedSettings, normalizedPresets, normalizedProfiles);
            var path = Path.Combine(_backupFolder, $"screenfast-settings-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, bundle, JsonOptions, cancellationToken);
            _logService.Info("settings.backup_exported", "ScreenFast exported a settings backup bundle.", new Dictionary<string, object?> { ["path"] = path });
            return OperationResult<string>.Success(path);
        }
        catch (Exception ex)
        {
            _logService.Warning("settings.backup_export_failed", "ScreenFast could not export a settings backup bundle.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return OperationResult<string>.Failure(AppError.ShellActionFailed($"ScreenFast could not export settings: {ex.Message}"));
        }
    }

    public async Task<OperationResult<AppSettings>> ImportLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_backupFolder))
            {
                return OperationResult<AppSettings>.Failure(AppError.InvalidState("No ScreenFast settings backup folder exists yet. Export a backup first."));
            }

            var latest = Directory.EnumerateFiles(_backupFolder, "screenfast-settings-*.json")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                return OperationResult<AppSettings>.Failure(AppError.InvalidState("No ScreenFast settings backup bundle was found."));
            }

            await using var stream = File.OpenRead(latest.FullName);
            var bundle = await JsonSerializer.DeserializeAsync<SettingsBackupBundle>(stream, JsonOptions, cancellationToken);
            if (bundle is null || bundle.BundleVersion != 1 || !string.Equals(bundle.AppName, "ScreenFast", StringComparison.Ordinal))
            {
                return OperationResult<AppSettings>.Failure(AppError.InvalidState("The selected settings backup is not a valid ScreenFast bundle."));
            }

            var presets = _presetLibraryService.NormalizePresets(bundle.Presets ?? bundle.Settings.Presets);
            var profiles = _presetLibraryService.NormalizeExportProfiles(bundle.ExportProfiles ?? bundle.Settings.ExportProfiles);
            var settings = bundle.Settings with
            {
                Version = Math.Max(bundle.Settings.Version, 3),
                Presets = presets,
                ExportProfiles = profiles,
                PresetSelection = _presetLibraryService.NormalizeSelection(bundle.Settings.PresetSelection, presets, profiles),
                DismissedRecoverySessionId = string.IsNullOrWhiteSpace(bundle.Settings.DismissedRecoverySessionId) ? null : bundle.Settings.DismissedRecoverySessionId
            };

            _logService.Info("settings.backup_imported", "ScreenFast imported a settings backup bundle.", new Dictionary<string, object?> { ["path"] = latest.FullName });
            return OperationResult<AppSettings>.Success(settings);
        }
        catch (Exception ex)
        {
            _logService.Warning("settings.backup_import_failed", "ScreenFast could not import a settings backup bundle.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return OperationResult<AppSettings>.Failure(AppError.ShellActionFailed($"ScreenFast could not import settings: {ex.Message}"));
        }
    }
}

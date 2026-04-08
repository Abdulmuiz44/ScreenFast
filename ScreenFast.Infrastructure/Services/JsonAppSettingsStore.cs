using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public JsonAppSettingsStore()
    {
        Directory.CreateDirectory(ScreenFastPaths.RootFolderPath);
        _settingsPath = ScreenFastPaths.SettingsFilePath;
    }

    public async Task<AppSettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettingsLoadResult(AppSettings.CreateDefault(), null);
            }

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken);
            if (settings is null)
            {
                return new AppSettingsLoadResult(AppSettings.CreateDefault(), "ScreenFast could not read the saved settings file, so defaults were restored.");
            }

            settings = Normalize(settings);
            return new AppSettingsLoadResult(settings, null);
        }
        catch
        {
            return new AppSettingsLoadResult(AppSettings.CreateDefault(), "ScreenFast could not load the saved settings file, so defaults were restored.");
        }
    }

    public async Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, Normalize(settings), SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _settingsPath, true);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(AppError.ShellActionFailed($"ScreenFast could not save settings: {ex.Message}"));
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var qualityPreset = Enum.IsDefined(typeof(VideoQualityPreset), settings.QualityPreset)
            ? settings.QualityPreset
            : VideoQualityPreset.Standard;
        var postRecordingBehavior = Enum.IsDefined(typeof(PostRecordingOpenBehavior), settings.PostRecordingOpenBehavior)
            ? settings.PostRecordingOpenBehavior
            : PostRecordingOpenBehavior.None;
        var countdownOption = Enum.IsDefined(typeof(RecordingCountdownOption), settings.CountdownOption)
            ? settings.CountdownOption
            : RecordingCountdownOption.Off;
        var overlayEnabled = settings.Version < 2 ? true : settings.OverlayEnabled;

        return settings with
        {
            Version = Math.Max(settings.Version, 2),
            QualityPreset = qualityPreset,
            PostRecordingOpenBehavior = postRecordingBehavior,
            CountdownOption = countdownOption,
            OverlayEnabled = overlayEnabled,
            DismissedRecoverySessionId = string.IsNullOrWhiteSpace(settings.DismissedRecoverySessionId) ? null : settings.DismissedRecoverySessionId
        };
    }
}

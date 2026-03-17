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
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenFast");
        _settingsPath = Path.Combine(root, "settings.json");
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
                return new AppSettingsLoadResult(
                    AppSettings.CreateDefault(),
                    "Saved settings were empty, so ScreenFast started with defaults.");
            }

            return new AppSettingsLoadResult(Normalize(settings), null);
        }
        catch (JsonException)
        {
            return new AppSettingsLoadResult(
                AppSettings.CreateDefault(),
                "Saved settings could not be read, so ScreenFast started with defaults.");
        }
        catch (Exception)
        {
            return new AppSettingsLoadResult(
                AppSettings.CreateDefault(),
                "ScreenFast could not read saved settings, so defaults were used.");
        }
    }

    public async Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, Normalize(settings), SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(AppError.RecordingFailed($"ScreenFast could not save settings: {ex.Message}"));
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        return settings with
        {
            Version = settings.Version <= 0 ? 1 : settings.Version,
            QualityPreset = Enum.IsDefined(settings.QualityPreset) ? settings.QualityPreset : VideoQualityPreset.Standard,
            Hotkeys = settings.Hotkeys ?? HotkeySettings.CreateDefault(),
            PostRecordingOpenBehavior = Enum.IsDefined(settings.PostRecordingOpenBehavior)
                ? settings.PostRecordingOpenBehavior
                : PostRecordingOpenBehavior.None,
            DismissedRecoverySessionId = string.IsNullOrWhiteSpace(settings.DismissedRecoverySessionId)
                ? null
                : settings.DismissedRecoverySessionId
        };
    }
}

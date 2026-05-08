namespace ScreenFast.Core.Models;

public sealed record SettingsBackupBundle(
    int BundleVersion,
    DateTimeOffset CreatedAtUtc,
    string AppName,
    AppSettings Settings,
    ScreenFastPresetLibrary Presets,
    ExportProfileLibrary ExportProfiles);

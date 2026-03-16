namespace ScreenFast.Core.Models;

public sealed record VideoQualityPresetDefinition(
    VideoQualityPreset Preset,
    string DisplayName,
    uint TargetBitrate,
    int FrameRate);

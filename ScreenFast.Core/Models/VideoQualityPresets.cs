namespace ScreenFast.Core.Models;

public static class VideoQualityPresets
{
    public static readonly IReadOnlyList<VideoQualityPresetDefinition> All =
    [
        new(VideoQualityPreset.Standard, "Standard", 6_000_000, 30),
        new(VideoQualityPreset.High, "High", 10_000_000, 30),
        new(VideoQualityPreset.Ultra, "Ultra", 16_000_000, 30)
    ];

    public static VideoQualityPresetDefinition Get(VideoQualityPreset preset)
    {
        return All.FirstOrDefault(definition => definition.Preset == preset) ?? All[0];
    }
}

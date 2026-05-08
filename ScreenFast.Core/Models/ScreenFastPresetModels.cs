namespace ScreenFast.Core.Models;

public sealed record ScreenFastPresetSelection(
    string RecordingPresetId,
    string ZoomPresetId,
    string StylingPresetId,
    string ExportPresetId,
    string ExportProfileId)
{
    public static ScreenFastPresetSelection CreateDefault() => new(
        ScreenFastPresetDefaults.QuickDemoRecordingId,
        ScreenFastPresetDefaults.StandardZoomId,
        ScreenFastPresetDefaults.CleanStylingId,
        ScreenFastPresetDefaults.RawRecorderExportId,
        ScreenFastPresetDefaults.RawRecorderProfileId);
}

public sealed record RecordingPreset(
    string Id,
    string DisplayName,
    string Description,
    VideoQualityPreset QualityPreset,
    bool IncludeSystemAudio,
    bool IncludeMicrophone,
    RecordingCountdownOption CountdownOption,
    bool OverlayEnabled,
    string ExportPresetId);

public sealed record ZoomPreset(
    string Id,
    string DisplayName,
    string Description,
    double TargetScale,
    int DwellMilliseconds,
    int TransitionMilliseconds,
    double SafeMarginRatio);

public sealed record StylingPreset(
    string Id,
    string DisplayName,
    string Description,
    ExportBackgroundStyle BackgroundStyle,
    string BackgroundValue,
    int Padding,
    int CornerRadius,
    bool ShadowEnabled,
    bool FrameEnabled);

public sealed record ExportPreset(
    string Id,
    string DisplayName,
    string Description,
    string ExportProfileId,
    string ZoomPresetId,
    string StylingPresetId);

public sealed record ScreenFastPresetLibrary(
    int Version,
    List<RecordingPreset> RecordingPresets,
    List<ZoomPreset> ZoomPresets,
    List<StylingPreset> StylingPresets,
    List<ExportPreset> ExportPresets)
{
    public static ScreenFastPresetLibrary CreateDefault() => new(
        1,
        ScreenFastPresetDefaults.CreateRecordingPresets().ToList(),
        ScreenFastPresetDefaults.CreateZoomPresets().ToList(),
        ScreenFastPresetDefaults.CreateStylingPresets().ToList(),
        ScreenFastPresetDefaults.CreateExportPresets().ToList());
}

public static class ScreenFastPresetDefaults
{
    public const string QuickDemoRecordingId = "recording.quick-demo";
    public const string TutorialRecordingId = "recording.tutorial";
    public const string MeetingClipRecordingId = "recording.meeting-clip";
    public const string ProductWalkthroughRecordingId = "recording.product-walkthrough";

    public const string SubtleZoomId = "zoom.subtle";
    public const string StandardZoomId = "zoom.standard";
    public const string StrongZoomId = "zoom.strong";

    public const string CleanStylingId = "style.clean";
    public const string GradientStylingId = "style.gradient";
    public const string BrandedFrameStylingId = "style.branded-frame";

    public const string RawRecorderExportId = "export.raw-recorder";
    public const string TutorialPolishedExportId = "export.tutorial-polished";
    public const string SocialClipExportId = "export.social-clip";
    public const string DemoPresentationExportId = "export.demo-presentation";

    public const string RawRecorderProfileId = "profile.raw-recorder";
    public const string TutorialPolishedProfileId = "profile.tutorial-polished";
    public const string SocialClipProfileId = "profile.social-clip";
    public const string DemoPresentationProfileId = "profile.demo-presentation";

    public static IReadOnlyList<RecordingPreset> CreateRecordingPresets() =>
    [
        new(QuickDemoRecordingId, "Quick Demo", "Fast screen demo with standard quality and quiet defaults.", VideoQualityPreset.Standard, false, false, RecordingCountdownOption.Off, true, RawRecorderExportId),
        new(TutorialRecordingId, "Tutorial", "Higher-quality lesson capture with microphone and polished tutorial export intent.", VideoQualityPreset.High, false, true, RecordingCountdownOption.ThreeSeconds, true, TutorialPolishedExportId),
        new(MeetingClipRecordingId, "Meeting Clip", "Lightweight meeting excerpt with system audio enabled and raw output preserved.", VideoQualityPreset.Standard, true, false, RecordingCountdownOption.Off, true, RawRecorderExportId),
        new(ProductWalkthroughRecordingId, "Product Walkthrough", "Presentation-ready walkthrough defaults for demos with microphone narration.", VideoQualityPreset.High, true, true, RecordingCountdownOption.ThreeSeconds, true, DemoPresentationExportId)
    ];

    public static IReadOnlyList<ZoomPreset> CreateZoomPresets() =>
    [
        new(SubtleZoomId, "Subtle Zoom", "Gentle motion for documentation and meeting clips.", 1.15, 900, 350, 0.12),
        new(StandardZoomId, "Standard Zoom", "Balanced cursor-aware zoom planning for tutorials and demos.", 1.35, 800, 300, 0.16),
        new(StrongZoomId, "Strong Zoom", "More assertive focus for dense UI or social crops.", 1.65, 650, 260, 0.20)
    ];

    public static IReadOnlyList<StylingPreset> CreateStylingPresets() =>
    [
        new(CleanStylingId, "Clean", "Neutral canvas with light padding and no decorative frame.", ExportBackgroundStyle.Solid, "#101828", 64, 18, false, false),
        new(GradientStylingId, "Gradient", "Soft gradient presentation background with rounded content.", ExportBackgroundStyle.Gradient, "#111827,#2563EB", 96, 24, true, true),
        new(BrandedFrameStylingId, "Branded Frame", "Framed layout ready for future brand colors and title treatments.", ExportBackgroundStyle.Solid, "#0F172A", 112, 28, true, true)
    ];

    public static IReadOnlyList<ExportPreset> CreateExportPresets() =>
    [
        new(RawRecorderExportId, "Raw Recorder Output", "Keep the finalized MP4 as the only required artifact.", RawRecorderProfileId, StandardZoomId, CleanStylingId),
        new(TutorialPolishedExportId, "Tutorial Polished Output", "Plan zoom and styling for tutorial export while preserving raw MP4.", TutorialPolishedProfileId, StandardZoomId, GradientStylingId),
        new(SocialClipExportId, "Social Clip Output", "Plan a more focused export profile for future social layouts.", SocialClipProfileId, StrongZoomId, GradientStylingId),
        new(DemoPresentationExportId, "Demo Presentation Output", "Presentation-style output plan for product walkthroughs.", DemoPresentationProfileId, SubtleZoomId, BrandedFrameStylingId)
    ];
}

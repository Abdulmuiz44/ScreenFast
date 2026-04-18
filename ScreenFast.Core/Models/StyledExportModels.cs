namespace ScreenFast.Core.Models;

public enum StyledExportPreset
{
    CleanDemo,
    Tutorial,
    SocialWide
}

public enum StyledExportBackgroundKind
{
    SolidColor,
    LinearGradient
}

public enum StyledExportShadowKind
{
    None,
    Soft
}

public sealed record StyledExportColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public static StyledExportColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue, 255);
}

public sealed record StyledExportBackground(
    StyledExportBackgroundKind Kind,
    StyledExportColor PrimaryColor,
    StyledExportColor? SecondaryColor,
    double GradientAngleDegrees);

public sealed record StyledExportFrameStyle(
    int PaddingPixels,
    int CornerRadiusPixels,
    StyledExportShadowKind ShadowKind,
    int ShadowBlurPixels,
    int ShadowOffsetXPixels,
    int ShadowOffsetYPixels,
    double ShadowOpacity);

public sealed record StyledExportCompositionSettings(
    StyledExportPreset Preset,
    int OutputWidth,
    int OutputHeight,
    StyledExportBackground Background,
    StyledExportFrameStyle FrameStyle)
{
    public static StyledExportCompositionSettings Create(StyledExportPreset preset = StyledExportPreset.CleanDemo) => preset switch
    {
        StyledExportPreset.Tutorial => new(
            preset,
            1920,
            1080,
            new StyledExportBackground(StyledExportBackgroundKind.SolidColor, StyledExportColor.FromRgb(245, 247, 250), null, 0),
            new StyledExportFrameStyle(112, 28, StyledExportShadowKind.Soft, 42, 0, 18, 0.22)),
        StyledExportPreset.SocialWide => new(
            preset,
            1920,
            1080,
            new StyledExportBackground(StyledExportBackgroundKind.LinearGradient, StyledExportColor.FromRgb(20, 24, 31), StyledExportColor.FromRgb(44, 54, 66), 135),
            new StyledExportFrameStyle(96, 30, StyledExportShadowKind.Soft, 52, 0, 22, 0.28)),
        _ => new(
            preset,
            1920,
            1080,
            new StyledExportBackground(StyledExportBackgroundKind.SolidColor, StyledExportColor.FromRgb(238, 241, 245), null, 0),
            new StyledExportFrameStyle(104, 24, StyledExportShadowKind.Soft, 40, 0, 18, 0.20))
    };
}

public sealed record StyledExportRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public sealed record StyledExportTimelineSegment(
    long StartMilliseconds,
    long EndMilliseconds,
    CameraViewportRect SourceViewport,
    StyledExportRect OutputContentRect,
    AutoZoomEasing Easing,
    AutoZoomSegmentReason Reason,
    bool ClickInfluenced);

public sealed record StyledExportDiagnostics(
    int SegmentCount,
    long DurationMilliseconds,
    int OutputWidth,
    int OutputHeight,
    double SourceAspectRatio,
    double OutputContentAspectRatio,
    int TransitionCount,
    bool UsesZoomPlan,
    bool UsesClickInfluence,
    IReadOnlyList<string> Warnings);

public sealed record StyledExportPlan(
    int SchemaVersion,
    string MetadataPath,
    string ZoomPlanPath,
    string InputVideoPath,
    string SuggestedOutputVideoPath,
    StyledExportCompositionSettings Composition,
    StyledExportRect OutputContentRect,
    IReadOnlyList<StyledExportTimelineSegment> TimelineSegments,
    StyledExportDiagnostics Diagnostics);

namespace ScreenFast.Core.Models;

public enum AutoZoomPlannerPreset
{
    Subtle,
    Standard,
    Aggressive
}

public enum AutoZoomEasing
{
    Hold,
    EaseInOutCubic
}

public enum AutoZoomSegmentReason
{
    Hold,
    FollowCursor,
    ClickEmphasis,
    Transition
}

public sealed record AutoZoomPlannerOptions(
    AutoZoomPlannerPreset Preset,
    double MinZoomFactor,
    double MaxZoomFactor,
    double TargetZoomFactor,
    double SafeMarginRatio,
    double DeadZoneRatio,
    long MinimumSegmentDurationMilliseconds,
    long MinimumRetargetIntervalMilliseconds,
    long TransitionDurationMilliseconds,
    long ClickHoldMilliseconds,
    bool EnableClickEmphasis)
{
    public static AutoZoomPlannerOptions Create(AutoZoomPlannerPreset preset = AutoZoomPlannerPreset.Standard) => preset switch
    {
        AutoZoomPlannerPreset.Subtle => new(preset, 1.0, 1.45, 1.22, 0.24, 0.18, 650, 900, 320, 500, true),
        AutoZoomPlannerPreset.Aggressive => new(preset, 1.0, 2.2, 1.75, 0.30, 0.12, 400, 550, 240, 650, true),
        _ => new(preset, 1.0, 1.8, 1.45, 0.27, 0.15, 550, 750, 280, 560, true)
    };
}

public sealed record AutoZoomPlannerInput(
    RecordingRenderInput RenderInput,
    AutoZoomPlannerOptions Options);

public sealed record CameraViewportRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2d);

    public double CenterY => Y + (Height / 2d);
}

public sealed record CameraClampInfo(
    bool ClampedLeft,
    bool ClampedTop,
    bool ClampedRight,
    bool ClampedBottom)
{
    public bool WasClamped => ClampedLeft || ClampedTop || ClampedRight || ClampedBottom;
}

public sealed record AutoZoomKeyframe(
    long OffsetMilliseconds,
    CameraViewportRect Viewport,
    double ZoomFactor,
    AutoZoomSegmentReason Reason,
    CameraClampInfo ClampInfo);

public sealed record AutoZoomSegment(
    long StartMilliseconds,
    long EndMilliseconds,
    AutoZoomKeyframe StartKeyframe,
    AutoZoomKeyframe EndKeyframe,
    AutoZoomEasing Easing,
    AutoZoomSegmentReason Reason,
    double SafeMarginRatio,
    bool ClickInfluenced)
{
    public long DurationMilliseconds => Math.Max(0, EndMilliseconds - StartMilliseconds);
}

public sealed record AutoZoomPlannerDiagnostics(
    int SegmentCount,
    long TotalTimelineDurationMilliseconds,
    double AverageZoomFactor,
    double MaxZoomFactor,
    int TransitionCount,
    int CursorSamplesConsumed,
    int ClickEventsConsumed,
    bool ClickEventsInfluencedPlan,
    int ClampedSegmentCount,
    IReadOnlyList<string> Warnings);

public sealed record AutoZoomPlan(
    int SchemaVersion,
    string SourceMetadataPath,
    string OutputVideoPath,
    int SourceWidth,
    int SourceHeight,
    AutoZoomPlannerOptions Options,
    IReadOnlyList<AutoZoomSegment> Segments,
    AutoZoomPlannerDiagnostics Diagnostics);

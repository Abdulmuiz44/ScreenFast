namespace ScreenFast.Core.Models;

public enum ExportOutputMode
{
    RawOnly,
    StyledOnly,
    Both
}

public enum ExportBackgroundStyle
{
    Solid,
    Gradient,
    Transparent
}

public enum ExportCanvasMode
{
    SourceSize,
    FitCanvas,
    FixedSize
}

public enum ExportOutputNamingMode
{
    PreserveRawName,
    AppendProfileName,
    AppendTimestamp
}

public sealed record ExportFrameStyle(
    bool Enabled,
    int CornerRadius,
    bool ShadowEnabled,
    int ShadowBlur,
    string ShadowColor,
    int BorderThickness,
    string BorderColor);

public sealed record ExportProfile(
    string Id,
    string DisplayName,
    string Description,
    ExportOutputMode OutputMode,
    ExportBackgroundStyle BackgroundStyle,
    string BackgroundValue,
    ExportCanvasMode CanvasMode,
    int? CanvasWidth,
    int? CanvasHeight,
    int Padding,
    int Margin,
    ExportFrameStyle Frame,
    string LinkedZoomPresetId,
    ExportOutputNamingMode NamingMode)
{
    public bool RequestsStyledOutput => OutputMode is ExportOutputMode.StyledOnly or ExportOutputMode.Both;
}

public sealed record ExportProfileLibrary(int Version, List<ExportProfile> Profiles)
{
    public static ExportProfileLibrary CreateDefault() => new(1, ExportProfileDefaults.CreateDefaults().ToList());
}

public static class ExportProfileDefaults
{
    public static IReadOnlyList<ExportProfile> CreateDefaults() =>
    [
        new(
            ScreenFastPresetDefaults.RawRecorderProfileId,
            "Raw Recorder Output",
            "Only the finalized MP4 is required. Metadata and zoom plans may still be saved for future use.",
            ExportOutputMode.RawOnly,
            ExportBackgroundStyle.Solid,
            "#000000",
            ExportCanvasMode.SourceSize,
            null,
            null,
            0,
            0,
            new ExportFrameStyle(false, 0, false, 0, "#00000000", 0, "#00000000"),
            ScreenFastPresetDefaults.StandardZoomId,
            ExportOutputNamingMode.PreserveRawName),
        new(
            ScreenFastPresetDefaults.TutorialPolishedProfileId,
            "Tutorial Polished Output",
            "Future styled tutorial export with balanced zoom, gradient background, padding, rounded frame, and shadow.",
            ExportOutputMode.Both,
            ExportBackgroundStyle.Gradient,
            "#111827,#2563EB",
            ExportCanvasMode.FitCanvas,
            1920,
            1080,
            96,
            32,
            new ExportFrameStyle(true, 24, true, 32, "#66000000", 1, "#33FFFFFF"),
            ScreenFastPresetDefaults.StandardZoomId,
            ExportOutputNamingMode.AppendProfileName),
        new(
            ScreenFastPresetDefaults.SocialClipProfileId,
            "Social Clip Output",
            "Future social-friendly export intent with strong zoom and fixed vertical canvas.",
            ExportOutputMode.Both,
            ExportBackgroundStyle.Gradient,
            "#312E81,#DB2777",
            ExportCanvasMode.FixedSize,
            1080,
            1920,
            120,
            24,
            new ExportFrameStyle(true, 30, true, 36, "#77000000", 0, "#00000000"),
            ScreenFastPresetDefaults.StrongZoomId,
            ExportOutputNamingMode.AppendProfileName),
        new(
            ScreenFastPresetDefaults.DemoPresentationProfileId,
            "Demo Presentation Output",
            "Future product-demo export with brand-friendly frame and presentation canvas.",
            ExportOutputMode.Both,
            ExportBackgroundStyle.Solid,
            "#0F172A",
            ExportCanvasMode.FixedSize,
            1920,
            1080,
            112,
            40,
            new ExportFrameStyle(true, 28, true, 40, "#80000000", 1, "#33475569"),
            ScreenFastPresetDefaults.SubtleZoomId,
            ExportOutputNamingMode.AppendProfileName)
    ];
}

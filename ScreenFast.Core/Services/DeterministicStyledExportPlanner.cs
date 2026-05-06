using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Services;

public sealed class DeterministicStyledExportPlanner : IStyledExportPlanner
{
    private const int StyledExportSchemaVersion = 1;
    private const int SupportedMetadataSchemaVersion = 1;
    private const int SupportedZoomPlanSchemaVersion = 1;

    public OperationResult<StyledExportPlan> Plan(
        RecordingRenderInput renderInput,
        AutoZoomPlan zoomPlan,
        StyledExportCompositionSettings composition,
        string zoomPlanPath,
        string? suggestedOutputVideoPath = null)
    {
        var warnings = new List<string>();
        var validation = Validate(renderInput, zoomPlan, composition, zoomPlanPath, warnings);
        if (!validation.IsSuccess)
        {
            return OperationResult<StyledExportPlan>.Failure(validation.Error!);
        }

        var normalizedComposition = NormalizeComposition(composition, warnings);
        var contentRect = ComputeContentRect(renderInput.Metadata.Source.Width, renderInput.Metadata.Source.Height, normalizedComposition);
        var timeline = zoomPlan.Segments
            .OrderBy(segment => segment.StartMilliseconds)
            .ThenBy(segment => segment.EndMilliseconds)
            .Select(segment => new StyledExportTimelineSegment(
                segment.StartMilliseconds,
                segment.EndMilliseconds,
                segment.EndKeyframe.Viewport,
                contentRect,
                segment.Easing,
                segment.Reason,
                segment.ClickInfluenced))
            .ToArray();

        if (timeline.Length == 0)
        {
            warnings.Add("Zoom plan contained no segments; styled export emitted no render timeline segments.");
        }

        var outputPath = string.IsNullOrWhiteSpace(suggestedOutputVideoPath)
            ? BuildSuggestedOutputVideoPath(renderInput.Metadata.OutputVideoPath)
            : suggestedOutputVideoPath;

        var diagnostics = BuildDiagnostics(renderInput, zoomPlan, normalizedComposition, contentRect, timeline, warnings);
        return OperationResult<StyledExportPlan>.Success(
            new StyledExportPlan(
                StyledExportSchemaVersion,
                renderInput.MetadataPath,
                zoomPlanPath,
                renderInput.Metadata.OutputVideoPath,
                outputPath,
                normalizedComposition,
                contentRect,
                timeline,
                diagnostics));
    }

    private static OperationResult Validate(
        RecordingRenderInput renderInput,
        AutoZoomPlan zoomPlan,
        StyledExportCompositionSettings composition,
        string zoomPlanPath,
        List<string> warnings)
    {
        if (renderInput.Metadata.SchemaVersion != SupportedMetadataSchemaVersion)
        {
            return OperationResult.Failure(AppError.SourceUnavailable($"Unsupported ScreenFast metadata schema version {renderInput.Metadata.SchemaVersion}."));
        }

        if (zoomPlan.SchemaVersion != SupportedZoomPlanSchemaVersion)
        {
            return OperationResult.Failure(AppError.SourceUnavailable($"Unsupported ScreenFast zoom plan schema version {zoomPlan.SchemaVersion}."));
        }

        if (renderInput.Metadata.Source.Width <= 0 || renderInput.Metadata.Source.Height <= 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export planning requires positive source dimensions."));
        }

        if (composition.OutputWidth <= 0 || composition.OutputHeight <= 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Styled export planning requires positive output dimensions."));
        }

        if (!string.Equals(renderInput.Metadata.OutputVideoPath, zoomPlan.OutputVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Zoom plan video path differs from metadata video path; styled export will use the metadata video path as source of truth.");
        }

        if (!string.Equals(renderInput.MetadataPath, zoomPlan.SourceMetadataPath, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Zoom plan metadata path differs from render input metadata path.");
        }

        if (string.IsNullOrWhiteSpace(zoomPlanPath))
        {
            warnings.Add("Zoom plan path was not provided; styled export can still be planned in memory.");
        }

        return OperationResult.Success();
    }

    private static StyledExportCompositionSettings NormalizeComposition(StyledExportCompositionSettings composition, List<string> warnings)
    {
        var outputWidth = Math.Clamp(composition.OutputWidth, 320, 7680);
        var outputHeight = Math.Clamp(composition.OutputHeight, 240, 4320);
        var maxPadding = Math.Max(0, Math.Min(outputWidth, outputHeight) / 3);
        var padding = Math.Clamp(composition.FrameStyle.PaddingPixels, 0, maxPadding);
        var radius = Math.Clamp(composition.FrameStyle.CornerRadiusPixels, 0, Math.Min(outputWidth, outputHeight) / 5);
        var shadowBlur = Math.Clamp(composition.FrameStyle.ShadowBlurPixels, 0, 240);
        var shadowOpacity = Math.Clamp(composition.FrameStyle.ShadowOpacity, 0d, 1d);

        if (outputWidth != composition.OutputWidth || outputHeight != composition.OutputHeight || padding != composition.FrameStyle.PaddingPixels)
        {
            warnings.Add("Styled export composition settings were clamped to supported output bounds.");
        }

        return composition with
        {
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            FrameStyle = composition.FrameStyle with
            {
                PaddingPixels = padding,
                CornerRadiusPixels = radius,
                ShadowBlurPixels = shadowBlur,
                ShadowOpacity = shadowOpacity
            }
        };
    }

    private static StyledExportRect ComputeContentRect(int sourceWidth, int sourceHeight, StyledExportCompositionSettings composition)
    {
        var availableWidth = Math.Max(1, composition.OutputWidth - (composition.FrameStyle.PaddingPixels * 2));
        var availableHeight = Math.Max(1, composition.OutputHeight - (composition.FrameStyle.PaddingPixels * 2));
        var sourceAspect = sourceWidth / (double)sourceHeight;
        var availableAspect = availableWidth / (double)availableHeight;

        double width;
        double height;
        if (sourceAspect >= availableAspect)
        {
            width = availableWidth;
            height = width / sourceAspect;
        }
        else
        {
            height = availableHeight;
            width = height * sourceAspect;
        }

        var x = (composition.OutputWidth - width) / 2d;
        var y = (composition.OutputHeight - height) / 2d;
        return new StyledExportRect(Round3(x), Round3(y), Round3(width), Round3(height));
    }

    private static StyledExportDiagnostics BuildDiagnostics(
        RecordingRenderInput renderInput,
        AutoZoomPlan zoomPlan,
        StyledExportCompositionSettings composition,
        StyledExportRect contentRect,
        IReadOnlyList<StyledExportTimelineSegment> timeline,
        IReadOnlyList<string> warnings)
    {
        var sourceAspect = renderInput.Metadata.Source.Width / (double)renderInput.Metadata.Source.Height;
        var contentAspect = contentRect.Width / contentRect.Height;
        return new StyledExportDiagnostics(
            timeline.Count,
            Math.Max(renderInput.Metadata.DurationMilliseconds, zoomPlan.Diagnostics.TotalTimelineDurationMilliseconds),
            composition.OutputWidth,
            composition.OutputHeight,
            Round3(sourceAspect),
            Round3(contentAspect),
            timeline.Count(segment => segment.Reason == AutoZoomSegmentReason.Transition),
            timeline.Count > 0,
            timeline.Any(segment => segment.ClickInfluenced),
            warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string BuildSuggestedOutputVideoPath(string inputVideoPath)
    {
        var folder = Path.GetDirectoryName(inputVideoPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(inputVideoPath);
        return Path.Combine(folder, $"{fileName}.styled.mp4");
    }

    private static double Round3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}

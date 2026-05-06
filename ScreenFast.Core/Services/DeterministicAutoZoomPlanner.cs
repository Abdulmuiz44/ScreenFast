using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Services;

public sealed class DeterministicAutoZoomPlanner : IAutoZoomPlanner
{
    private const int SupportedMetadataSchemaVersion = 1;
    private const int PlanSchemaVersion = 1;

    public OperationResult<AutoZoomPlan> Plan(AutoZoomPlannerInput input)
    {
        var warnings = new List<string>();
        var validation = Validate(input, warnings);
        if (!validation.IsSuccess)
        {
            return OperationResult<AutoZoomPlan>.Failure(validation.Error!);
        }

        var metadata = input.RenderInput.Metadata;
        var options = NormalizeOptions(input.Options, warnings);
        var sourceWidth = metadata.Source.Width;
        var sourceHeight = metadata.Source.Height;
        var duration = Math.Max(0, metadata.DurationMilliseconds);
        var samples = NormalizeSamples(metadata.Telemetry.CursorSamples, sourceWidth, sourceHeight, duration).ToArray();
        var clicks = NormalizeClicks(metadata.Telemetry.ClickEvents, sourceWidth, sourceHeight, duration).ToArray();

        if (metadata.SchemaVersion != SupportedMetadataSchemaVersion)
        {
            return OperationResult<AutoZoomPlan>.Failure(
                AppError.SourceUnavailable($"Unsupported ScreenFast metadata schema version {metadata.SchemaVersion}."));
        }

        warnings.AddRange(metadata.Warnings);
        warnings.AddRange(metadata.Telemetry.Warnings);
        if (samples.Length == 0)
        {
            warnings.Add("No usable source-relative cursor samples were available; planner emitted a full-frame hold.");
        }
        else if (samples.Length < 8)
        {
            warnings.Add("Cursor telemetry is sparse; zoom planning may be conservative.");
        }

        var fullFrame = CreateFullFrame(sourceWidth, sourceHeight);
        var currentFrame = fullFrame;
        var currentClamp = new CameraClampInfo(false, false, false, false);
        var currentZoom = 1d;
        var currentOffset = 0L;
        var lastRetargetOffset = long.MinValue / 2;
        var segments = new List<AutoZoomSegment>();
        var clickEventsInfluencedPlan = false;
        var clickIndex = 0;

        foreach (var sample in samples)
        {
            while (clickIndex < clicks.Length && clicks[clickIndex].OffsetMilliseconds <= sample.OffsetMilliseconds)
            {
                var click = clicks[clickIndex];
                if (options.EnableClickEmphasis && click.Kind == CursorClickEventKind.Down && CanRetarget(click.OffsetMilliseconds, lastRetargetOffset, options))
                {
                    var target = CreateTargetFrame(click.SourceX!.Value, click.SourceY!.Value, sourceWidth, sourceHeight, Math.Min(options.MaxZoomFactor, options.TargetZoomFactor + 0.2), options);
                    AddRetargetSegments(segments, ref currentFrame, ref currentClamp, ref currentZoom, ref currentOffset, target.Frame, target.Clamp, target.Zoom, click.OffsetMilliseconds, AutoZoomSegmentReason.ClickEmphasis, options, true);
                    lastRetargetOffset = click.OffsetMilliseconds;
                    clickEventsInfluencedPlan = true;
                }

                clickIndex++;
            }

            if (!CanRetarget(sample.OffsetMilliseconds, lastRetargetOffset, options))
            {
                continue;
            }

            if (IsInsideSafeRegion(sample.SourceX!.Value, sample.SourceY!.Value, currentFrame, options.SafeMarginRatio))
            {
                continue;
            }

            if (IsInsideDeadZone(sample.SourceX.Value, sample.SourceY.Value, currentFrame, options.DeadZoneRatio))
            {
                continue;
            }

            var planned = CreateTargetFrame(sample.SourceX.Value, sample.SourceY.Value, sourceWidth, sourceHeight, options.TargetZoomFactor, options);
            if (IsNearlySameFrame(currentFrame, planned.Frame))
            {
                continue;
            }

            AddRetargetSegments(segments, ref currentFrame, ref currentClamp, ref currentZoom, ref currentOffset, planned.Frame, planned.Clamp, planned.Zoom, sample.OffsetMilliseconds, AutoZoomSegmentReason.FollowCursor, options, false);
            lastRetargetOffset = sample.OffsetMilliseconds;
        }

        AddHoldSegment(segments, currentFrame, currentClamp, currentZoom, currentOffset, duration, options.SafeMarginRatio, clickInfluenced: false);

        if (segments.Count == 0)
        {
            AddHoldSegment(segments, fullFrame, new CameraClampInfo(false, false, false, false), 1d, 0, duration, options.SafeMarginRatio, clickInfluenced: false);
        }

        var diagnostics = BuildDiagnostics(segments, duration, samples.Length, clicks.Length, clickEventsInfluencedPlan, warnings);
        return OperationResult<AutoZoomPlan>.Success(
            new AutoZoomPlan(
                PlanSchemaVersion,
                input.RenderInput.MetadataPath,
                metadata.OutputVideoPath,
                sourceWidth,
                sourceHeight,
                options,
                segments,
                diagnostics));
    }

    private static OperationResult Validate(AutoZoomPlannerInput input, List<string> warnings)
    {
        var metadata = input.RenderInput.Metadata;
        if (metadata is null)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Auto-zoom planning requires recording metadata."));
        }

        if (metadata.Source.Width <= 0 || metadata.Source.Height <= 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Auto-zoom planning requires positive source dimensions."));
        }

        if (metadata.DurationMilliseconds < 0)
        {
            return OperationResult.Failure(AppError.SourceUnavailable("Auto-zoom planning requires a non-negative timeline duration."));
        }

        if (string.IsNullOrWhiteSpace(input.RenderInput.MetadataPath))
        {
            warnings.Add("Render input did not include a metadata path; the plan can still be generated in memory.");
        }

        return OperationResult.Success();
    }

    private static AutoZoomPlannerOptions NormalizeOptions(AutoZoomPlannerOptions options, List<string> warnings)
    {
        var minZoom = Clamp(options.MinZoomFactor, 1d, 8d);
        var maxZoom = Clamp(Math.Max(options.MaxZoomFactor, minZoom), minZoom, 8d);
        var targetZoom = Clamp(options.TargetZoomFactor, minZoom, maxZoom);
        var safeMargin = Clamp(options.SafeMarginRatio, 0.05, 0.45);
        var deadZone = Clamp(options.DeadZoneRatio, 0.02, 0.40);
        var minimumSegment = Math.Max(100, options.MinimumSegmentDurationMilliseconds);
        var retarget = Math.Max(100, options.MinimumRetargetIntervalMilliseconds);
        var transition = Math.Max(0, options.TransitionDurationMilliseconds);
        var clickHold = Math.Max(0, options.ClickHoldMilliseconds);

        if (minZoom != options.MinZoomFactor || maxZoom != options.MaxZoomFactor || targetZoom != options.TargetZoomFactor)
        {
            warnings.Add("Planner zoom options were clamped to supported ranges.");
        }

        return options with
        {
            MinZoomFactor = minZoom,
            MaxZoomFactor = maxZoom,
            TargetZoomFactor = targetZoom,
            SafeMarginRatio = safeMargin,
            DeadZoneRatio = deadZone,
            MinimumSegmentDurationMilliseconds = minimumSegment,
            MinimumRetargetIntervalMilliseconds = retarget,
            TransitionDurationMilliseconds = transition,
            ClickHoldMilliseconds = clickHold
        };
    }

    private static IEnumerable<CursorPositionSample> NormalizeSamples(
        IEnumerable<CursorPositionSample> samples,
        int sourceWidth,
        int sourceHeight,
        long duration)
    {
        return samples
            .Where(sample => sample.SourceX.HasValue && sample.SourceY.HasValue)
            .Select(sample => sample with
            {
                OffsetMilliseconds = Clamp(sample.OffsetMilliseconds, 0, duration),
                SourceX = (int)Math.Round(Clamp((double)sample.SourceX!.Value, 0d, Math.Max(0d, sourceWidth - 1d))),
                SourceY = (int)Math.Round(Clamp((double)sample.SourceY!.Value, 0d, Math.Max(0d, sourceHeight - 1d)))
            })
            .OrderBy(sample => sample.OffsetMilliseconds)
            .ThenBy(sample => sample.SourceX)
            .ThenBy(sample => sample.SourceY);
    }

    private static IEnumerable<CursorClickEvent> NormalizeClicks(
        IEnumerable<CursorClickEvent> clicks,
        int sourceWidth,
        int sourceHeight,
        long duration)
    {
        return clicks
            .Where(click => click.SourceX.HasValue && click.SourceY.HasValue)
            .Select(click => click with
            {
                OffsetMilliseconds = Clamp(click.OffsetMilliseconds, 0, duration),
                SourceX = (int)Math.Round(Clamp((double)click.SourceX!.Value, 0d, Math.Max(0d, sourceWidth - 1d))),
                SourceY = (int)Math.Round(Clamp((double)click.SourceY!.Value, 0d, Math.Max(0d, sourceHeight - 1d)))
            })
            .OrderBy(click => click.OffsetMilliseconds)
            .ThenBy(click => click.Button)
            .ThenBy(click => click.Kind);
    }

    private static bool CanRetarget(long offset, long lastRetargetOffset, AutoZoomPlannerOptions options)
    {
        return offset - lastRetargetOffset >= options.MinimumRetargetIntervalMilliseconds;
    }

    private static bool IsInsideSafeRegion(int sourceX, int sourceY, CameraViewportRect frame, double safeMarginRatio)
    {
        var marginX = frame.Width * safeMarginRatio;
        var marginY = frame.Height * safeMarginRatio;
        return sourceX >= frame.X + marginX &&
               sourceX <= frame.Right - marginX &&
               sourceY >= frame.Y + marginY &&
               sourceY <= frame.Bottom - marginY;
    }

    private static bool IsInsideDeadZone(int sourceX, int sourceY, CameraViewportRect frame, double deadZoneRatio)
    {
        return Math.Abs(sourceX - frame.CenterX) <= frame.Width * deadZoneRatio &&
               Math.Abs(sourceY - frame.CenterY) <= frame.Height * deadZoneRatio;
    }

    private static (CameraViewportRect Frame, double Zoom, CameraClampInfo Clamp) CreateTargetFrame(
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        double zoom,
        AutoZoomPlannerOptions options)
    {
        var targetZoom = Clamp(zoom, options.MinZoomFactor, options.MaxZoomFactor);
        var width = sourceWidth / targetZoom;
        var height = sourceHeight / targetZoom;
        var x = sourceX - (width / 2d);
        var y = sourceY - (height / 2d);
        var clamped = ClampFrame(x, y, width, height, sourceWidth, sourceHeight);
        return (clamped.Frame, targetZoom, clamped.ClampInfo);
    }

    private static (CameraViewportRect Frame, CameraClampInfo ClampInfo) ClampFrame(
        double x,
        double y,
        double width,
        double height,
        int sourceWidth,
        int sourceHeight)
    {
        var maxX = Math.Max(0, sourceWidth - width);
        var maxY = Math.Max(0, sourceHeight - height);
        var clampedX = Clamp(x, 0, maxX);
        var clampedY = Clamp(y, 0, maxY);
        return (
            new CameraViewportRect(Round3(clampedX), Round3(clampedY), Round3(Math.Min(width, sourceWidth)), Round3(Math.Min(height, sourceHeight))),
            new CameraClampInfo(clampedX != x && x < 0, clampedY != y && y < 0, clampedX != x && x > maxX, clampedY != y && y > maxY));
    }

    private static CameraViewportRect CreateFullFrame(int sourceWidth, int sourceHeight) => new(0, 0, sourceWidth, sourceHeight);

    private static bool IsNearlySameFrame(CameraViewportRect left, CameraViewportRect right)
    {
        return Math.Abs(left.X - right.X) < 2d &&
               Math.Abs(left.Y - right.Y) < 2d &&
               Math.Abs(left.Width - right.Width) < 2d &&
               Math.Abs(left.Height - right.Height) < 2d;
    }

    private static void AddRetargetSegments(
        List<AutoZoomSegment> segments,
        ref CameraViewportRect currentFrame,
        ref CameraClampInfo currentClamp,
        ref double currentZoom,
        ref long currentOffset,
        CameraViewportRect targetFrame,
        CameraClampInfo targetClamp,
        double targetZoom,
        long targetOffset,
        AutoZoomSegmentReason reason,
        AutoZoomPlannerOptions options,
        bool clickInfluenced)
    {
        var clampedTargetOffset = Math.Max(currentOffset, targetOffset);
        AddHoldSegment(segments, currentFrame, currentClamp, currentZoom, currentOffset, clampedTargetOffset, options.SafeMarginRatio, clickInfluenced: false);

        var transitionEnd = Math.Max(clampedTargetOffset, clampedTargetOffset + options.TransitionDurationMilliseconds);
        var startKeyframe = new AutoZoomKeyframe(clampedTargetOffset, currentFrame, Round3(currentZoom), AutoZoomSegmentReason.Transition, currentClamp);
        var endKeyframe = new AutoZoomKeyframe(transitionEnd, targetFrame, Round3(targetZoom), reason, targetClamp);

        if (transitionEnd > clampedTargetOffset)
        {
            segments.Add(
                new AutoZoomSegment(
                    clampedTargetOffset,
                    transitionEnd,
                    startKeyframe,
                    endKeyframe,
                    AutoZoomEasing.EaseInOutCubic,
                    AutoZoomSegmentReason.Transition,
                    options.SafeMarginRatio,
                    clickInfluenced));
        }

        currentFrame = targetFrame;
        currentClamp = targetClamp;
        currentZoom = targetZoom;
        currentOffset = transitionEnd;

        if (clickInfluenced && options.ClickHoldMilliseconds > 0)
        {
            var holdEnd = currentOffset + options.ClickHoldMilliseconds;
            AddHoldSegment(segments, currentFrame, currentClamp, currentZoom, currentOffset, holdEnd, options.SafeMarginRatio, clickInfluenced: true);
            currentOffset = holdEnd;
        }
    }

    private static void AddHoldSegment(
        List<AutoZoomSegment> segments,
        CameraViewportRect frame,
        CameraClampInfo clampInfo,
        double zoom,
        long start,
        long end,
        double safeMarginRatio,
        bool clickInfluenced)
    {
        if (end <= start)
        {
            return;
        }

        var keyframe = new AutoZoomKeyframe(start, frame, Round3(zoom), AutoZoomSegmentReason.Hold, clampInfo);
        var endKeyframe = keyframe with { OffsetMilliseconds = end };
        segments.Add(
            new AutoZoomSegment(
                start,
                end,
                keyframe,
                endKeyframe,
                AutoZoomEasing.Hold,
                AutoZoomSegmentReason.Hold,
                safeMarginRatio,
                clickInfluenced));
    }

    private static AutoZoomPlannerDiagnostics BuildDiagnostics(
        IReadOnlyList<AutoZoomSegment> segments,
        long duration,
        int sampleCount,
        int clickCount,
        bool clickEventsInfluencedPlan,
        IReadOnlyList<string> warnings)
    {
        var weightedDuration = segments.Sum(segment => Math.Max(1, segment.DurationMilliseconds));
        var averageZoom = weightedDuration == 0
            ? 1d
            : segments.Sum(segment => segment.EndKeyframe.ZoomFactor * Math.Max(1, segment.DurationMilliseconds)) / weightedDuration;
        var maxZoom = segments.Count == 0 ? 1d : segments.Max(segment => segment.EndKeyframe.ZoomFactor);
        return new AutoZoomPlannerDiagnostics(
            segments.Count,
            duration,
            Round3(averageZoom),
            Round3(maxZoom),
            segments.Count(segment => segment.Reason == AutoZoomSegmentReason.Transition),
            sampleCount,
            clickCount,
            clickEventsInfluencedPlan,
            segments.Count(segment => segment.StartKeyframe.ClampInfo.WasClamped || segment.EndKeyframe.ClampInfo.WasClamped),
            warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

    private static long Clamp(long value, long min, long max) => Math.Min(max, Math.Max(min, value));

    private static double Round3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}

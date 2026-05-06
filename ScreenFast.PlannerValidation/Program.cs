using ScreenFast.Core.Models;
using ScreenFast.Core.Services;

var checks = new List<(string Name, Action Check)>
{
    ("stable cursor path stays calm", ValidateStablePath),
    ("fast movement creates transitions", ValidateFastMovement),
    ("sparse telemetry warns and holds", ValidateSparseTelemetry),
    ("edge cursor clamps viewport", ValidateEdgeClamping),
    ("click event can influence plan", ValidateClickInfluence),
    ("planner output is deterministic", ValidateDeterminism),
    ("styled export layout preserves aspect", ValidateStyledExportLayout),
    ("styled export timeline is deterministic", ValidateStyledExportDeterminism),
    ("styled export default output is separate", ValidateStyledExportOutputNaming)
};

var failures = new List<string>();
foreach (var check in checks)
{
    try
    {
        check.Check();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {check.Name}: {ex.Message}");
        Console.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Auto-zoom planner validation failed.");
    Environment.Exit(1);
}

Console.WriteLine();
Console.WriteLine("Auto-zoom planner validation passed.");

static void ValidateStablePath()
{
    var plan = CreatePlan(CreateSamples((0, 960, 540), (1000, 970, 545), (2000, 955, 538)));
    Assert(plan.Diagnostics.CursorSamplesConsumed == 3, "Expected all stable samples to be consumed.");
    Assert(plan.Diagnostics.TransitionCount == 0, "Stable center movement should not transition.");
    Assert(plan.Segments.Count == 1, "Stable center movement should emit one hold segment.");
    Assert(plan.Diagnostics.MaxZoomFactor == 1d, "Stable center movement should stay full-frame.");
}

static void ValidateFastMovement()
{
    var plan = CreatePlan(CreateSamples((0, 960, 540), (900, 1720, 860), (1900, 260, 220), (3000, 1660, 860)));
    Assert(plan.Diagnostics.TransitionCount >= 2, "Fast movement should produce transitions.");
    Assert(plan.Diagnostics.MaxZoomFactor > 1d, "Fast movement should zoom in.");
}

static void ValidateSparseTelemetry()
{
    var plan = CreatePlan([]);
    Assert(plan.Diagnostics.SegmentCount == 1, "Sparse telemetry should emit a full-frame hold.");
    Assert(plan.Diagnostics.Warnings.Any(x => x.Contains("No usable", StringComparison.OrdinalIgnoreCase)), "Sparse telemetry should warn.");
}

static void ValidateEdgeClamping()
{
    var plan = CreatePlan(CreateSamples((0, 960, 540), (900, 20, 20), (1800, 1890, 1060)));
    Assert(plan.Diagnostics.ClampedSegmentCount > 0, "Edge movement should report clamping.");
    Assert(plan.Segments.All(segment => segment.EndKeyframe.Viewport.X >= 0 && segment.EndKeyframe.Viewport.Y >= 0), "Viewport should not go negative.");
    Assert(plan.Segments.All(segment => segment.EndKeyframe.Viewport.Right <= 1920.001 && segment.EndKeyframe.Viewport.Bottom <= 1080.001), "Viewport should stay inside source.");
}

static void ValidateClickInfluence()
{
    var clicks = new[]
    {
        new CursorClickEvent(900, CursorClickButton.Left, CursorClickEventKind.Down, 1500, 800, 1500, 800, true)
    };
    var plan = CreatePlan(CreateSamples((0, 960, 540), (900, 1500, 800), (1600, 1510, 810)), clicks);
    Assert(plan.Diagnostics.ClickEventsConsumed == 1, "Click event should be consumed.");
    Assert(plan.Diagnostics.ClickEventsInfluencedPlan, "Click event should influence plan.");
    Assert(plan.Segments.Any(segment => segment.ClickInfluenced), "At least one segment should be marked click-influenced.");
}

static void ValidateDeterminism()
{
    var samples = CreateSamples((0, 960, 540), (900, 1720, 860), (1900, 260, 220), (3000, 1660, 860));
    var first = CreatePlan(samples);
    var second = CreatePlan(samples);
    Assert(string.Join("|", first.Segments.Select(DescribeSegment)) == string.Join("|", second.Segments.Select(DescribeSegment)), "Planner segments should be identical across repeated runs.");
    Assert(DescribeDiagnostics(first.Diagnostics) == DescribeDiagnostics(second.Diagnostics), "Planner diagnostics should be identical across repeated runs.");
}


static void ValidateStyledExportLayout()
{
    var zoomPlan = CreatePlan(CreateSamples((0, 960, 540), (900, 1720, 860), (1900, 260, 220)));
    var exportPlan = CreateStyledExportPlan(zoomPlan);
    Assert(exportPlan.OutputContentRect.Width > 0 && exportPlan.OutputContentRect.Height > 0, "Styled export content rect should be positive.");
    Assert(exportPlan.OutputContentRect.X >= 0 && exportPlan.OutputContentRect.Y >= 0, "Styled export content rect should stay inside output.");
    Assert(exportPlan.OutputContentRect.Right <= exportPlan.Composition.OutputWidth + 0.001, "Styled export content rect should not exceed output width.");
    Assert(exportPlan.OutputContentRect.Bottom <= exportPlan.Composition.OutputHeight + 0.001, "Styled export content rect should not exceed output height.");
    Assert(Math.Abs(exportPlan.Diagnostics.SourceAspectRatio - exportPlan.Diagnostics.OutputContentAspectRatio) < 0.002, "Styled export should preserve source aspect ratio in the content frame.");
}

static void ValidateStyledExportDeterminism()
{
    var zoomPlan = CreatePlan(CreateSamples((0, 960, 540), (900, 1720, 860), (1900, 260, 220)));
    var first = CreateStyledExportPlan(zoomPlan);
    var second = CreateStyledExportPlan(zoomPlan);
    Assert(string.Join("|", first.TimelineSegments.Select(DescribeStyledSegment)) == string.Join("|", second.TimelineSegments.Select(DescribeStyledSegment)), "Styled export timeline should be deterministic.");
    Assert(first.OutputContentRect == second.OutputContentRect, "Styled export content rect should be deterministic.");
}

static void ValidateStyledExportOutputNaming()
{
    var zoomPlan = CreatePlan(CreateSamples((0, 960, 540), (900, 1720, 860)));
    var exportPlan = CreateStyledExportPlan(zoomPlan);
    Assert(exportPlan.InputVideoPath == "fixture.mp4", "Styled export should keep the raw MP4 as input.");
    Assert(exportPlan.SuggestedOutputVideoPath.EndsWith("fixture.styled.mp4", StringComparison.OrdinalIgnoreCase), "Styled export should suggest a separate output path.");
}
static AutoZoomPlan CreatePlan(
    IReadOnlyList<CursorPositionSample> samples,
    IReadOnlyList<CursorClickEvent>? clicks = null)
{
    var planner = new DeterministicAutoZoomPlanner();
    var metadata = new RecordingSidecarMetadata(
        1,
        "recording-fixture",
        "session-fixture",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "fixture.mp4",
        "fixture.mp4",
        new RecordingSourceMetadata("display:0x1", CaptureSourceKind.Display, "Display", "Display: Fixture", 1920, 1080, new SourceBoundsSnapshot(0, 0, 1920, 1080)),
        4000,
        VideoQualityPreset.Standard,
        "Standard",
        false,
        false,
        RecordingCountdownOption.Off,
        new RecordingTelemetryTimeline(20, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(4), new SourceBoundsSnapshot(0, 0, 1920, 1080), samples, clicks ?? [], []),
        [],
        []);
    var result = planner.Plan(new AutoZoomPlannerInput(new RecordingRenderInput("fixture.screenfast.json", metadata), AutoZoomPlannerOptions.Create()));
    if (!result.IsSuccess || result.Value is null)
    {
        throw new InvalidOperationException(result.Error?.Message ?? "Planner failed.");
    }

    return result.Value;
}

static IReadOnlyList<CursorPositionSample> CreateSamples(params (long Offset, int X, int Y)[] points)
{
    return points
        .Select(point => new CursorPositionSample(point.Offset, point.X, point.Y, point.X, point.Y, true))
        .ToArray();
}


static StyledExportPlan CreateStyledExportPlan(AutoZoomPlan zoomPlan)
{
    var planner = new DeterministicStyledExportPlanner();
    var metadata = new RecordingSidecarMetadata(
        1,
        "recording-fixture",
        "session-fixture",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "fixture.mp4",
        "fixture.mp4",
        new RecordingSourceMetadata("display:0x1", CaptureSourceKind.Display, "Display", "Display: Fixture", 1920, 1080, new SourceBoundsSnapshot(0, 0, 1920, 1080)),
        4000,
        VideoQualityPreset.Standard,
        "Standard",
        false,
        false,
        RecordingCountdownOption.Off,
        new RecordingTelemetryTimeline(20, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(4), new SourceBoundsSnapshot(0, 0, 1920, 1080), [], [], []),
        [],
        []);
    var result = planner.Plan(
        new RecordingRenderInput("fixture.screenfast.json", metadata),
        zoomPlan,
        StyledExportCompositionSettings.Create(),
        "fixture.zoomplan.json");
    if (!result.IsSuccess || result.Value is null)
    {
        throw new InvalidOperationException(result.Error?.Message ?? "Styled export planner failed.");
    }

    return result.Value;
}

static string DescribeStyledSegment(StyledExportTimelineSegment segment)
{
    return string.Join(",",
        segment.StartMilliseconds,
        segment.EndMilliseconds,
        segment.SourceViewport,
        segment.OutputContentRect,
        segment.Easing,
        segment.Reason,
        segment.ClickInfluenced);
}
static string DescribeSegment(AutoZoomSegment segment)
{
    return string.Join(",",
        segment.StartMilliseconds,
        segment.EndMilliseconds,
        segment.StartKeyframe.Viewport,
        segment.EndKeyframe.Viewport,
        segment.StartKeyframe.ZoomFactor,
        segment.EndKeyframe.ZoomFactor,
        segment.Easing,
        segment.Reason,
        segment.ClickInfluenced);
}

static string DescribeDiagnostics(AutoZoomPlannerDiagnostics diagnostics)
{
    return string.Join(",",
        diagnostics.SegmentCount,
        diagnostics.TotalTimelineDurationMilliseconds,
        diagnostics.AverageZoomFactor,
        diagnostics.MaxZoomFactor,
        diagnostics.TransitionCount,
        diagnostics.CursorSamplesConsumed,
        diagnostics.ClickEventsConsumed,
        diagnostics.ClickEventsInfluencedPlan,
        diagnostics.ClampedSegmentCount,
        string.Join(";", diagnostics.Warnings));
}
static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

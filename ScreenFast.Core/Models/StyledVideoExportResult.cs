namespace ScreenFast.Core.Models;

public sealed record StyledVideoExportResult(
    string InputVideoPath,
    string OutputVideoPath,
    string StyledExportPlanPath,
    int SegmentCount,
    IReadOnlyList<string> Warnings);

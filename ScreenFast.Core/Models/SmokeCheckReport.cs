namespace ScreenFast.Core.Models;

public sealed record SmokeCheckReport(DateTimeOffset CompletedAtUtc, IReadOnlyList<SmokeCheckItem> Items)
{
    public SmokeCheckSeverity HighestSeverity =>
        Items.Any(item => item.Severity == SmokeCheckSeverity.Error)
            ? SmokeCheckSeverity.Error
            : Items.Any(item => item.Severity == SmokeCheckSeverity.Warning)
                ? SmokeCheckSeverity.Warning
                : SmokeCheckSeverity.Ok;

    public int WarningCount => Items.Count(item => item.Severity == SmokeCheckSeverity.Warning);
    public int ErrorCount => Items.Count(item => item.Severity == SmokeCheckSeverity.Error);
    public bool HasIssues => HighestSeverity != SmokeCheckSeverity.Ok;
}

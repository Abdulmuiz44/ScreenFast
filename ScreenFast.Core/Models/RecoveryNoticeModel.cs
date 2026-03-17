namespace ScreenFast.Core.Models;

public sealed record RecoveryNoticeModel(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    string SourceSummary,
    string? TargetFilePath)
{
    public string StartedAtText => StartedAtUtc.LocalDateTime.ToString("g");
}

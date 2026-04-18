namespace ScreenFast.Core.Models;

public sealed record DiagnosticsManifest(
    DateTimeOffset CreatedAtUtc,
    string AppVersion,
    string OsVersion,
    string MachineName,
    AppSettings Settings,
    RecorderStatusSnapshot RecorderStatus,
    RecoverySessionMarker? InterruptedSession,
    IReadOnlyList<RecordingHistoryEntry> RecentHistory,
    IReadOnlyList<DiagnosticsMetadataSidecarSummary> RecentMetadataSidecars);

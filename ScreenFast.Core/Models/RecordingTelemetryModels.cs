namespace ScreenFast.Core.Models;

public sealed record SourceBoundsSnapshot(
    int Left,
    int Top,
    int Width,
    int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

public sealed record CursorPositionSample(
    long OffsetMilliseconds,
    int ScreenX,
    int ScreenY,
    int? SourceX,
    int? SourceY,
    bool IsInsideSource);

public enum CursorClickButton
{
    Left,
    Right,
    Middle
}

public enum CursorClickEventKind
{
    Down,
    Up
}

public sealed record CursorClickEvent(
    long OffsetMilliseconds,
    CursorClickButton Button,
    CursorClickEventKind Kind,
    int ScreenX,
    int ScreenY,
    int? SourceX,
    int? SourceY,
    bool IsInsideSource);

public sealed record RecordingTelemetryStartRequest(
    string SessionId,
    CaptureSourceModel Source,
    DateTimeOffset StartedAtUtc,
    int SampleRateHz);

public sealed record RecordingTelemetryTimeline(
    int SampleRateHz,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    SourceBoundsSnapshot? SourceBounds,
    IReadOnlyList<CursorPositionSample> CursorSamples,
    IReadOnlyList<CursorClickEvent> ClickEvents,
    IReadOnlyList<string> Warnings);

namespace ScreenFast.Core.Models;

public enum HotkeyValidationSeverity
{
    Info,
    Warning,
    Error
}

public enum HotkeyCommandKind
{
    StartRecording,
    StopRecording,
    PauseResumeRecording
}

public sealed record HotkeyValidationIssue(
    HotkeyValidationSeverity Severity,
    HotkeyCommandKind? Command,
    string Message,
    HotkeyGesture? Gesture);

public sealed record HotkeyValidationResult(IReadOnlyList<HotkeyValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != HotkeyValidationSeverity.Error);
    public string Summary => Issues.Count == 0 ? "Hotkeys are valid." : string.Join(" ", Issues.Select(issue => issue.Message));

    public static HotkeyValidationResult Success() => new([]);
}

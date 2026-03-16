namespace ScreenFast.Core.Results;

public sealed record AppError(string Code, string Message)
{
    public static AppError MissingWindowHandle() =>
        new("window_handle_missing", "The recorder window is not ready yet. Close and reopen the app, then try again.");

    public static AppError InvalidState(string message) =>
        new("invalid_state", message);

    public static AppError SourceUnavailable(string message) =>
        new("source_unavailable", message);

    public static AppError FolderSelectionFailed(string message) =>
        new("folder_picker_failed", message);

    public static AppError RecordingFailed(string message) =>
        new("recording_failed", message);
}

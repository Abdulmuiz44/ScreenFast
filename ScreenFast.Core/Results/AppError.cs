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

    public static AppError SourceSizeChanged() =>
        new("source_size_changed", "The selected source changed size during recording. ScreenFast stopped to avoid a corrupted MP4. Start a new recording with the new size.");

    public static AppError AudioCaptureFailed(string message) =>
        new("audio_capture_failed", message);
}

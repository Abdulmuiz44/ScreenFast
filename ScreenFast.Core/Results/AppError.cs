namespace ScreenFast.Core.Results;

public sealed record AppError(string Code, string Message)
{
    public static AppError MissingWindowHandle() =>
        new("window_handle_missing", "The recorder window is not ready yet. Close and reopen the app, then try again.");

    public static AppError InvalidState(string message) =>
        new("invalid_state", message);
}

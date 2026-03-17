using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IScreenFastLogService
{
    string LogsFolderPath { get; }

    void Info(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null);

    void Warning(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null);

    void Error(string eventName, string message, IReadOnlyDictionary<string, object?>? properties = null);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentLogFilesAsync(CancellationToken cancellationToken = default);
}

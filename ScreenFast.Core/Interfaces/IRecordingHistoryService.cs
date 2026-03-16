using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingHistoryService
{
    Task<IReadOnlyList<RecordingHistoryEntry>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task AddEntryAsync(RecordingHistoryEntry entry, CancellationToken cancellationToken = default);

    Task RemoveEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task ClearMissingAsync(CancellationToken cancellationToken = default);

    Task RefreshAvailabilityAsync(CancellationToken cancellationToken = default);
}

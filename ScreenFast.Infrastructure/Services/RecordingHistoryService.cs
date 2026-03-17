using System.Text.Json;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingHistoryService : IRecordingHistoryService
{
    private const int MaxEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _historyPath;
    private readonly IScreenFastLogService _logService;

    public RecordingHistoryService(IScreenFastLogService logService)
    {
        _logService = logService;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenFast");
        Directory.CreateDirectory(root);
        _historyPath = Path.Combine(root, "recording-history.json");
    }

    public async Task<IReadOnlyList<RecordingHistoryEntry>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadUnsafeAsync(cancellationToken);
            return store.Entries
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddEntryAsync(RecordingHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadUnsafeAsync(cancellationToken);
            var entries = store.Entries
                .Where(x => x.Id != entry.Id)
                .ToList();

            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries = entries.Take(MaxEntries).ToList();
            }

            await SaveUnsafeAsync(new RecordingHistoryStoreModel(1, entries), cancellationToken);
            _logService.Info(
                "history.entry_added",
                "ScreenFast added a recording history entry.",
                new Dictionary<string, object?>
                {
                    ["fileName"] = entry.FileName,
                    ["isSuccess"] = entry.IsSuccess,
                    ["duration"] = entry.Duration
                });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveEntryAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadUnsafeAsync(cancellationToken);
            var entries = store.Entries.Where(x => x.Id != entryId).ToList();
            await SaveUnsafeAsync(new RecordingHistoryStoreModel(1, entries), cancellationToken);
            _logService.Info("history.entry_removed", "ScreenFast removed a recording history entry.", new Dictionary<string, object?> { ["entryId"] = entryId });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveUnsafeAsync(RecordingHistoryStoreModel.CreateEmpty(), cancellationToken);
            _logService.Info("history.cleared", "ScreenFast cleared the recording history.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearMissingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadUnsafeAsync(cancellationToken);
            var filtered = store.Entries.Where(x => x.IsFileAvailable || !x.IsSuccess).ToList();
            await SaveUnsafeAsync(new RecordingHistoryStoreModel(1, filtered), cancellationToken);
            _logService.Info("history.missing_cleared", "ScreenFast removed missing-file history entries.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadUnsafeAsync(cancellationToken);
            var updated = store.Entries
                .Select(entry => entry with
                {
                    IsFileAvailable = entry.IsSuccess && !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath)
                })
                .ToList();
            await SaveUnsafeAsync(new RecordingHistoryStoreModel(1, updated), cancellationToken);
            _logService.Info("history.availability_refreshed", "ScreenFast refreshed recording history availability.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RecordingHistoryStoreModel> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyPath))
        {
            return RecordingHistoryStoreModel.CreateEmpty();
        }

        try
        {
            await using var stream = File.OpenRead(_historyPath);
            var model = await JsonSerializer.DeserializeAsync<RecordingHistoryStoreModel>(stream, JsonOptions, cancellationToken);
            return model ?? RecordingHistoryStoreModel.CreateEmpty();
        }
        catch (Exception ex)
        {
            _logService.Warning("history.load_failed", "ScreenFast could not read recording history and fell back to an empty list.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return RecordingHistoryStoreModel.CreateEmpty();
        }
    }

    private async Task SaveUnsafeAsync(RecordingHistoryStoreModel store, CancellationToken cancellationToken)
    {
        var tempPath = _historyPath + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, store, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _historyPath, true);
        }
        catch (Exception ex)
        {
            _logService.Warning("history.save_failed", "ScreenFast could not persist recording history.", new Dictionary<string, object?> { ["error"] = ex.Message });
            throw;
        }
    }
}

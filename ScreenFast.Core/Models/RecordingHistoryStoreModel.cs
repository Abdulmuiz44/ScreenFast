namespace ScreenFast.Core.Models;

public sealed record RecordingHistoryStoreModel(
    int Version,
    List<RecordingHistoryEntry> Entries)
{
    public static RecordingHistoryStoreModel CreateEmpty() => new(1, []);
}

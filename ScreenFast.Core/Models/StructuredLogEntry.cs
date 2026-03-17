namespace ScreenFast.Core.Models;

public sealed record StructuredLogEntry(
    DateTimeOffset TimestampUtc,
    ScreenFastLogLevel Level,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, string?>? Properties);

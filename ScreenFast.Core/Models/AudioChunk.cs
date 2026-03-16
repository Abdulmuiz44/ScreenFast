namespace ScreenFast.Core.Models;

public sealed record AudioChunk(
    AudioInputKind Kind,
    byte[] Buffer,
    int BytesRecorded,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    bool IsFloat,
    int SampleFrames,
    long TimestampHundredsOfNanoseconds);

namespace ScreenFast.Core.Models;

public sealed record CapturedFrame(
    nint TexturePointer,
    long TimestampHundredsOfNanoseconds,
    int Width,
    int Height);

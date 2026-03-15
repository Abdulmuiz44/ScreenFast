namespace ScreenFast.Core.Models;

public sealed record CaptureSourceModel(
    string SourceId,
    CaptureSourceKind Type,
    string DisplayName,
    int Width,
    int Height)
{
    public string DimensionsText => $"{Width} x {Height}";

    public string TypeDisplayName => Type switch
    {
        CaptureSourceKind.Display => "Display",
        CaptureSourceKind.Window => "Window",
        _ => "Unknown"
    };
}

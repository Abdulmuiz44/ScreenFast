namespace ScreenFast.Core.Models;

public sealed record HotkeySettings(
    HotkeyGesture StartRecording,
    HotkeyGesture StopRecording,
    HotkeyGesture PauseResumeRecording)
{
    public static HotkeySettings CreateDefault() =>
        new(
            new HotkeyGesture(true, true, false, 0x78),
            new HotkeyGesture(true, true, false, 0x79),
            new HotkeyGesture(true, true, false, 0x7A));
}

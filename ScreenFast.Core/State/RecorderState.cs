namespace ScreenFast.Core.State;

public enum RecorderState
{
    Idle = 0,
    Selecting = 1,
    Ready = 2,
    Recording = 3,
    Paused = 4,
    Stopping = 5,
    Error = 6
}

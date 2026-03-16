using ScreenFast.Core.Results;

namespace ScreenFast.Core.State;

public sealed class RecorderStateMachine
{
    private static readonly IReadOnlyDictionary<RecorderState, RecorderState[]> AllowedTransitions =
        new Dictionary<RecorderState, RecorderState[]>
        {
            [RecorderState.Idle] = [RecorderState.Selecting, RecorderState.Ready, RecorderState.Error],
            [RecorderState.Selecting] = [RecorderState.Idle, RecorderState.Ready, RecorderState.Error],
            [RecorderState.Ready] = [RecorderState.Selecting, RecorderState.Recording, RecorderState.Idle, RecorderState.Error],
            [RecorderState.Recording] = [RecorderState.Paused, RecorderState.Stopping, RecorderState.Error],
            [RecorderState.Paused] = [RecorderState.Recording, RecorderState.Stopping, RecorderState.Error],
            [RecorderState.Stopping] = [RecorderState.Idle, RecorderState.Ready, RecorderState.Error],
            [RecorderState.Error] = [RecorderState.Idle, RecorderState.Selecting, RecorderState.Ready]
        };

    public RecorderState CurrentState { get; private set; } = RecorderState.Idle;

    public OperationResult TransitionTo(RecorderState nextState)
    {
        if (CurrentState == nextState)
        {
            return OperationResult.Success();
        }

        if (!AllowedTransitions.TryGetValue(CurrentState, out var allowed) || !allowed.Contains(nextState))
        {
            return OperationResult.Failure(
                AppError.InvalidState($"Recorder cannot move from {CurrentState} to {nextState}."));
        }

        CurrentState = nextState;
        return OperationResult.Success();
    }
}

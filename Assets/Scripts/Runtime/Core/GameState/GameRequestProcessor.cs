using System;

// Queue에서 꺼낸 요청을 현재 상태에 맞는 실제 동작으로 명시적으로 연결한다.
public sealed class GameRequestProcessor : IGameRequestHandler
{
    private readonly GameStateManager _stateManager;

    public GameRequestProcessor(GameStateManager stateManager)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public void Handle(IGameRequest request)
    {
        if (!_stateManager.CanProcess(request))
            return;

        if (IsResetRequest(_stateManager.CurrentState, request))
        {
            _stateManager.BeginReset(request);
            return;
        }

        if (TryGetNextState(_stateManager.CurrentState, request, out GameState nextState))
        {
            _stateManager.ApplyTransition(nextState);
            return;
        }

        _stateManager.Reject(request, "request not allowed in current state");
    }

    // 각 요청이 현재 상태에서 실행하는 상태 전환을 한 곳에 명시한다.
    public static bool TryGetNextState(GameState current, IGameRequest request, out GameState next)
    {
        next = current;
        switch (request)
        {
            case InitializationCompletedRequest when current == GameState.Initializing:
                next = GameState.Ready;
                return true;
            case StartRequest when current == GameState.Ready:
                next = GameState.IntroDialogue;
                return true;
            case DialogueCompleteRequest when current == GameState.IntroDialogue:
                next = GameState.Countdown;
                return true;
            case CountdownCompleteRequest when current == GameState.Countdown:
                next = GameState.Battle;
                return true;
            case PauseRequest when current == GameState.Battle:
                next = GameState.Paused;
                return true;
            case ResumeRequest when current == GameState.Paused:
                next = GameState.Battle;
                return true;
            case TimerReachedZeroRequest when current == GameState.Battle:
                next = GameState.Finishing;
                return true;
            case FinishingCompleteRequest when current == GameState.Finishing:
                next = GameState.OutroDialogue;
                return true;
            case DialogueCompleteRequest when current == GameState.OutroDialogue:
                next = GameState.Result;
                return true;
            default:
                return false;
        }
    }

    // Scene Reload 없이 Reset을 시작할 수 있는 요청과 상태를 한 곳에 명시한다.
    public static bool IsResetRequest(GameState current, IGameRequest request)
    {
        return request is RestartRequest &&
                   (current == GameState.Paused || current == GameState.Result) ||
               request is QuitToStartRequest && current == GameState.Paused;
    }
}

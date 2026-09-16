// 현재 상태와 요청 타입에 따라 허용된 전이를 판정한다.
public static class GameStateRules
{
    // 일반 상태 변경 요청의 다음 상태를 찾는다.
    public static bool TryGetNext(GameState current, IGameRequest request, out GameState next)
    {
        next = current;
        switch (current)
        {
            case GameState.Initializing when request is InitializationCompletedRequest:
                next = GameState.Ready;
                return true;
            case GameState.Ready when request is StartRequest:
                next = GameState.IntroDialogue;
                return true;
            case GameState.IntroDialogue when request is DialogueCompleteRequest:
                next = GameState.Countdown;
                return true;
            case GameState.Countdown when request is CountdownCompleteRequest:
                next = GameState.Battle;
                return true;
            case GameState.Battle when request is PauseRequest:
                next = GameState.Paused;
                return true;
            case GameState.Paused when request is ResumeRequest:
                next = GameState.Battle;
                return true;
            case GameState.Battle when request is TimerReachedZeroRequest:
                next = GameState.Finishing;
                return true;
            case GameState.Finishing when request is FinishingCompleteRequest:
                next = GameState.OutroDialogue;
                return true;
            case GameState.OutroDialogue when request is DialogueCompleteRequest:
                next = GameState.Result;
                return true;
            default:
                return false;
        }
    }

    // 현재 상태에서 Reset 요청이 허용되는지 판정한다.
    public static bool IsResetRequest(GameState current, IGameRequest request)
    {
        return (request is RestartRequest &&
                (current == GameState.Paused || current == GameState.Result)) ||
               (request is QuitToStartRequest && current == GameState.Paused);
    }
}

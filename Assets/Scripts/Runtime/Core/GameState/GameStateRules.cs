// 기존 순수 로직 호출부를 Processor의 단일 전이표로 연결한다.
public static class GameStateRules
{
    public static bool TryGetNext(GameState current, IGameRequest request, out GameState next) =>
        GameRequestProcessor.TryGetNextState(current, request, out next);

    public static bool IsResetRequest(GameState current, IGameRequest request) =>
        GameRequestProcessor.IsResetRequest(current, request);
}

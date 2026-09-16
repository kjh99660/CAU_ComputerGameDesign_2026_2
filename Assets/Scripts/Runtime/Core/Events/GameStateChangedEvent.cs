// 성공한 상태 전환의 이전 상태, 현재 상태와 Tick을 전달하는 알림이다.
public sealed class GameStateChangedEvent
{
    public GameState Previous { get; }
    public GameState Current { get; }
    public int Tick { get; }

    // 상태 전환 결과를 변경할 수 없는 알림 객체로 만든다.
    public GameStateChangedEvent(GameState previous, GameState current, int tick)
    {
        Previous = previous;
        Current = current;
        Tick = tick;
    }
}

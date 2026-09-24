// 성공한 상태 전환을 관련 시스템에 즉시 적용하는 단일 진입점이다.
public interface IGameStateChangeHandler
{
    void HandleGameStateChanged(GameStateChangedEvent notification);
}

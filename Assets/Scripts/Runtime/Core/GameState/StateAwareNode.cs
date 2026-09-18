// 상태에 따라 실행 여부를 캐시하는 일반 C# 하위 요소의 기반 클래스다.
// 상태 알림은 상위 계층이 전달하며 Unity 생명주기 함수는 사용하지 않는다.
public abstract class StateAwareNode : IGameLoopNode
{
    private bool _hasState;

    public bool IsExecutionAllowed { get; private set; }
    protected GameState CurrentState { get; private set; } = GameState.Initializing;
    protected int CurrentTick { get; private set; }

    // 현재보다 오래된 라운드 알림을 버리고 상태 필터를 갱신한다.
    public void OnGameStateChanged(GameStateChangedEvent notification)
    {
        if (notification == null || (_hasState && notification.Tick < CurrentTick))
            return;

        if (_hasState && notification.Tick == CurrentTick && notification.Current == CurrentState)
            return;

        CurrentState = notification.Current;
        CurrentTick = notification.Tick;
        _hasState = true;
        IsExecutionAllowed = AllowsExecutionIn(CurrentState);
        OnStateApplied(CurrentState);
    }

    // 이 요소가 지정 상태에서 동작하는지 판정한다.
    public abstract bool AllowsExecutionIn(GameState state);

    // 상위 계층에서 호출하는 프레임 작업을 실행한다.
    public abstract void Update(float deltaTime, float unscaledDeltaTime);

    // 상태 진입 및 이탈에 따른 표시와 자원 정책을 적용한다.
    protected virtual void OnStateApplied(GameState state) { }
}

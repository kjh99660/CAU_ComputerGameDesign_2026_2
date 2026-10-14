// Battle에서만 새 Player 입력과 행동 시작을 처리할 일반 C# 요소다.
public sealed class PlayerInputSystem : StateAwareSystem
{
    private readonly IPlayerRuntime _runtime;

    public PlayerInputSystem() { }

    public PlayerInputSystem(IPlayerRuntime runtime)
    {
        _runtime = runtime;
    }

    // 새 조작은 전투 진행 중에만 허용한다.
    public override bool AllowsExecutionIn(GameState state) => state == GameState.Battle;

    // 실행 불가 상태라면 즉시 반환하고 입력 처리를 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        _runtime?.CaptureInput();
    }

    protected override void OnStateApplied(GameState state) =>
        _runtime?.ApplyState(state, CurrentTick);
}

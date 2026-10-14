// Battle과 Finishing에서 이미 시작된 Player 행동의 진행을 맡을 일반 C# 요소다.
public sealed class PlayerActionSystem : StateAwareSystem
{
    private readonly IPlayerRuntime _runtime;

    public PlayerActionSystem() { }

    public PlayerActionSystem(IPlayerRuntime runtime)
    {
        _runtime = runtime;
    }

    // 진행 중인 공격 결과는 Finishing에서도 정산할 수 있다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 실행 불가 상태라면 즉시 반환하고 진행 중 행동 갱신을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        _runtime?.UpdateAction(deltaTime);
    }
}

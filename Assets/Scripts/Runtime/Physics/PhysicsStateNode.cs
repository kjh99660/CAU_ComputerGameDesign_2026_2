// 상태 변경에 맞춰 물리 정지와 복원 정책을 적용할 일반 C# 요소다.
public sealed class PhysicsStateNode : StateAwareNode, IFixedGameLoopNode
{
    // 진행 중인 물리 결과는 Battle과 Finishing에서만 갱신한다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 상태 진입 시 Rigidbody 스냅샷과 복원을 연결할 자리다.
    protected override void OnStateApplied(GameState state)
    {
        // TODO: Ready, Dialogue, Countdown, Paused, Result에서 Body를 스냅샷 후 정지한다.
        // TODO: Battle 복귀 시 현재 Tick의 유효한 속도와 Simulation Flag만 복원한다.
        // TODO: Finishing에서는 물리 시뮬레이션을 유지하고 Reset 때 Snapshot을 폐기한다.
        // 주의: FixedUpdate 필터만으로 Unity Rigidbody 시뮬레이션은 정지하지 않는다.
    }

    // 프레임 주기에 적용할 추가 물리 정책은 아직 없다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;
    }

    // 실행 불가 상태라면 즉시 반환하고 사용자 정의 물리 갱신을 생략한다.
    public void FixedUpdate(float fixedDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: PhysicsPauseManager의 활성 Body 정책과 진행 중 Collision 결과를 확인한다.
    }
}

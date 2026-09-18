// Rigidbody의 등록과 사용자 정의 물리 갱신을 맡을 일반 C# 요소다.
public sealed class PhysicsBodyNode : StateAwareNode, IFixedGameLoopNode
{
    // Body의 진행 중 결과는 Battle과 Finishing에서만 갱신한다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 프레임 주기에 적용할 Body 작업은 아직 없다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;
    }

    // 실행 불가 상태라면 즉시 반환하고 Body 보조 갱신을 생략한다.
    public void FixedUpdate(float fixedDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: Pool 대여/반환 Adapter에서 Registry 등록과 해제를 연결한다.
        // TODO: Knockback과 Ground Collision 이후의 보조 처리를 구현한다.
        // 주의: 이 필터는 Rigidbody의 실제 시뮬레이션을 제어하지 않는다.
    }
}

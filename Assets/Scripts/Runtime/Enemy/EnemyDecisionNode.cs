// Battle에서만 Enemy의 새 이동과 공격 결정을 내릴 일반 C# 요소다.
public sealed class EnemyDecisionNode : StateAwareNode
{
    // 새 AI 판단은 전투 진행 중에만 허용한다.
    public override bool AllowsExecutionIn(GameState state) => state == GameState.Battle;

    // 실행 불가 상태라면 즉시 반환하고 AI 판단을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: Spatial Grid의 주변 후보만 조회해 추적과 Separation을 계산한다.
        // TODO: 판단 주기를 분산해 많은 Enemy의 매 프레임 판단 비용을 제한한다.
        // TODO: 공격 시작 전 거리, 상태, 예고 Animation 조건을 확인한다.
    }
}

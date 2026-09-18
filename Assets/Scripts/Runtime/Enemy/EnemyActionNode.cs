// Battle과 Finishing에서 시작된 Enemy 행동의 결과를 마무리할 일반 C# 요소다.
public sealed class EnemyActionNode : StateAwareNode
{
    // 진행 중인 공격과 죽음 연출은 Finishing에서도 처리한다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 실행 불가 상태라면 즉시 반환하고 행동 결과 갱신을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: 이미 시작된 공격 Active Frame, 피격, Knockback, Death를 처리한다.
        // TODO: Finishing에서는 새 AI 명령을 만들지 않고 기존 Collision 결과만 반영한다.
        // TODO: Death 연출 완료 시 Pool로 반환하고 이전 Tick의 Callback을 무시한다.
    }
}

// Battle과 Finishing에서 이미 시작된 Player 행동의 진행을 맡을 일반 C# 요소다.
public sealed class PlayerActionSystem : StateAwareSystem
{
    // 진행 중인 공격 결과는 Finishing에서도 정산할 수 있다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 실행 불가 상태라면 즉시 반환하고 진행 중 행동 갱신을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: 시작된 Combo, 공격 Animation Window, Hitbox와 피격 반응을 진행한다.
        // TODO: Finishing에서는 새 공격을 시작하지 않고 기존 적중과 Score만 정산한다.
        // TODO: 상태 이탈과 Reset 때 입력 버퍼, HitTargets, Coroutine을 취소한다.
    }
}

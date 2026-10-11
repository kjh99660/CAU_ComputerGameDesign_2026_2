// Battle과 Finishing에서 전투 HUD의 표시와 연출을 맡을 일반 C# 요소다.
public sealed class BattleHudSystem : StateAwareSystem
{
    // Score와 Timer 표시는 Finishing에서도 유지한다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    // 상태가 바뀔 때 HUD 표시 조합을 적용할 자리다.
    protected override void OnStateApplied(GameState state)
    {
        // TODO: Battle에서는 Pause, Score, Timer, Gauge를 표시한다.
        // TODO: Finishing에서는 Timer 0, Score, TIME UP만 표시하고 Pause와 Gauge를 숨긴다.
        // TODO: 숨긴 Panel의 Raycast와 불필요한 Layout 갱신을 차단한다.
    }

    // 실행 불가 상태라면 즉시 반환하고 HUD Animation을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: Score 변화와 Gauge 연출을 필요한 동안만 unscaled time으로 갱신한다.
        // TODO: 값 자체는 Score, Timer, Gauge 이벤트를 받아 갱신한다.
    }
}

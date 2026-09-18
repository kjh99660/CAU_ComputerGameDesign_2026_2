// Battle에서만 새 Player 입력과 행동 시작을 처리할 일반 C# 요소다.
public sealed class PlayerInputNode : StateAwareNode
{
    // 새 조작은 전투 진행 중에만 허용한다.
    public override bool AllowsExecutionIn(GameState state) => state == GameState.Battle;

    // 실행 불가 상태라면 즉시 반환하고 입력 처리를 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: 기존 Input System의 이동, 시점, 회피, Guard, Jump 입력을 읽는다.
        // TODO: 일반 공격, Charge Attack, Special Skill 시작 요청을 전투 요소에 전달한다.
        // TODO: 새 행동을 시작하기 전에 Action State와 입력 버퍼를 검증한다.
    }
}

// Intro와 Outro 대화의 표시와 진행 입력을 맡을 일반 C# 요소다.
public sealed class DialogueNode : StateAwareNode
{
    // 대화 관련 갱신은 두 Dialogue 상태에서만 허용한다.
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.IntroDialogue || state == GameState.OutroDialogue;

    // 상태 진입과 이탈에 맞춰 대화 화면을 정리할 자리다.
    protected override void OnStateApplied(GameState state)
    {
        // TODO: 좌우 Portrait, 현재 화자 강조, Dialogue Box와 Skip을 표시한다.
        // TODO: 대화 이탈과 Reset 때 텍스트 연출 및 지연 Callback을 취소한다.
    }

    // 실행 불가 상태라면 즉시 반환하고 대화 진행을 생략한다.
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        // TODO: 텍스트 연출과 진행 입력을 unscaled time으로 처리한다.
        // TODO: 일반 완료와 Skip 모두 DialogueCompleteRequest를 발행한다.
    }
}

// 전투 HUD와 대화 화면의 갱신을 순서대로 전달하는 계층이다.
public sealed class UiLayer : GameLoopLayer
{
    // 전투 HUD와 대화 요소를 순서대로 구성한다.
    public UiLayer() : base(new BattleHudNode(), new DialogueNode()) { }
}

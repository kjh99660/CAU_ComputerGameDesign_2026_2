// Player의 입력과 진행 중 행동을 순서대로 갱신하는 계층이다.
public sealed class PlayerLayer : GameLoopLayer
{
    // 입력 판단 뒤 행동 결과를 처리하도록 하위 요소를 구성한다.
    public PlayerLayer() : base(new PlayerInputSystem(), new PlayerActionSystem()) { }
}

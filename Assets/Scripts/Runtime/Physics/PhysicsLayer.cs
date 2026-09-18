// 물리 상태와 Body 요소를 순서대로 호출하는 일반 C# 계층이다.
public sealed class PhysicsLayer : GameLoopLayer, IFixedGameLoopNode
{
    private readonly PhysicsStateNode _state;
    private readonly PhysicsBodyNode _body;

    // 상태 정책을 Body 처리보다 먼저 배치한다.
    public PhysicsLayer() : this(new PhysicsStateNode(), new PhysicsBodyNode()) { }

    // 두 하위 요소를 프레임 갱신 순서에 등록한다.
    private PhysicsLayer(PhysicsStateNode state, PhysicsBodyNode body) : base(state, body)
    {
        _state = state;
        _body = body;
    }

    // Unity 물리 주기에 맞춰 두 하위 요소를 순서대로 호출한다.
    public void FixedUpdate(float fixedDeltaTime)
    {
        _state.FixedUpdate(fixedDeltaTime);
        _body.FixedUpdate(fixedDeltaTime);
    }
}

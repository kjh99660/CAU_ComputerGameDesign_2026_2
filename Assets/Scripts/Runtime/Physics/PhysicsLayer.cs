// 물리 상태 정책과 Body 보조 처리를 순서대로 실행하는 계층이다.
public sealed class PhysicsLayer : GameLoopLayer, IFixedGameLoopSystem
{
    private readonly PhysicsStateSystem _state;
    private readonly PhysicsBodySystem _body;

    public PhysicsLayer(PhysicsPauseManager pauseManager)
        : this(new PhysicsStateSystem(pauseManager), new PhysicsBodySystem()) { }

    private PhysicsLayer(PhysicsStateSystem state, PhysicsBodySystem body) : base(state, body)
    {
        _state = state;
        _body = body;
    }

    public void FixedUpdate(float fixedDeltaTime)
    {
        _state.FixedUpdate(fixedDeltaTime);
        _body.FixedUpdate(fixedDeltaTime);
    }
}

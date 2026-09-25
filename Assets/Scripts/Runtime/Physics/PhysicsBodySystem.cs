// 물리 결과를 위한 확장 지점을 제공한다. Rigidbody 정지/복원은 PhysicsPauseManager가 맡는다.
public sealed class PhysicsBodySystem : StateAwareSystem, IFixedGameLoopSystem
{
    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    public override void Update(float deltaTime, float unscaledDeltaTime) { }

    public void FixedUpdate(float fixedDeltaTime) { }
}

using System;

// GameState 변경을 PhysicsPauseManager의 Registry 정책으로 변환한다.
public sealed class PhysicsStateSystem : StateAwareSystem, IFixedGameLoopSystem
{
    private readonly PhysicsPauseManager _pauseManager;

    public PhysicsStateSystem(PhysicsPauseManager pauseManager)
    {
        _pauseManager = pauseManager ?? throw new ArgumentNullException(nameof(pauseManager));
    }

    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    protected override void OnStateApplied(GameState state) =>
        _pauseManager.ApplyState(state, CurrentTick);

    public override void Update(float deltaTime, float unscaledDeltaTime) { }

    public void FixedUpdate(float fixedDeltaTime) { }
}

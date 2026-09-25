using System;

// Battle에서만 새 Enemy Wave를 만들고 상태 변경을 EnemyRuntime에 전달한다.
public sealed class EnemySpawnSystem : StateAwareSystem
{
    private readonly EnemyRuntime _runtime;

    public EnemySpawnSystem(EnemyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override bool AllowsExecutionIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameState state) =>
        _runtime.ApplyState(state, CurrentTick);

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (IsExecutionAllowed)
            _runtime.UpdateSpawning(deltaTime, CurrentTick);
    }
}

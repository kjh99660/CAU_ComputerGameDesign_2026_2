using System;

// Battle에서만 Enemy의 추적, Separation과 공격 시작을 판단한다.
public sealed class EnemyAISystem : StateAwareSystem
{
    private readonly EnemyRuntime _runtime;

    public EnemyAISystem(EnemyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override bool AllowsExecutionIn(GameState state) => state == GameState.Battle;

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        _runtime.UpdateAI(deltaTime, CurrentTick);
    }
}

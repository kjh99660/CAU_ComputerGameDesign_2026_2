using System;

// Battle과 Finishing에서 이미 시작된 공격, Knockback과 사망을 마무리한다.
public sealed class EnemyActionSystem : StateAwareSystem
{
    private readonly EnemyRuntime _runtime;

    public EnemyActionSystem(EnemyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public override bool AllowsExecutionIn(GameState state) =>
        state == GameState.Battle || state == GameState.Finishing;

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsExecutionAllowed)
            return;

        _runtime.UpdateActions(deltaTime, CurrentTick);
    }
}

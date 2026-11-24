using System;
using UnityEngine;

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
            _runtime.UpdateSpawning(deltaTime,
                RoundClockStep(deltaTime, FeedbackTime.RealDelta(unscaledDeltaTime), Time.maximumDeltaTime), CurrentTick);
    }

    // The step the round timer (CombatRoundRuntime.Advance) took this frame; the battle time the spawn phases follow
    // takes the same one. The timer runs on real time in a frame that began inside a hit stop (in Battle only a hit stop
    // slows the game) and on game time otherwise. It runs before the spawner and may already have ended the stop and put
    // the time scale back, so the time scale now cannot tell which kind of frame this is; the frame's own deltaTime can,
    // because Unity scaled it by the time scale the frame began with. Game time never exceeds maximumDeltaTime, so a long
    // frame outside a hit stop is not mistaken for one.
    public static float RoundClockStep(float deltaTime, float realDelta, float maximumDeltaTime) =>
        deltaTime < Mathf.Min(realDelta, maximumDeltaTime) * 0.999f ? realDelta : deltaTime;
}

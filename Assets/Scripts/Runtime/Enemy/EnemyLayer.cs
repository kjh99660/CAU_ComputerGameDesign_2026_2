// Spawn, AI 판단, 진행 중 행동을 결정적인 순서로 갱신하는 Enemy 계층이다.
public sealed class EnemyLayer : GameLoopLayer
{
    public EnemyLayer(EnemyRuntime runtime)
        : base(new EnemySpawnSystem(runtime), new EnemyAISystem(runtime), new EnemyActionSystem(runtime)) { }
}

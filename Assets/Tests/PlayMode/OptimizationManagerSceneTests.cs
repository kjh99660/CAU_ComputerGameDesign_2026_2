using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

public sealed class OptimizationManagerSceneTests
{
    [UnityTest]
    public IEnumerator SceneSwitchesChangeSearchAndRetryDoesNotReuseEnemiesWithPoolingOff()
    {
        yield return TestScenes.Load(TestScenes.NeonCity);
        var manager = OptimizationManager.Instance;
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        Assert.That(manager, Is.Not.Null);
        yield return WaitFor(state, GameState.Ready);
        Assert.That(enemies.PooledEnemyCount, Is.EqualTo(enemies.Settings.MaximumEnemyCount));

        Set(manager, "spatialPartitioning", false);
        Set(manager, "enemyPooling", false);
        Assert.That(enemies.Perf.Mode, Is.EqualTo(EnemyCandidateMode.BruteForce));
        EnemyAgent previous = enemies.Perf.ActiveEnemies[0];
        enemies.Perf.SetMode(EnemyCandidateMode.SpatialGrid);

        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        yield return null;

        Assert.That(previous == null, Is.True);
        Assert.That(enemies.PooledEnemyCount, Is.EqualTo(enemies.Perf.ActiveEnemies.Count));
        Assert.That(enemies.Perf.Mode, Is.EqualTo(EnemyCandidateMode.BruteForce));
        Set(manager, "spatialPartitioning", true);
        Assert.That(enemies.Perf.Mode, Is.EqualTo(EnemyCandidateMode.SpatialGrid));
        EnemyAgent active = enemies.Perf.ActiveEnemies[0];
        var candidates = new System.Collections.Generic.List<EnemyAgent>();
        Assert.That(enemies.Perf.Collect(active.transform.position, 0.1f, candidates), Does.Contain(active));
    }

    [UnityTest]
    public IEnumerator HitQueryToggleKeepsTheSameColliders()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        var manager = OptimizationManager.Instance;
        Assert.That(manager, Is.Not.Null);
        Vector3 point = new Vector3(0f, 500f, 0f);
        var colliderObject = new GameObject("Overlap test");
        colliderObject.transform.position = point;
        var collider = colliderObject.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var buffer = new Collider[4];
        try
        {
            Collider[] optimized = AttackOverlapQuery.Sphere(point, 1f, buffer, ~0, out int optimizedCount);
            Assert.That(optimized, Is.SameAs(buffer));
            Assert.That(optimizedCount, Is.EqualTo(1));
            Set(manager, "nonAllocHitQueries", false);
            Collider[] unoptimized = AttackOverlapQuery.Sphere(point, 1f, buffer, ~0, out int unoptimizedCount);
            Assert.That(unoptimized, Is.Not.SameAs(buffer));
            Assert.That(unoptimizedCount, Is.EqualTo(optimizedCount));
            Assert.That(unoptimized[0], Is.SameAs(collider));
            Assert.That(optimized[0], Is.SameAs(collider));
        }
        finally
        {
            Object.Destroy(colliderObject);
        }
    }

    [UnityTest]
    public IEnumerator SrpBatcherToggleUpdatesTheRuntimeAndRestoresThePipelineSetting()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        var manager = OptimizationManager.Instance;
        var pipeline = new SerializedObject(GraphicsSettings.currentRenderPipeline);
        bool original = pipeline.FindProperty("m_UseSRPBatcher").boolValue;
        Assert.That(original, Is.True);

        Set(manager, "srpBatcher", false);
        yield return null;
        pipeline.Update();
        Assert.That(pipeline.FindProperty("m_UseSRPBatcher").boolValue, Is.False);
        Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.False);

        manager.enabled = false;
        pipeline.Update();
        Assert.That(pipeline.FindProperty("m_UseSRPBatcher").boolValue, Is.EqualTo(original));
        Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.EqualTo(original));
    }

    [UnityTest]
    public IEnumerator UnpooledSlashIsDestroyedAtRoundReset()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        Set(OptimizationManager.Instance, "combatVfxPooling", false);
        var root = new GameObject("Unpooled VFX test");
        var vfx = root.AddComponent<CombatVfxRuntime>();
        try
        {
            vfx.Configure(null, Resources.Load<CombatVfxSettings>("CombatVfxSettings"));
            vfx.ApplyState(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 7));
            vfx.PlaySlash(root.transform, 1f, 7);
            Assert.That(root.transform.childCount, Is.EqualTo(1));
            Transform oldEffect = root.transform.GetChild(0);

            yield return vfx.ResetRound(8);
            yield return null;
            Assert.That(oldEffect == null, Is.True);
            vfx.ApplyState(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 8));
            vfx.PlaySlash(root.transform, 1f, 8);
            Assert.That(root.transform.childCount, Is.EqualTo(1));
        }
        finally
        {
            Object.Destroy(root);
        }
    }

    private static void Set(OptimizationManager manager, string field, bool value)
    {
        var serialized = new SerializedObject(manager);
        serialized.FindProperty(field).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(state.CurrentState, Is.EqualTo(expected));
    }
}

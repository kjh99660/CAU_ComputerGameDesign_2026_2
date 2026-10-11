using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class EnemySystemsTests
{
    [Test]
    public void EnemySystemsFollowBattleAndFinishingPolicy()
    {
        GameObject owner = new("EnemyRuntimeTest");
        try
        {
            EnemyRuntime runtime = owner.AddComponent<EnemyRuntime>();
            Assert.IsTrue(new EnemySpawnSystem(runtime).AllowsExecutionIn(GameState.Battle));
            Assert.IsFalse(new EnemySpawnSystem(runtime).AllowsExecutionIn(GameState.Finishing));
            Assert.IsTrue(new EnemyAISystem(runtime).AllowsExecutionIn(GameState.Battle));
            Assert.IsFalse(new EnemyAISystem(runtime).AllowsExecutionIn(GameState.Finishing));
            Assert.IsTrue(new EnemyActionSystem(runtime).AllowsExecutionIn(GameState.Finishing));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void SpatialGridReturnsOnlyOverlappingCells()
    {
        GameObject firstObject = new("FirstEnemy");
        GameObject nearObject = new("NearEnemy");
        GameObject farObject = new("FarEnemy");
        try
        {
            EnemyAgent first = firstObject.AddComponent<EnemyAgent>();
            EnemyAgent near = nearObject.AddComponent<EnemyAgent>();
            EnemyAgent far = farObject.AddComponent<EnemyAgent>();
            first.transform.position = Vector3.zero;
            near.transform.position = new Vector3(2f, 0f, 1f);
            far.transform.position = new Vector3(30f, 0f, 30f);
            var grid = new EnemySpatialGrid(4f);
            grid.AddOrUpdate(first);
            grid.AddOrUpdate(near);
            grid.AddOrUpdate(far);

            var results = new List<EnemyAgent>();
            grid.Query(Vector3.zero, 3f, results);

            CollectionAssert.Contains(results, first);
            CollectionAssert.Contains(results, near);
            CollectionAssert.DoesNotContain(results, far);
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(nearObject);
            Object.DestroyImmediate(farObject);
        }
    }

    [Test]
    public void EnemyAcceptsOnlyOneHitPerSpawn()
    {
        GameObject gameObject = new("Enemy");
        EnemySettings settings = ScriptableObject.CreateInstance<EnemySettings>();
        try
        {
            EnemyAgent enemy = gameObject.AddComponent<EnemyAgent>();
            enemy.Initialize(null);
            enemy.Activate(Vector3.zero, 7);

            Assert.IsTrue(enemy.ApplyHit(Vector3.forward, settings, 7));
            Assert.IsFalse(enemy.ApplyHit(Vector3.forward, settings, 7));
            Assert.AreEqual(EnemyAgentState.Knockback, enemy.State);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void PhysicsPauseRestoresSameTickSnapshot()
    {
        GameObject gameObject = new("Body");
        try
        {
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.linearVelocity = new Vector3(3f, 0f, 0f);
            var manager = new PhysicsPauseManager();
            manager.ApplyState(GameState.Battle, 4);
            manager.Register(body, 4);

            manager.ApplyState(GameState.Paused, 4);
            Assert.IsTrue(body.isKinematic);

            manager.ApplyState(GameState.Battle, 4);
            Assert.IsFalse(body.isKinematic);
            Assert.AreEqual(3f, body.linearVelocity.x, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void RuntimeResetReturnsWaveToInitialIdlePopulation()
    {
        GameObject owner = new("EnemyRuntime");
        GameObject prototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
        EnemySettings settings = ScriptableObject.CreateInstance<EnemySettings>();
        try
        {
            EnemyAgent prefab = prototype.AddComponent<EnemyAgent>();
            EnemyRuntime runtime = owner.AddComponent<EnemyRuntime>();
            SetPrivateField(runtime, "enemyPrefab", prefab);
            SetPrivateField(runtime, "settings", settings);
            runtime.Configure(new PhysicsPauseManager());

            Exhaust(runtime.InitializeRound(3));
            Assert.AreEqual(settings.InitialEnemyCount, runtime.ActiveEnemyCount);

            runtime.ApplyState(GameState.Battle, 3);
            runtime.UpdateSpawning(settings.SpawnInterval, 3);
            Assert.Greater(runtime.ActiveEnemyCount, settings.InitialEnemyCount);

            runtime.StopProducing();
            Exhaust(runtime.ResetRound(4));
            Assert.AreEqual(settings.InitialEnemyCount, runtime.ActiveEnemyCount);

            runtime.StopProducing();
            Exhaust(runtime.ResetRound(4));
            Assert.AreEqual(settings.InitialEnemyCount, runtime.ActiveEnemyCount);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(prototype);
            Object.DestroyImmediate(settings);
        }
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Field not found: {name}");
        field.SetValue(target, value);
    }

    private static void Exhaust(System.Collections.IEnumerator routine)
    {
        while (routine.MoveNext()) { }
    }
}

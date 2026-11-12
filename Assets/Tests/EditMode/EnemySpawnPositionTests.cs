using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class EnemySpawnPositionTests
{
    [TestCase(0f)]
    [TestCase(2.5f)]
    public void WavesFollowPlayerXZButKeepTheRoundSpawnPlane(float heightOffset)
    {
        var owner = new GameObject("SpawnOwner");
        var target = new GameObject("SpawnTarget");
        var prototype = new GameObject("SpawnPrototype");
        var settings = ScriptableObject.CreateInstance<EnemySettings>();
        try
        {
            owner.transform.SetPositionAndRotation(new Vector3(110f, 35f, -80f), Quaternion.Euler(0f, 43f, 0f));
            target.transform.position = new Vector3(40f, 5f, -60f);
            Set(settings, "spawnHeight", heightOffset);
            Set(settings, "initialEnemyCount", 0);
            var runtime = owner.AddComponent<EnemyRuntime>();
            Set(runtime, "enemyPrefab", prototype.AddComponent<EnemyAgent>());
            Set(runtime, "settings", settings);
            runtime.SetTarget(target.transform);
            runtime.Configure(new PhysicsPauseManager());
            Exhaust(runtime.InitializeRound(3));
            runtime.ApplyState(GameState.Battle, 3);
            // Reproduce the elevated-player frame that previously made a whole wave float.
            target.transform.position = new Vector3(-70f, 85f, 120f);
            runtime.UpdateSpawning(settings.SpawnInterval, 3);
            var spawned = owner.GetComponentsInChildren<EnemyAgent>();
            Assert.AreEqual(settings.SpawnCountPerWave, spawned.Length);
            foreach (EnemyAgent enemy in spawned)
            {
                Assert.AreEqual(5f + heightOffset, enemy.transform.position.y, 0.001f,
                    "Current player elevation must not become a persistent kinematic enemy spawn height.");
                Vector3 offset = enemy.transform.position - target.transform.position;
                offset.y = 0f;
                Assert.That(offset.magnitude, Is.InRange(settings.MinimumSpawnDistance - 0.001f, settings.MaximumSpawnDistance + 0.001f));
            }
            // A reset captures the restored player's plane and reuses the old instances.
            target.transform.position = new Vector3(7f, -3f, 11f);
            runtime.StopProducing();
            Exhaust(runtime.ResetRound(4));
            runtime.ApplyState(GameState.Battle, 4);
            target.transform.position += Vector3.up * 50f;
            runtime.UpdateSpawning(settings.SpawnInterval, 4);
            foreach (EnemyAgent enemy in owner.GetComponentsInChildren<EnemyAgent>())
                Assert.AreEqual(-3f + heightOffset, enemy.transform.position.y, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(prototype);
            Object.DestroyImmediate(settings);
        }
    }

    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    private static void Exhaust(IEnumerator routine) { while (routine.MoveNext()) { } }
}

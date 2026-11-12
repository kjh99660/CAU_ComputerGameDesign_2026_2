using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class EnemySpawnSceneTests
{
    [UnityTest]
    public IEnumerator MainWavesStayInConfiguredRadiusAndDoNotCopyElevatedPlayerY()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity");
        var state = Object.FindFirstObjectByType<GameStateManager>();
        for (int frame = 0; frame < 120 && state.CurrentState != GameState.Ready; frame++) yield return null;
        Assert.AreEqual(GameState.Ready, state.CurrentState);
        var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var controller = player.GetComponent<CharacterController>();
        Vector3 original = player.transform.position;
        float spawnY = original.y + runtime.Settings.SpawnHeight;
        var initial = new HashSet<EnemyAgent>(Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None));
        Assert.AreEqual(runtime.Settings.InitialEnemyCount, initial.Count);
        foreach (EnemyAgent enemy in initial) AssertSpawn(enemy, original, spawnY, runtime.Settings);
        try
        {
            // Isolate the spawner while the game remains Ready: no AI motion or combat.
            runtime.ApplyState(GameState.Battle, state.Tick);
            controller.enabled = false;
            player.transform.position = original + new Vector3(30f, 80f, -20f);
            runtime.UpdateSpawning(runtime.Settings.SpawnInterval, state.Tick);
            int added = 0;
            foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            {
                if (initial.Contains(enemy)) continue;
                added++;
                AssertSpawn(enemy, player.transform.position, spawnY, runtime.Settings);
                Assert.IsTrue(enemy.Body.isKinematic);
                Assert.Less(Vector3.Distance(enemy.Body.position, enemy.transform.position), 0.001f);
                var skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.IsNotNull(skin);
                Vector3 visualOffset = skin.bounds.center - enemy.transform.position;
                visualOffset.y = 0f;
                Assert.Less(visualOffset.magnitude, 2f, "The new model must remain near its spawn root.");
            }
            Assert.AreEqual(runtime.Settings.SpawnCountPerWave, added);
            yield return null;
            yield return null;
            foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
                Assert.AreEqual(spawnY, enemy.transform.position.y, 0.001f, "Animator/Physics must not relocate a newly-spawned root.");

            // Reuse a body whose old physics pose was high above the arena.
            EnemyAgent reused = null;
            foreach (EnemyAgent enemy in initial) { reused = enemy; break; }
            Assert.IsTrue(runtime.TryApplyHit(reused, Vector3.up, 1f, state.Tick));
            reused.Body.position = original + Vector3.up * 100f;
            runtime.UpdateActions(runtime.Settings.KnockbackFallbackDuration + 0.01f, state.Tick);
            runtime.UpdateActions(runtime.Settings.DeathDuration + 0.01f, state.Tick);
            Assert.IsFalse(reused.gameObject.activeSelf);
            runtime.UpdateSpawning(runtime.Settings.SpawnInterval, state.Tick);
            Assert.IsTrue(reused.gameObject.activeSelf);
            AssertSpawn(reused, player.transform.position, spawnY, runtime.Settings);
            Assert.Less(Vector3.Distance(reused.Body.position, reused.transform.position), 0.001f);
            yield return null;
            yield return new WaitForFixedUpdate();
            AssertSpawn(reused, player.transform.position, spawnY, runtime.Settings);
        }
        finally
        {
            player.transform.position = original;
            controller.enabled = true;
            runtime.ApplyState(GameState.Ready, state.Tick);
        }
    }

    private static void AssertSpawn(EnemyAgent enemy, Vector3 center, float y, EnemySettings settings)
    {
        Vector3 delta = enemy.transform.position - center;
        delta.y = 0f;
        Assert.That(delta.magnitude, Is.InRange(settings.MinimumSpawnDistance - 0.001f, settings.MaximumSpawnDistance + 0.001f));
        Assert.AreEqual(y, enemy.transform.position.y, 0.001f);
    }
}

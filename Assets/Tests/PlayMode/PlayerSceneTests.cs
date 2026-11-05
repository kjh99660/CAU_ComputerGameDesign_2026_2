using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System.Reflection;

public sealed class PlayerSceneTests
{
    [UnityTest]
    public IEnumerator MainSceneSpawnsPlayerAtCenterAndConnectsEnemyTarget()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;

        PlayerRuntime player = Object.FindFirstObjectByType<PlayerRuntime>(FindObjectsInactive.Include);
        EnemyRuntime enemy = Object.FindFirstObjectByType<EnemyRuntime>(FindObjectsInactive.Include);
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");

        Assert.IsNotNull(player);
        Assert.IsNotNull(enemy);
        Assert.IsNotNull(spawn);
        Assert.Less(Vector3.Distance(spawn.transform.position, player.transform.position), 0.001f);
        Assert.AreSame(player.TargetTransform, enemy.TargetTransform);
        Assert.AreEqual(1, Object.FindObjectsByType<PlayerRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        Assert.IsNotNull(player.GetComponent<Animator>());
        Assert.AreEqual(2, player.GetComponentsInChildren<Animator>(true).Length);

        EnemyAgent enemyAgent = null;
        for (int frame = 0; frame < 30 && enemyAgent == null; frame++)
        {
            enemyAgent = Object.FindFirstObjectByType<EnemyAgent>(FindObjectsInactive.Include);
            if (enemyAgent == null)
                yield return null;
        }
        Assert.IsNotNull(enemyAgent);
        Animator enemyAnimator = enemyAgent.GetComponent<Animator>();
        Assert.IsNotNull(enemyAnimator);
        Assert.IsNotNull(enemyAnimator.runtimeAnimatorController);
        Assert.Greater(enemyAgent.GetComponentsInChildren<Renderer>(true).Length, 0);

        enemyAgent.RunAI(enemyAgent.transform.position + Vector3.forward * 10f,
            System.Array.Empty<EnemyAgent>(), enemy.Settings, 0.02f, enemyAgent.ActiveTick);
        enemyAnimator.Update(0f);
        Assert.AreEqual(EnemyAgentState.Chase, enemyAgent.State);
        Assert.IsTrue(enemyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Chase"));

        Assert.IsTrue(enemyAgent.ApplyHit(Vector3.back, enemy.Settings, enemyAgent.ActiveTick));
        enemyAnimator.Update(0f);
        Assert.AreEqual(EnemyAgentState.Knockback, enemyAgent.State);
        Assert.IsTrue(enemyAnimator.GetCurrentAnimatorStateInfo(0).IsName("Knockback"));
    }

    [UnityTest]
    public IEnumerator SwordAndAnimatorPauseAndResetWithRound()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        GameStateManager state = Object.FindFirstObjectByType<GameStateManager>();
        Assert.IsNotNull(state);
        float deadline = Time.realtimeSinceStartup + 20f;
        while (state.CurrentState != GameState.Ready && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(GameState.Ready, state.CurrentState);
        PlayerRuntime player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.IsNotNull(player);
        Animator animator = player.GetComponent<Animator>();
        MonoBehaviour sword = System.Array.Find(player.GetComponentsInChildren<MonoBehaviour>(true),
            c => c != null && c.GetType().Name == "SwordFollowBlend");
        Assert.IsNotNull(sword);
        FieldInfo weight = sword.GetType().GetField("handWeight");
        FieldInfo drawn = sword.GetType().GetField("drawn");

        player.ApplyState(GameState.Battle, 1);
        Assert.Greater(animator.speed, 0f);
        drawn.SetValue(sword, true);
        weight.SetValue(sword, 0.4f);
        player.ApplyState(GameState.Paused, 1);
        float animationTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        yield return null;
        yield return null;
        Assert.AreEqual(0f, animator.speed);
        Assert.AreEqual(animationTime, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 0.0001f);
        Assert.AreEqual(0.4f, (float)weight.GetValue(sword), 0.0001f);

        player.ApplyState(GameState.Finishing, 1);
        Assert.Greater(animator.speed, 0f);
        player.StopProducing();
        yield return player.ResetRound(2);
        player.ApplyState(GameState.Ready, 2);
        Assert.IsFalse((bool)drawn.GetValue(sword));
        Assert.AreEqual(0f, (float)weight.GetValue(sword));
        Assert.AreEqual(0f, animator.speed);
    }

    [UnityTest]
    public IEnumerator PooledEnemyRestoresIdleStateAndVisualPose()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);

        EnemyRuntime runtime = null;
        EnemyAgent enemy = null;
        for (int frame = 0; frame < 30 && enemy == null; frame++)
        {
            runtime = Object.FindFirstObjectByType<EnemyRuntime>(FindObjectsInactive.Include);
            enemy = Object.FindFirstObjectByType<EnemyAgent>(FindObjectsInactive.Include);
            if (enemy == null)
                yield return null;
        }

        Assert.IsNotNull(runtime);
        Assert.IsNotNull(enemy);
        Animator animator = enemy.GetComponent<Animator>();
        Transform visual = enemy.transform.Find("Visual");
        Assert.IsNotNull(animator);
        Assert.IsNotNull(visual);
        Vector3 defaultVisualScale = visual.localScale;
        Vector3 defaultRootScale = enemy.transform.localScale;

        int oldTick = enemy.ActiveTick;
        Assert.IsTrue(enemy.ApplyHit(Vector3.back, runtime.Settings, oldTick));
        enemy.RunAction(runtime.Settings, runtime.Settings.KnockbackFallbackDuration + 0.01f, oldTick);
        animator.Update(runtime.Settings.DeathDuration);
        enemy.RunAction(runtime.Settings, runtime.Settings.DeathDuration + 0.01f, oldTick);
        Assert.AreEqual(EnemyAgentState.Dying, enemy.State);
        Assert.IsTrue(enemy.WantsPoolReturn);

        GameObject poolRoot = new("EnemyPoolTest");
        enemy.DeactivateForPool(poolRoot.transform);
        enemy.Activate(new Vector3(3f, 0f, 4f), oldTick + 1);

        Assert.AreEqual(EnemyAgentState.Idle, enemy.State);
        Assert.AreEqual(oldTick + 1, enemy.ActiveTick);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        Assert.AreEqual(Vector3.zero, visual.localPosition);
        Assert.AreEqual(defaultVisualScale, visual.localScale);
        Assert.AreEqual(defaultRootScale, enemy.transform.localScale);
        Assert.Less(Quaternion.Angle(Quaternion.identity, visual.localRotation), 1.1f);
        Object.Destroy(poolRoot);
    }
}

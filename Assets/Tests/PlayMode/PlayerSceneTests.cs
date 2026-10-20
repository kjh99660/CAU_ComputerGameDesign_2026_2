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
    }

    [UnityTest]
    public IEnumerator SwordAndAnimatorPauseAndResetWithRound()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
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
}

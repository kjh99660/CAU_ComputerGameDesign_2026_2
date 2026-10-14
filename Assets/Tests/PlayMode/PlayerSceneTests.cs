using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
    }
}

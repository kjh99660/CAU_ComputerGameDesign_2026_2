using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class EnemySkeletalSceneTests
{
    [UnityTest]
    public IEnumerator SkeletalChasePausesResumesAndResetsAfterDeath()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity");
        EnemyRuntime runtime = null;
        EnemyAgent enemy = null;
        for (int frame = 0; frame < 60 && enemy == null; frame++)
        {
            yield return null;
            runtime = Object.FindFirstObjectByType<EnemyRuntime>();
            enemy = Object.FindFirstObjectByType<EnemyAgent>();
        }
        Assert.IsNotNull(enemy);
        var skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
        Assert.IsNotNull(skin);
        var animator = enemy.GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // Test outside Game camera visibility.
        Transform leg = skin.bones.First(b => b.name == "mixamorig:LeftUpLeg");
        int tick = enemy.ActiveTick;
        enemy.SetIdle(tick);
        animator.Update(0f);
        Quaternion idle = leg.localRotation;
        runtime.ApplyState(GameState.Battle, tick);
        enemy.RunAI(enemy.transform.position + Vector3.forward * 10f, System.Array.Empty<EnemyAgent>(), runtime.Settings, 0f, tick);
        animator.Update(0f); // Evaluate Animator.Play before advancing its newly-entered state.
        animator.Update(0.16f);
        Assert.Greater(Quaternion.Angle(idle, leg.localRotation), 5f);
        runtime.ApplyState(GameState.Paused, tick);
        Quaternion paused = leg.localRotation;
        yield return null;
        yield return null;
        Assert.Less(Quaternion.Angle(paused, leg.localRotation), 0.05f);
        runtime.ApplyState(GameState.Finishing, tick);
        animator.Update(0.2f);
        Assert.Greater(Quaternion.Angle(paused, leg.localRotation), 5f);
        Assert.IsTrue(enemy.ApplyHit(Vector3.zero, runtime.Settings, tick));
        enemy.RunAction(runtime.Settings, runtime.Settings.KnockbackFallbackDuration + 0.01f, tick);
        animator.Update(runtime.Settings.DeathDuration);
        var pool = new GameObject("SkeletalPoolTest");
        enemy.DeactivateForPool(pool.transform);
        enemy.Activate(Vector3.zero, tick + 1);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        Assert.Less(Quaternion.Angle(idle, leg.localRotation), 0.1f);
        Object.Destroy(pool);
    }
}

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
        yield return TestScenes.Load(TestScenes.Arena);
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
        // Each enemy starts its gait at its own phase, so look at the whole swing instead of one instant.
        Vector3 target = enemy.transform.position + Vector3.forward * 30f;
        float swing = 0f;
        for (int i = 0; i < 60; i++)
        {
            enemy.RunAI(target, System.Array.Empty<EnemyAgent>(), runtime.Settings, 1f / 60f, tick);
            animator.Update(1f / 60f);
            swing = Mathf.Max(swing, Quaternion.Angle(idle, leg.localRotation));
        }
        Assert.Greater(swing, 15f, "Chasing must swing the legs.");
        Assert.IsTrue(enemy.ShowsLocomotion);
        runtime.ApplyState(GameState.Paused, tick);
        Quaternion paused = leg.localRotation;
        yield return null;
        yield return null;
        Assert.Less(Quaternion.Angle(paused, leg.localRotation), 0.05f);
        // Finishing stops new movement: the enemy settles into its stance instead of running on the spot.
        runtime.ApplyState(GameState.Finishing, tick);
        Assert.Greater(animator.speed, 0f);
        Assert.IsFalse(enemy.ShowsLocomotion);
        animator.Update(0.6f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        Assert.Less(Quaternion.Angle(idle, leg.localRotation), 8f);
        Assert.IsTrue(enemy.ApplyHit(Vector3.zero, runtime.Settings, tick));
        enemy.RunAction(runtime.Settings, runtime.Settings.KnockbackFallbackDuration + 0.01f, tick);
        animator.Update(runtime.Settings.DeathDuration);
        var pool = new GameObject("SkeletalPoolTest");
        enemy.DeactivateForPool(pool.transform);
        enemy.Activate(Vector3.zero, tick + 1);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        // Back in the stance (its phase differs per enemy), with nothing left of the death pose.
        Assert.Less(Quaternion.Angle(idle, leg.localRotation), 8f);
        Object.Destroy(pool);
    }
}

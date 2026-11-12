using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class CombatVfxTests
{
    [UnityTest]
    public IEnumerator SlashUsesFacingPausesReusesAndClearsOnReset()
    {
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.IsNotNull(settings);
        Assert.IsNotNull(settings.slashPrefab);
        Assert.IsNotNull(settings.impactPrefab);
        var root = new GameObject("VfxTest");
        var player = new GameObject("VfxTestPlayer");
        var vfx = root.AddComponent<CombatVfxRuntime>();
        vfx.Configure(null, settings);
        try
        {
            player.transform.SetPositionAndRotation(new Vector3(5f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));
            vfx.ApplyState(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 7));
            vfx.PlaySlash(player.transform, 1f, 6);
            Assert.AreEqual(0, root.transform.childCount, "Stale attacks must not produce effects.");
            vfx.PlaySlash(player.transform, 1f, 7);
            Assert.AreEqual(1, root.transform.childCount);
            var effect = root.transform.GetChild(0);
            Assert.Less(Vector3.Distance(player.transform.position + player.transform.rotation * settings.slashOffset, effect.position), 0.001f);
            Assert.Less(Quaternion.Angle(player.transform.rotation * Quaternion.Euler(settings.slashEulerOffset), effect.rotation), 0.01f);
            yield return null;
            vfx.ApplyState(new GameStateChangedEvent(GameState.Battle, GameState.Paused, 7));
            var particle = effect.GetComponent<ParticleSystem>();
            float pausedTime = particle.time;
            yield return null;
            yield return null;
            Assert.IsTrue(effect.gameObject.activeSelf);
            Assert.IsTrue(particle.isPaused);
            Assert.AreEqual(pausedTime, particle.time);
            vfx.ApplyState(new GameStateChangedEvent(GameState.Paused, GameState.Battle, 7));
            Assert.IsTrue(particle.isPlaying);
            vfx.StopProducing();
            yield return vfx.ResetRound(8);
            Assert.IsFalse(effect.gameObject.activeSelf);
            vfx.ApplyState(new GameStateChangedEvent(GameState.Paused, GameState.Ready, 8));
            vfx.ApplyState(new GameStateChangedEvent(GameState.Countdown, GameState.Battle, 8));
            vfx.PlaySlash(player.transform, 1f, 8);
            Assert.AreEqual(1, root.transform.childCount, "Reset should reuse the pooled instance.");
            vfx.ApplyState(new GameStateChangedEvent(GameState.Finishing, GameState.OutroDialogue, 8));
            Assert.IsFalse(effect.gameObject.activeSelf);
        }
        finally
        {
            Object.Destroy(root);
            Object.Destroy(player);
        }
    }

    [UnityTest]
    public IEnumerator AcceptedEnemyHitCreatesOneImpactAtTarget()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity");
        yield return null;
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        yield return WaitFor(state, GameState.Ready);
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        var vfx = Object.FindFirstObjectByType<CombatVfxRuntime>();
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Vector3 position = enemy.transform.position + settings.impactOffset;
        Assert.IsTrue(enemies.TryApplyHit(enemy, Vector3.forward, 1f, state.Tick));
        Transform impact = vfx.transform.Find("Impact(Clone)");
        Assert.IsNotNull(impact);
        Assert.IsTrue(impact.gameObject.activeSelf);
        Assert.Less(Vector3.Distance(position, impact.position), 0.001f);
        Assert.IsFalse(enemies.TryApplyHit(enemy, Vector3.forward, 1f, state.Tick));
        int impacts = 0;
        foreach (Transform child in vfx.transform)
            if (child.name == "Impact(Clone)") impacts++;
        Assert.AreEqual(1, impacts, "Count only Impact instances; the shared bootstrap also owns other pools.");
        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        Assert.IsFalse(impact.gameObject.activeSelf);
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected)
    {
        for (int i = 0; i < 120 && state.CurrentState != expected; i++) yield return null;
        Assert.AreEqual(expected, state.CurrentState);
    }
}

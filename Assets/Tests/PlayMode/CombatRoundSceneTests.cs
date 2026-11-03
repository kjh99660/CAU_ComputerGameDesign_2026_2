using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class CombatRoundSceneTests
{
    [UnityTest]
    public IEnumerator TimerFinishingAcceptsExistingResultsThenClosesAndRestoresTime()
    {
        var settings = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(settings);
        float duration = settings.roundDuration;
        float finishing = settings.finishingDuration;
        try
        {
            settings.roundDuration = 0.5f;
            settings.finishingDuration = 0.3f;
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
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
            yield return WaitFor(state, GameState.Finishing);
            Assert.AreEqual(0.5f, Time.timeScale);
            var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
            var enemy = Object.FindFirstObjectByType<EnemyAgent>();
            var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
            Assert.IsTrue(runtime.TryApplyHit(enemy, Vector3.forward, 1f, state.Tick));
            Assert.AreEqual(110, combat.Stats.Score);
            yield return WaitFor(state, GameState.OutroDialogue);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(combat.Stats.RecordEnemyKill(state.Tick));
            Assert.AreEqual(110, combat.Stats.Score);
        }
        finally
        {
            settings.roundDuration = duration;
            settings.finishingDuration = finishing;
            Time.timeScale = 1f;
        }
    }

    [UnityTest]
    public IEnumerator AcceptedHitUpdatesHudAndRetryClearsRound()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        Assert.IsNotNull(state);
        Assert.IsNotNull(events);
        yield return WaitFor(state, GameState.Ready);
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);

        var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var battle = Object.FindFirstObjectByType<BattleView>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.AreEqual(100f, combat.Settings.roundDuration);
        Assert.AreEqual(2.5f, combat.Settings.finishingDuration);
        Assert.IsNotNull(enemy);
        Assert.IsNotNull(player);
        enemy.transform.position = player.transform.position + player.transform.forward * combat.Settings.attackForwardOffset + Vector3.up * combat.Settings.attackHeight;
        Physics.SyncTransforms();
        var animator = player.GetComponent<Animator>();
        animator.Play("Attack1", 0, 0.25f);
        animator.Update(0f);
        float hitDeadline = Time.realtimeSinceStartup + 4f;
        while (combat.Stats.KillCount == 0 && Time.realtimeSinceStartup < hitDeadline)
            yield return null;
        Assert.AreEqual(1, combat.Stats.KillCount, "The active attack should hit the nearby enemy.");
        Assert.IsFalse(runtime.TryApplyHit(enemy, Vector3.forward, 1f, state.Tick));
        Assert.AreEqual(110, combat.Stats.Score);
        Assert.AreEqual("000110", Find<TMP_Text>(battle.transform, "ScoreValue").text);
        var fill = Find<Image>(battle.transform, "SpecialGaugeFill");
        Assert.IsNotNull(fill.sprite);
        Assert.AreEqual(0.08f, fill.fillAmount, 0.0001f);
        var percent = Find<TMP_Text>(battle.transform, "SpecialGaugePercent");
        Assert.AreEqual("8%", percent.text);
        battle.RenderGauge(1f);
        Assert.AreEqual("100%", percent.text);
        Assert.Greater(fill.color.r, fill.color.b);

        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        Assert.AreEqual(0, combat.Stats.Score);
        Assert.AreEqual(0f, combat.Stats.Gauge);
        Assert.AreEqual(0, combat.Stats.KillCount);
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, state.CurrentState);
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }
}

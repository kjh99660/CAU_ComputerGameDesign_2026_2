using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using System.Reflection;

public sealed class CombatRoundSceneTests
{
    [UnityTest]
    public IEnumerator DialogueAdvancesSkipsAndRestartsAfterRetry()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        Assert.IsNotNull(data);
        Assert.GreaterOrEqual(data.intro.Length, 2);
        Assert.GreaterOrEqual(data.outro.Length, 1);
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        yield return WaitFor(state, GameState.Ready);
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        var view = Object.FindFirstObjectByType<DialogueView>();
        var text = Find<TMP_Text>(view.transform, "BodyText");
        var next = Find<Button>(view.transform, "DialogueBox");
        Assert.AreEqual(data.intro[0].text, text.text);
        Assert.AreEqual(data.intro[0].speaker, Find<TMP_Text>(view.transform, "SpeakerName").text);
        next.onClick.Invoke();
        Assert.AreEqual(data.intro[1].text, text.text);
        Assert.AreEqual(data.intro[1].speaker, Find<TMP_Text>(view.transform, "SpeakerName").text);
        next.onClick.Invoke();
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        Assert.AreEqual(data.intro[0].text, text.text);
        Find<Button>(view.transform, "SkipButton").onClick.Invoke();
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        events.EnqueueRequest(new TimerReachedZeroRequest(state.Tick));
        yield return WaitFor(state, GameState.Finishing);
        events.EnqueueRequest(new FinishingCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.OutroDialogue);
        Assert.AreEqual(data.outro[0].text, text.text);
        next.onClick.Invoke();
        yield return WaitFor(state, GameState.Result);
        Assert.AreEqual(1f, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator EscapePausesCancelsRetryConfirmationAndResumes()
    {
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
        var bootstrap = Object.FindFirstObjectByType<CoreBootstrap>();
        var layer = (UiLayer)typeof(CoreBootstrap).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
        PressEscape(layer);
        yield return WaitFor(state, GameState.Paused);

        var pause = Object.FindFirstObjectByType<PauseView>();
        Find<Button>(pause.transform, "RetryButton").onClick.Invoke();
        var confirmation = Object.FindFirstObjectByType<RetryConfirmationView>(FindObjectsInactive.Include);
        Assert.IsTrue(confirmation.gameObject.activeSelf);
        PressEscape(layer);
        yield return null;
        Assert.AreEqual(GameState.Paused, state.CurrentState);
        Assert.IsFalse(confirmation.gameObject.activeSelf);
        PressEscape(layer);
        yield return WaitFor(state, GameState.Battle);
    }

    private static void PressEscape(UiLayer layer)
    {
        typeof(UiLayer).GetMethod("HandleEscape", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);
    }

    [UnityTest]
    public IEnumerator FullGaugeSpecialHitsNearbyEnemyAndUpdatesResult()
    {
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
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        enemy.transform.position = player.transform.position + Vector3.forward;
        Physics.SyncTransforms();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        enemies.UpdateAI(0f, state.Tick); // Update the grid after positioning the test enemy.
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(state.Tick);
        Assert.AreEqual(100f, combat.Stats.Gauge);
        int previousKills = combat.Stats.KillCount;
        Assert.IsTrue(combat.TryUseSpecial(state.Tick, player.transform.position));
        Assert.AreEqual(1, combat.Stats.SpecialUseCount);
        Assert.AreEqual(0f, combat.Stats.Gauge);
        Assert.Greater(combat.Stats.KillCount, previousKills);
        Assert.IsFalse(combat.TryUseSpecial(state.Tick, player.transform.position));
        var ring = GameObject.Find("SpecialShockwave").GetComponent<LineRenderer>();
        Assert.IsTrue(ring.enabled);
        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        Vector3 pausedScale = ring.transform.localScale;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.AreEqual(pausedScale, ring.transform.localScale);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        Assert.IsFalse(ring.enabled);
    }

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
        var scoreText = Find<TMP_Text>(battle.transform, "ScoreValue");
        Assert.Greater(scoreText.color.g, scoreText.color.r);
        var fill = Find<Image>(battle.transform, "SpecialGaugeFill");
        Assert.IsNotNull(fill.sprite);
        Assert.AreEqual(0.08f, fill.fillAmount, 0.0001f);
        var percent = Find<TMP_Text>(battle.transform, "SpecialGaugePercent");
        Assert.AreEqual("8%", percent.text);
        battle.RenderGauge(1f);
        Assert.AreEqual("100%", percent.text);
        Assert.Greater(fill.color.r, fill.color.b);

        EnemyAgent attacker = null;
        foreach (var candidate in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (candidate != enemy) { attacker = candidate; break; }
        Assert.IsNotNull(attacker);
        attacker.transform.position = player.transform.position + Vector3.forward * 0.5f;
        Physics.SyncTransforms();
        runtime.UpdateAI(0f, state.Tick);
        runtime.UpdateActions(runtime.Settings.AttackDuration, state.Tick);
        Assert.AreEqual(1, combat.Stats.HitCount);
        Assert.AreEqual(99, combat.Stats.Score);
        Assert.Greater(scoreText.color.r, scoreText.color.g);
        Image hitFlash = Find<Image>(battle.transform, "PlayerHitFlash");
        Assert.Greater(hitFlash.color.a, 0f);

        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        float pausedAlpha = hitFlash.color.a;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.AreEqual(pausedAlpha, hitFlash.color.a, 0.0001f);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);
        Assert.AreEqual(0, combat.Stats.Score);
        Assert.AreEqual(0f, combat.Stats.Gauge);
        Assert.AreEqual(0, combat.Stats.KillCount);
        Assert.AreEqual(0f, hitFlash.color.a, 0.0001f);
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

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
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        yield return WaitFor(state, GameState.Ready);
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        var view = Object.FindFirstObjectByType<DialogueView>();
        var text = Find<TMP_Text>(view.transform, "BodyText");
        var speaker = Find<TMP_Text>(view.transform, "SpeakerName");
        Assert.AreSame(text.font, speaker.font);
        Assert.AreEqual(AtlasPopulationMode.Dynamic, text.font.atlasPopulationMode);
        Assert.IsTrue(text.font.HasCharacter('한', false, true));
        Assert.IsTrue(text.font.HasCharacter('글', false, true));
        var dialogueFont = text.font;
        var box = Find<Button>(view.transform, "DialogueBox");
        // 화면 아무 곳 클릭은 다른 대화 요소 뒤의 투명한 전체 화면 버튼이 받는다.
        var advanceArea = Find<Button>(view.transform, "AdvanceArea");
        Assert.AreEqual(0, advanceArea.transform.GetSiblingIndex());

        // 노아(왼쪽)·K-7(오른쪽) 초상화가 있고, 클릭을 가로채지 않으며, 첫 화자가 바로 강조된다.
        var leftPortrait = Find<Image>(view.transform, "LeftPortrait");
        var rightPortrait = Find<Image>(view.transform, "RightPortrait");
        Assert.IsNotNull(leftPortrait.sprite);
        Assert.IsNotNull(rightPortrait.sprite);
        Assert.IsFalse(leftPortrait.raycastTarget || rightPortrait.raycastTarget, "Portraits must not block dialogue clicks.");
        AssertSpeakerEmphasis(leftPortrait, rightPortrait, data.intro[0].leftSpeaker);
        var nextIndicator = Find<Image>(view.transform, "NextIndicator");
        Assert.IsFalse(nextIndicator.raycastTarget);

        // 대화가 열린 직후의 입력은 무시한다(전투 끝까지 누르던 클릭이 첫 대사를 넘기지 않게).
        advanceArea.onClick.Invoke();
        Assert.AreEqual(data.intro[0].text, text.text);
        Assert.Less(text.maxVisibleCharacters, data.intro[0].text.Length, "Input right after the dialogue opens must be ignored.");

        // 첫 줄은 한 번에 나타나지 않고 타이핑된다. 타이핑은 unscaled time이라 timeScale이 0이어도 진행한다.
        int typedBefore = text.maxVisibleCharacters;
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        try { yield return new WaitForSecondsRealtime(0.2f); }
        finally { Time.timeScale = previousTimeScale; }
        Assert.Greater(text.maxVisibleCharacters, typedBefore, "Dialogue typing must use unscaled time.");
        yield return new WaitForSecondsRealtime(0.25f);

        AdvanceLine(advanceArea, text, speaker, data.intro[0], nextIndicator);
        // 화자가 바뀌면 강조가 0.15초 동안 다음 화자 쪽으로 옮겨 간다.
        yield return new WaitForSecondsRealtime(0.3f);
        AssertSpeakerEmphasis(leftPortrait, rightPortrait, data.intro[1].leftSpeaker);
        for (int i = 1; i < data.intro.Length; i++)
            AdvanceLine(advanceArea, text, speaker, data.intro[i], nextIndicator);
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
        Assert.Less(text.maxVisibleCharacters, data.intro[0].text.Length, "Retry must restart typing from the first line.");
        AssertSpeakerEmphasis(leftPortrait, rightPortrait, data.intro[0].leftSpeaker);
        Assert.AreSame(dialogueFont, text.font);
        Find<Button>(view.transform, "SkipButton").onClick.Invoke();
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        events.EnqueueRequest(new TimerReachedZeroRequest(state.Tick));
        yield return WaitFor(state, GameState.Finishing);
        events.EnqueueRequest(new FinishingCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.OutroDialogue);
        yield return new WaitForSecondsRealtime(0.4f);
        foreach (DialogueLine line in data.outro)
            AdvanceLine(box, text, speaker, line, nextIndicator);
        yield return WaitFor(state, GameState.Result);
        Assert.AreEqual(1f, Time.timeScale);
    }

    // 말하는 쪽 초상화가 듣는 쪽보다 밝고 크다.
    private static void AssertSpeakerEmphasis(Image left, Image right, bool leftSpeaking)
    {
        Image speaking = leftSpeaking ? left : right;
        Image listening = leftSpeaking ? right : left;
        Assert.Greater(speaking.color.r, listening.color.r + 0.2f, "The speaking portrait must be brighter than the listener.");
        Assert.Greater(speaking.rectTransform.localScale.x, listening.rectTransform.localScale.x);
    }

    // 타이핑 중인 줄은 첫 입력에 완성되고, 다 보인 줄은 다음 입력에 다음 줄(또는 대화 완료)로 넘어간다.
    // 넘김 표시는 타이핑 중에는 숨고, 줄이 다 보이면 나타난다.
    private static void AdvanceLine(Button next, TMP_Text text, TMP_Text speaker, DialogueLine line, Graphic nextIndicator)
    {
        Assert.AreEqual(line.text, text.text);
        Assert.AreEqual(line.speaker, speaker.text);
        if (text.maxVisibleCharacters < line.text.Length)
        {
            Assert.IsFalse(nextIndicator.enabled, "The next indicator must stay hidden while the line is typing.");
            next.onClick.Invoke();
            Assert.AreEqual(line.text, text.text, "The first input must finish the typing line, not skip it.");
            Assert.GreaterOrEqual(text.maxVisibleCharacters, line.text.Length);
        }
        Assert.IsTrue(nextIndicator.enabled, "The next indicator must appear once the line is complete.");
        next.onClick.Invoke();
    }

    [UnityTest]
    public IEnumerator EscapePausesCancelsRetryConfirmationAndResumes()
    {
        yield return TestScenes.Load(TestScenes.Arena);
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
        yield return TestScenes.Load(TestScenes.Arena);
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
            yield return TestScenes.Load(TestScenes.Arena);
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
            // One kill in the game's settings: hit 15 + kill 150 (the 2026-11-25 values, 50% above 10 + 100).
            Assert.AreEqual(165, combat.Stats.Score);
            yield return WaitFor(state, GameState.OutroDialogue);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(combat.Stats.RecordEnemyKill(state.Tick));
            Assert.AreEqual(165, combat.Stats.Score);
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
        yield return TestScenes.Load(TestScenes.Arena);
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
        // Keep unrelated enemies away from this animation-event hit and its lunge.
        foreach (var candidate in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            candidate.transform.position = player.transform.position + Vector3.right * 100f;
        enemy.transform.position = player.transform.position + player.transform.forward * 4.5f;
        Assert.IsTrue((bool)typeof(PlayerRuntime).GetField("_hasAttackHitSource",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player),
            "The prefab hitbox must replace the provisional runtime attack sphere.");
        Physics.SyncTransforms();
        var animator = player.GetComponent<Animator>();
        animator.Play("Attack1", 0, 0f);
        animator.Update(0f);
        float hitDeadline = Time.realtimeSinceStartup + 4f;
        while (combat.Stats.KillCount == 0 && Time.realtimeSinceStartup < hitDeadline)
            yield return null;
        Assert.AreEqual(1, combat.Stats.KillCount, "The active attack should hit the nearby enemy.");
        Assert.IsFalse(runtime.TryApplyHit(enemy, Vector3.forward, 1f, state.Tick));
        Assert.AreEqual(165, combat.Stats.Score);
        Assert.AreEqual("000165", Find<TMP_Text>(battle.transform, "ScoreValue").text);
        var scoreText = Find<TMP_Text>(battle.transform, "ScoreValue");
        Assert.Greater(scoreText.color.g, scoreText.color.r);
        var fill = Find<Image>(battle.transform, "SpecialGaugeFill");
        Assert.IsNotNull(fill.sprite);
        Assert.AreEqual(0.08f, fill.fillAmount, 0.0001f);
        var percent = Find<TMP_Text>(battle.transform, "SpecialGaugePercent");
        Assert.AreEqual("8%", percent.text);
        battle.RenderGauge(1f);
        Assert.AreEqual("100%", percent.text);
        // 가득 찬 게이지는 팔레트의 강조색(청록)으로 바뀐다.
        Assert.AreEqual(UiPalette.Accent, fill.color);

        EnemyAgent attacker = null;
        foreach (var candidate in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (candidate != enemy) { attacker = candidate; break; }
        Assert.IsNotNull(attacker);
        attacker.transform.position = player.transform.position + Vector3.forward * 0.5f;
        // Face the player, as an enemy that walked up to it does. Whichever enemy the unsorted search returns still has
        // the heading it walked in with from the far side, and a Rusher swings before it turns; the single step past the
        // wind-up below skips the wind-up's own turn, so its strike would miss.
        attacker.Face(player.transform.position);
        Physics.SyncTransforms();
        // An enemy attacks when its turn comes (it may hover a moment first), and its strike lands when the
        // telegraphed wind-up ends.
        for (int i = 0; i < 200 && attacker.State != EnemyAgentState.Attack; i++)
            runtime.UpdateAI(0.05f, state.Tick);
        Assert.AreEqual(EnemyAgentState.Attack, attacker.State);
        runtime.UpdateActions(runtime.Settings.AttackWindup, state.Tick);
        Assert.AreEqual(1, combat.Stats.HitCount);
        Assert.AreEqual(149, combat.Stats.Score, "A hit on the player takes 10% of the score, rounded down (165 - 16).");
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

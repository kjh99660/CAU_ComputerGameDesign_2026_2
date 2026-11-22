using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 화면 연출(카운트다운, HUD 강조, 결과 숫자 올리기)이 실제 Scene의 UI에 연결되어 있고,
// unscaled time으로 재생되며, Pause에서 멈추고 Retry 뒤에 남지 않는지 확인한다.
public sealed class UiEffectsSceneTests
{
    private GameStateManager _state;
    private EventManager _events;

    [UnityTest]
    public IEnumerator CountdownAnimatesOnUnscaledTimeAndRestartsAfterRetry()
    {
        yield return LoadMainAndWaitReady();
        yield return EnterCountdown();

        var view = Object.FindFirstObjectByType<CountdownView>();
        var number = Find<TMP_Text>(view.transform, "Number");
        var ready = Find<TMP_Text>(view.transform, "ReadyText");
        var frame = Find<Image>(view.transform, "NumberFrame");
        var flash = Find<Image>(view.transform, "StartFlash");
        Assert.AreEqual(0, flash.transform.GetSiblingIndex(), "The flash must sit behind the countdown elements.");
        Assert.IsFalse(flash.raycastTarget, "The flash must not block input.");
        Assert.AreEqual("3", number.text);

        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        try
        {
            // timeScale이 0이어도 3 → 2 → 1로 넘어가고, 그동안 숫자의 크기와 불투명도가 움직인다.
            float minAlpha = 1f, maxScale = 0f;
            bool sawTwo = false;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (number.text != "1" && Time.realtimeSinceStartup < deadline)
            {
                sawTwo |= number.text == "2";
                minAlpha = Mathf.Min(minAlpha, number.canvasRenderer.GetAlpha());
                maxScale = Mathf.Max(maxScale, number.rectTransform.localScale.x);
                yield return null;
            }
            Assert.AreEqual("1", number.text, "Countdown must advance on unscaled time.");
            Assert.IsTrue(sawTwo, "Countdown must show 2 between 3 and 1.");
            Assert.Less(minAlpha, 0.9f, "The number must fade within a step.");
            Assert.Greater(maxScale, 1.02f, "The number must pop in larger than its rest size.");

            // 같은 프레임 안에서 자세를 직접 확인한다(다음 프레임에는 Presenter가 실제 시간으로 다시 그린다).
            view.AnimateNumber(0f);
            Assert.AreEqual(0f, number.canvasRenderer.GetAlpha(), 1e-4f);
            Assert.Greater(number.rectTransform.localScale.x, 1.2f);
            view.AnimateNumber(0.4f);
            Assert.AreEqual(1f, number.rectTransform.localScale.x, 1e-4f);
            Assert.AreEqual(1f, number.canvasRenderer.GetAlpha(), 1e-4f);
            Assert.IsFalse(flash.enabled);
            view.AnimateNumber(view.StepDuration);
            Assert.Less(number.canvasRenderer.GetAlpha(), 0.5f);
            Assert.Less(number.rectTransform.localScale.x, 1f);

            view.AnimateStart(0f);
            Assert.IsTrue(flash.enabled, "START must flash.");
            Assert.Greater(flash.canvasRenderer.GetAlpha(), 0.01f);
            Assert.Less(flash.canvasRenderer.GetAlpha(), 0.31f, "The flash stays a tint, not a white-out.");
            Assert.Greater(ready.rectTransform.localScale.x, 1.2f);
            view.AnimateStart(view.StartDuration);
            Assert.IsFalse(flash.enabled, "The flash must be over before the countdown ends.");
            Assert.AreEqual(1f, ready.rectTransform.localScale.x, 1e-4f);
            Assert.AreEqual(0f, frame.canvasRenderer.GetAlpha(), 1e-3f);

            // 연출이 끝나면 timeScale과 무관하게 완료 요청이 나가 Battle로 넘어간다.
            yield return WaitFor(GameState.Battle);
        }
        finally
        {
            Time.timeScale = previousTimeScale;
        }

        // Retry 뒤의 카운트다운은 3부터 다시 시작하고, 이전 START 자세(사라진 틀, 번쩍임)가 남지 않는다.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        yield return EnterCountdown();
        Assert.AreEqual("3", number.text);
        Assert.AreEqual("READY", ready.text);
        Assert.AreEqual(1f, frame.canvasRenderer.GetAlpha(), 1e-4f);
        Assert.AreEqual(1f, ready.rectTransform.localScale.x, 1e-4f);
        Assert.IsFalse(flash.enabled);
        Assert.AreEqual(1f, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator TimerWarnsInTheLastTenSecondsAndFullGaugePulsesUntilUsed()
    {
        yield return LoadMainAndWaitReady();
        yield return EnterCountdown();
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        var battle = Object.FindFirstObjectByType<BattleView>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var timer = Find<TMP_Text>(battle.transform, "TimerValue");
        var gauge = Find<RectTransform>(battle.transform, "SpecialGaugeFrame");
        Color normal = timer.color;
        Assert.Greater(normal.g, 0.5f, "The timer starts in the normal (not alert) color.");

        // 표시되는 초만 같은 프레임 안에서 바꿔 본다(다음 프레임에는 실제 남은 시간이 다시 그려진다).
        battle.RenderTimer(10.4f);
        Assert.AreEqual("00:11", timer.text);
        AssertColor(normal, timer.color);
        battle.RenderTimer(10f);
        Assert.AreEqual("00:10", timer.text);
        AssertColor(UiPalette.Alert, timer.color);
        battle.UpdateHud(0f);
        Assert.Greater(timer.rectTransform.localScale.x, 1.05f, "Each warning second starts with a tick.");
        battle.UpdateHud(1f);
        Assert.AreEqual(1f, timer.rectTransform.localScale.x, 1e-4f);
        battle.RenderTimer(57f);
        AssertColor(normal, timer.color);
        Assert.AreEqual(1f, timer.rectTransform.localScale.x, 1e-4f);

        // 게이지가 가득 차면 틀이 맥동하고, Pause에서는 멈춘다.
        Assert.AreEqual(1f, gauge.localScale.x, 1e-4f);
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.AreEqual(100f, combat.Stats.Gauge);
        float maxScale = 1f;
        float until = Time.realtimeSinceStartup + 1.2f;
        while (Time.realtimeSinceStartup < until)
        {
            maxScale = Mathf.Max(maxScale, gauge.localScale.x);
            yield return null;
        }
        Assert.Greater(maxScale, 1.03f, "A full gauge must pulse.");
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedScale = gauge.localScale.x;
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.AreEqual(pausedScale, gauge.localScale.x, "The pulse must freeze while paused.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        // 특수 공격으로 게이지를 쓰면 바로 원래 크기로 돌아간다.
        Assert.IsTrue(combat.TryUseSpecial(_state.Tick, player.transform.position));
        Assert.AreEqual(0f, combat.Stats.Gauge);
        Assert.AreEqual(1f, gauge.localScale.x, 1e-4f);
        yield return null;
        Assert.AreEqual(1f, gauge.localScale.x, 1e-4f);

        // 가득 찬 채로 Retry해도 맥동이 남지 않는다.
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        yield return new WaitForSecondsRealtime(0.2f);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0f, combat.Stats.Gauge);
        Assert.AreEqual(1f, gauge.localScale.x, 1e-4f);
    }

    [UnityTest]
    public IEnumerator TimerWarningFollowsTheRealClockAndClearsOnRetry()
    {
        var settings = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(settings);
        float duration = settings.roundDuration;
        try
        {
            // 12초 라운드: 처음에는 보통 색이고, 실제 시계가 10초에 닿으면 경고색으로 바뀐다.
            settings.roundDuration = 12f;
            yield return LoadMainAndWaitReady();
            yield return EnterCountdown();
            _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
            yield return WaitFor(GameState.Battle);

            var battle = Object.FindFirstObjectByType<BattleView>();
            var timer = Find<TMP_Text>(battle.transform, "TimerValue");
            Assert.AreEqual("00:12", timer.text);
            Color normal = timer.color;
            Assert.Greater(normal.g, 0.5f, "Twelve seconds left is not a warning yet.");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (timer.text != "00:10" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("00:10", timer.text);
            AssertColor(UiPalette.Alert, timer.color);

            // Pause 동안에도 경고색은 그대로이고, 새 라운드가 100초로 시작하면 보통 색으로 돌아간다.
            _events.EnqueueRequest(new PauseRequest(_state.Tick));
            yield return WaitFor(GameState.Paused);
            AssertColor(UiPalette.Alert, timer.color);
            settings.roundDuration = 100f;
            _events.EnqueueRequest(new RestartRequest(_state.Tick));
            yield return WaitFor(GameState.Ready);
            Assert.AreEqual("01:40", timer.text);
            AssertColor(normal, timer.color);
            Assert.AreEqual(1f, timer.rectTransform.localScale.x, 1e-4f);
        }
        finally
        {
            settings.roundDuration = duration;
        }
    }

    [UnityTest]
    public IEnumerator ResultCountsUpOnUnscaledTimeAndRetryClearsIt()
    {
        yield return LoadMainAndWaitReady();
        yield return EnterCountdown();
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        for (int i = 0; i < 6; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);

        var result = Object.FindFirstObjectByType<ResultView>();
        var scoreText = Find<TMP_Text>(result.transform, "FinalScoreValue");
        var killsText = Find<TMP_Text>(result.transform, "KillsValue");
        var comboText = Find<TMP_Text>(result.transform, "MaxComboValue");
        var hitsText = Find<TMP_Text>(result.transform, "HitsTakenValue");
        var specialText = Find<TMP_Text>(result.transform, "SpecialUsesValue");
        var retry = Find<Button>(result.transform, "RetryButton");
        int finalScore = combat.Stats.Score;
        Assert.Greater(finalScore, 0);
        Assert.GreaterOrEqual(combat.Stats.KillCount, 6);

        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        try
        {
            // timeScale이 0이어도 0부터 최종값까지 오른다. 도중의 값은 최종값보다 작고 줄어들지 않는다.
            bool sawPartial = false;
            int previous = 0;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (scoreText.text != finalScore.ToString("D6") && Time.realtimeSinceStartup < deadline)
            {
                int shown = int.Parse(scoreText.text);
                Assert.GreaterOrEqual(shown, previous, "The score must not count down.");
                Assert.Less(shown, finalScore);
                previous = shown;
                sawPartial = true;
                yield return null;
            }
            Assert.IsTrue(sawPartial, "The final score must count up instead of appearing at once.");
            Assert.AreEqual(finalScore.ToString("D6"), scoreText.text, "Count-up must use unscaled time.");
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(combat.Stats.KillCount.ToString("D3"), killsText.text);
            Assert.AreEqual(combat.Stats.MaxCombo.ToString("D2"), comboText.text);
            Assert.AreEqual(combat.Stats.HitCount.ToString("D2"), hitsText.text);
            Assert.AreEqual(combat.Stats.SpecialUseCount.ToString("D2"), specialText.text);

            // 숫자가 다 오르면 Retry 버튼이 맥동한다.
            float maxScale = 1f;
            float until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                maxScale = Mathf.Max(maxScale, retry.transform.localScale.x);
                yield return null;
            }
            Assert.Greater(maxScale, 1.01f, "Retry must be emphasized after the count-up.");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
        }

        // Result의 Retry는 확인 없이 Ready로 가고, 숫자와 강조가 남지 않는다.
        retry.onClick.Invoke();
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual("000000", scoreText.text);
        Assert.AreEqual("000", killsText.text);
        Assert.AreEqual(1f, retry.transform.localScale.x, 1e-4f);
        Assert.AreEqual(0, combat.Stats.Score);
        Assert.AreEqual(1f, Time.timeScale);
    }

    private IEnumerator LoadMainAndWaitReady()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_events);
        yield return WaitFor(GameState.Ready);
    }

    private IEnumerator EnterCountdown()
    {
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.01f, "red");
        Assert.AreEqual(expected.g, actual.g, 0.01f, "green");
        Assert.AreEqual(expected.b, actual.b, 0.01f, "blue");
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }
}

using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
            // 화면에 실제로 보이는 불투명도: 굵은 글자는 TMP 하위 메시(자식 CanvasRenderer)가 그리므로 그쪽 알파까지 본다.
            float maxVisibleAlphaOfTwo = 0f, worstSubMeshGap = 0f;
            bool sawTwo = false;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (number.text != "1" && Time.realtimeSinceStartup < deadline)
            {
                sawTwo |= number.text == "2";
                float alpha = number.canvasRenderer.GetAlpha();
                minAlpha = Mathf.Min(minAlpha, alpha);
                maxScale = Mathf.Max(maxScale, number.rectTransform.localScale.x);
                float visible = VisibleAlpha(number);
                worstSubMeshGap = Mathf.Max(worstSubMeshGap, Mathf.Abs(alpha - visible));
                if (number.text == "2") maxVisibleAlphaOfTwo = Mathf.Max(maxVisibleAlphaOfTwo, visible);
                yield return null;
            }
            Assert.AreEqual("1", number.text, "Countdown must advance on unscaled time.");
            Assert.IsTrue(sawTwo, "Countdown must show 2 between 3 and 1.");
            Assert.Less(minAlpha, 0.9f, "The number must fade within a step.");
            Assert.Greater(maxScale, 1.02f, "The number must pop in larger than its rest size.");
            Assert.Less(worstSubMeshGap, 0.01f, "Bold glyphs are drawn by TMP sub-meshes; their alpha must follow the number's alpha.");
            Assert.Greater(maxVisibleAlphaOfTwo, 0.95f, "Each number must become fully visible on screen, not stay at the alpha it had when the text changed.");

            // 같은 프레임 안에서 자세를 직접 확인한다(다음 프레임에는 Presenter가 실제 시간으로 다시 그린다).
            view.AnimateNumber(0f);
            Assert.AreEqual(0f, number.canvasRenderer.GetAlpha(), 1e-4f);
            Assert.Greater(number.rectTransform.localScale.x, 1.2f);
            view.AnimateNumber(0.4f);
            Assert.AreEqual(1f, number.rectTransform.localScale.x, 1e-4f);
            Assert.AreEqual(1f, number.canvasRenderer.GetAlpha(), 1e-4f);
            Assert.AreEqual(1f, VisibleAlpha(number), 1e-4f, "The glyphs themselves must be opaque, not only the parent renderer.");
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

    // Start 화면의 조작 안내가 실제 키와 어긋나지 않는지 본다(전에는 SPACE로 적혀 있었고 회피는 Left Shift였다).
    [UnityTest]
    public IEnumerator StartHintNamesTheKeyThatDodgeIsBoundTo()
    {
        yield return LoadMainAndWaitReady();

        var start = Object.FindFirstObjectByType<StartView>();
        Assert.IsNotNull(start, "The start screen must be visible in Ready.");
        string hint = Regex.Replace(Find<TMP_Text>(start.transform, "ControlHint").text, @"\s+", " ");

        // 회피 컴포넌트는 팀원의 기본 어셈블리에 있어 이름으로 찾는다. 게임이 실제로 듣고 있는 액션을 읽는다.
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.IsNotNull(player);
        Component dodge = player.GetComponent("PlayerDodge");
        Assert.IsNotNull(dodge, "The player must carry its dodge component.");
        FieldInfo field = dodge.GetType().GetField("_dodge", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "PlayerDodge no longer keeps its resolved action in _dodge; update this test.");
        var action = field.GetValue(dodge) as InputAction;
        Assert.IsNotNull(action, "The dodge action must be resolved from the input asset.");

        StringAssert.Contains(KeyboardKeyLabel(action) + " DODGE", hint, "The control hint must name the key that dodge is bound to.");
    }

    // 전투가 끝난 화면에는 점수와 시간(00:00)만 남고, 일시정지 버튼과 게이지는 사라진다.
    [UnityTest]
    public IEnumerator FinishingKeepsScoreAndZeroTimerAndDropsPauseAndGauge()
    {
        yield return LoadMainAndWaitReady();
        yield return EnterCountdown();
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        var battle = Object.FindFirstObjectByType<BattleView>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        for (int i = 0; i < 3; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        int score = combat.Stats.Score;
        Assert.Greater(score, 0);

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        try
        {
            var finishing = Object.FindFirstObjectByType<FinishingView>();
            Assert.IsNotNull(finishing, "The finishing screen must be shown.");
            var panel = Find<RectTransform>(finishing.transform, "TimerPanel");
            var timer = Find<TMP_Text>(finishing.transform, "TimerValue");
            Assert.IsTrue(timer.gameObject.activeInHierarchy, "The timer must stay on screen while finishing.");
            Assert.AreEqual("00:00", timer.text);
            AssertColor(UiPalette.Alert, timer.color);
            Assert.AreSame(panel, timer.transform.parent, "The timer value belongs to the timer panel.");

            // 시간 패널은 오른쪽 위에서 점수 패널과 같은 높이에 있고, 화면 밖으로 나가거나 다른 요소를 덮지 않는다.
            var scorePanel = Find<RectTransform>(finishing.transform, "ScorePanel");
            Assert.AreEqual(Vector2.one, panel.anchorMin);
            Assert.AreEqual(scorePanel.anchoredPosition.y, panel.anchoredPosition.y, 0.01f, "The timer panel must sit level with the score panel.");
            Rect screen = WorldRect((RectTransform)finishing.transform);
            Rect panelRect = WorldRect(panel);
            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take clicks.");
                Rect drawn = WorldRect(graphic.rectTransform);
                Assert.IsTrue(drawn.xMin >= screen.xMin - 0.5f && drawn.xMax <= screen.xMax + 0.5f &&
                              drawn.yMin >= screen.yMin - 0.5f && drawn.yMax <= screen.yMax + 0.5f,
                    graphic.name + " must stay inside the screen.");
            }
            Assert.IsFalse(panelRect.Overlaps(WorldRect(scorePanel)), "The timer panel must not cover the score panel.");
            Assert.IsFalse(panelRect.Overlaps(WorldRect(Find<RectTransform>(finishing.transform, "TimeUpFrame"))), "The timer panel must not cover TIME UP.");

            Assert.AreEqual(score.ToString("D6"), Find<TMP_Text>(finishing.transform, "ScoreValue").text);
            Assert.IsTrue(Find<TMP_Text>(finishing.transform, "TimeUp").gameObject.activeInHierarchy);
            Assert.IsFalse(battle.gameObject.activeInHierarchy, "The battle HUD (pause button, gauge) must be hidden while finishing.");

            // 다음 화면으로 넘어가면 종료 화면이 남지 않고, Retry 뒤에도 값이 그대로다.
            _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
            yield return WaitFor(GameState.OutroDialogue);
            Assert.IsFalse(finishing.gameObject.activeInHierarchy);
            _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
            yield return WaitFor(GameState.Result);
            _events.EnqueueRequest(new RestartRequest(_state.Tick));
            yield return WaitFor(GameState.Ready);
            Assert.AreEqual("00:00", timer.text);
            Assert.AreEqual("000000", Find<TMP_Text>(finishing.transform, "ScoreValue").text);
        }
        finally
        {
            Time.timeScale = 1f;
        }
    }

    // 화면에서 차지하는 사각형(캔버스 배율이 적용된 좌표).
    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    // 액션에 묶인 키보드 키를 안내문에 쓰는 낱말로 바꾼다(leftShift → SHIFT, space → SPACE).
    private static string KeyboardKeyLabel(InputAction action)
    {
        const string keyboard = "<Keyboard>/";
        foreach (InputBinding binding in action.bindings)
        {
            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path) || !path.StartsWith(keyboard, System.StringComparison.Ordinal)) continue;
            string key = path.Substring(keyboard.Length);
            return key.EndsWith("Shift", System.StringComparison.OrdinalIgnoreCase) ? "SHIFT" : key.ToUpperInvariant();
        }
        Assert.Fail("The dodge action has no keyboard binding.");
        return null;
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

    // 글자가 화면에 실제로 그려지는 불투명도. TMP는 굵은 글자를 자식 하위 메시로 그리므로 그 CanvasRenderer까지 본다.
    private static float VisibleAlpha(TMP_Text text)
    {
        float alpha = text.canvasRenderer.GetAlpha();
        foreach (TMP_SubMeshUI subMesh in text.GetComponentsInChildren<TMP_SubMeshUI>())
            alpha = Mathf.Min(alpha, subMesh.canvasRenderer.GetAlpha());
        return alpha;
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }
}

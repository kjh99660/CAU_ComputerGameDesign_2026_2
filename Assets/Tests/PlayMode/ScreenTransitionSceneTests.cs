using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// TransitionLayer in the real scene: the screen is black while the round is being set up, fades in from black at Ready
// (first start, Retry, Quit) and briefly at Result, blocks clicks only while mostly opaque, and always ends transparent and
// non-blocking. It is purely visual: requests are processed on the same frame as without it.
public sealed class ScreenTransitionSceneTests
{
    private GameStateManager _state;
    private EventManager _events;
    private UIRoot _ui;
    private ScreenTransition _transition;

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator TheFirstReadyFadesInFromBlackAndEndsTransparentAndNonBlocking()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        Bind();
        RectTransform layer = _transition.Layer;
        Assert.IsNotNull(layer, "UIRoot must create the TransitionLayer at runtime.");
        Assert.AreSame(_ui.transform, layer.parent, "The TransitionLayer sits on the UI canvas, outside every screen view.");
        Assert.AreEqual(Vector2.zero, layer.anchorMin);
        Assert.AreEqual(Vector2.one, layer.anchorMax);
        Assert.AreEqual(Vector2.zero, layer.offsetMin, "The fade covers the whole screen, not the safe area.");
        Assert.AreEqual(Vector2.zero, layer.offsetMax);
        var layerCanvas = layer.GetComponent<Canvas>();
        Assert.IsTrue(layerCanvas.overrideSorting);
        Assert.Greater(layerCanvas.sortingOrder, _ui.GetComponent<Canvas>().sortingOrder, "The fade draws above every other UI layer.");
        Assert.IsNotNull(layer.GetComponent<GraphicRaycaster>(), "The fade takes clicks before every other UI layer.");
        if (_state.CurrentState == GameState.Initializing)
        {
            Assert.AreEqual(1f, _transition.Opacity, "The screen stays black while the round is set up.");
            Assert.IsTrue(_transition.BlocksInput);
        }

        yield return WaitForFadeFromBlack(GameState.Ready);
        Assert.IsTrue(layer.gameObject.activeInHierarchy);
        Button start = FindButton(_ui.StartScreen.transform, "StartButton");
        Assert.AreSame(layer.gameObject, TopHit(start), "While mostly opaque the fade takes the click meant for Start.");

        // The fade runs on real time: it finishes even with the game clock stopped.
        Time.timeScale = 0f;
        yield return FadeOut(_transition.ReadyFadeSeconds, start);
        Time.timeScale = 1f;
        Assert.AreSame(start.gameObject, TopHit(start), "Once transparent the Start button gets the click.");
        Assert.AreEqual(GameState.Ready, _state.CurrentState, "The fade itself never changes the state.");
    }

    [UnityTest]
    public IEnumerator RetryAndQuitFadeInFromBlackWithoutDelayingTheStateFlow()
    {
        yield return LoadAndWaitUntilClear();
        yield return EnterBattle();

        // Pause → Retry (confirmed): black while the round resets, then a fade in from black at Ready.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        yield return null;
        AssertClear("Pause is not a screen transition.");
        int requestFrame = Time.frameCount;
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return null;
        Assert.AreEqual(requestFrame + 1, Time.frameCount);
        Assert.IsTrue(_state.IsResetting || _state.CurrentState == GameState.Ready, "The restart is processed on the next frame, as always.");
        yield return WaitForFadeFromBlack(GameState.Ready);
        Button start = FindButton(_ui.StartScreen.transform, "StartButton");
        Assert.AreSame(_transition.Layer.gameObject, TopHit(start));

        // Starting during the fade: the request is processed on the very next frame, and the fade ends transparent.
        requestFrame = Time.frameCount;
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return null;
        Assert.AreEqual(requestFrame + 1, Time.frameCount);
        Assert.AreEqual(GameState.IntroDialogue, _state.CurrentState, "The fade must not delay the StartRequest.");
        yield return null;
        AssertClear("A state change ends the fade.");

        // Intro → Countdown → Battle → Pause → Quit: the same fade in from black at Ready, then transparent.
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        yield return null;
        AssertClear("The countdown is not faded.");
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        yield return null;
        AssertClear("Battle is not faded.");
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        yield return null;
        FindButton(_ui.PauseScreen.transform, "QuitButton").onClick.Invoke();
        yield return WaitForFadeFromBlack(GameState.Ready);
        yield return FadeOut(_transition.ReadyFadeSeconds, start);
        Assert.AreSame(start.gameObject, TopHit(start));
    }

    [UnityTest]
    public IEnumerator ResultFadesInBrieflyAndItsRetryFadesInAtReady()
    {
        yield return LoadAndWaitUntilClear();
        yield return EnterBattle();
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        yield return null;
        AssertClear("Finishing is not faded.");
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        yield return null;
        AssertClear("The outro dialogue is not faded.");
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitForFadeFromBlack(GameState.Result);
        Assert.Less(_transition.ResultFadeSeconds, _transition.ReadyFadeSeconds, "The result fade is the short one.");
        Button retry = FindButton(_ui.ResultScreen.transform, "RetryButton");
        yield return FadeOut(_transition.ResultFadeSeconds, retry);
        Assert.AreSame(retry.gameObject, TopHit(retry));

        // Result → Retry (no confirmation): black during the reset, then a fade in from black at Ready.
        retry.onClick.Invoke();
        yield return WaitForFadeFromBlack(GameState.Ready);
        yield return FadeOut(_transition.ReadyFadeSeconds, FindButton(_ui.StartScreen.transform, "StartButton"));
        Assert.AreEqual(1f, Time.timeScale);
    }

    // 상태가 바뀐 뒤 TransitionLayer가 그 상태를 처음 읽은 프레임(LateUpdate)까지 기다린다. 그 순간의 불투명도는 정확히 1이어야 한다.
    // Reset 완료는 코루틴에서 일어나므로 테스트 코루틴보다 먼저일 수도 나중일 수도 있다. 그래서 프레임 수가 아니라 페이드 시작을 기다린다.
    private IEnumerator WaitForFadeFromBlack(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!(_state.CurrentState == expected && _transition.IsFading) && Time.realtimeSinceStartup < deadline)
        {
            if (_transition.IsCovering)
            {
                Assert.AreEqual(1f, _transition.Opacity, "The screen is black while the round resets.");
                Assert.IsTrue(_transition.BlocksInput);
            }
            yield return null;
        }
        Assert.AreEqual(expected, _state.CurrentState);
        Assert.IsTrue(_transition.IsFading, expected + " must start a fade.");
        Assert.AreEqual(1f, _transition.Opacity, expected + " starts from black.");
        Assert.IsTrue(_transition.BlocksInput, "A black screen blocks clicks.");
        Assert.IsTrue(_transition.Layer.gameObject.activeInHierarchy);
    }

    // 실제 시간으로 잠깐 기다리는 동안 줄곧 옅어지고, 절반보다 옅으면 클릭을 막지 않으며, 끝나면 투명하고 꺼진다.
    private IEnumerator FadeOut(float seconds, Button target)
    {
        float started = Time.realtimeSinceStartup;
        float previous = _transition.Opacity;
        bool checkedHalfway = false;
        while (_transition.Opacity > 0f && Time.realtimeSinceStartup - started < seconds + 2f)
        {
            yield return null;
            Assert.LessOrEqual(_transition.Opacity, previous, "The fade only gets lighter.");
            previous = _transition.Opacity;
            if (_transition.Opacity > 0f && !_transition.BlocksInput && !checkedHalfway)
            {
                checkedHalfway = true;
                Assert.Less(_transition.Opacity, 0.5f + 1e-4f, "Input is blocked while mostly opaque.");
                Assert.AreSame(target.gameObject, TopHit(target), "A fade that no longer blocks lets the click through.");
            }
        }
        float took = Time.realtimeSinceStartup - started;
        Assert.AreEqual(0f, _transition.Opacity, $"The fade must end within {seconds + 2f:0.00} s (took {took:0.00} s).");
        Assert.GreaterOrEqual(took, seconds * 0.5f, "The fade is visible for about its length.");
        yield return null;
        AssertClear("A finished fade is transparent, non-blocking and off.");
    }

    private void AssertClear(string message)
    {
        Assert.AreEqual(0f, _transition.Opacity, message);
        Assert.IsFalse(_transition.BlocksInput, message);
        Assert.IsFalse(_transition.Layer.gameObject.activeSelf, message);
    }

    // 버튼 가운데를 누르면 맨 먼저 클릭을 받는 UI 오브젝트.
    private static GameObject TopHit(Button button)
    {
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        var data = new PointerEventData(EventSystem.current) { position = point };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        Assert.Greater(hits.Count, 0, $"Nothing takes a click at {point} ({button.name}).");
        return hits[0].gameObject;
    }

    private static Button FindButton(Transform root, string name)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
            if (button.name == name) return button;
        Assert.Fail("Missing button " + name);
        return null;
    }

    private void Bind()
    {
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _ui = Object.FindFirstObjectByType<UIRoot>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_events);
        Assert.IsNotNull(_ui);
        _transition = _ui.GetComponent<ScreenTransition>();
        Assert.IsNotNull(_transition, "UIRoot must add the screen transition.");
    }

    private IEnumerator LoadAndWaitUntilClear()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        Bind();
        yield return WaitFor(GameState.Ready);
        float deadline = Time.realtimeSinceStartup + 3f;
        while ((_transition.Opacity > 0f || _transition.Layer.gameObject.activeSelf) && Time.realtimeSinceStartup < deadline)
            yield return null;
        AssertClear("The first fade ends by itself.");
    }

    private IEnumerator EnterBattle()
    {
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }
}

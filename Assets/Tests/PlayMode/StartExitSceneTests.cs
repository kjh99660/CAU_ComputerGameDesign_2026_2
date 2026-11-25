using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

// The start screen's EXIT button (2026-11-25): the game's own way out, at the top right where the dialogue's SKIP sits.
// A mouse click leaves the game (the real exit quits the player, or stops Play Mode in the Editor; here a stand-in
// counts the calls). It is no game state: nothing is queued and the state stays Ready. Space and Enter still press only
// START, and the button cannot be pressed on any other screen. The pause menu's QUIT keeps returning to this screen.
public sealed class StartExitSceneTests
{
    private sealed class CountingExit : IApplicationExit
    {
        public int Calls;
        public void Exit() => Calls++;
    }

    private GameStateManager _state;
    private EventManager _events;
    private UIRoot _ui;
    private CoreBootstrap _bootstrap;
    private CountingExit _exit;

    [UnitySetUp]
    public IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _ui = Object.FindFirstObjectByType<UIRoot>();
        _bootstrap = Object.FindFirstObjectByType<CoreBootstrap>();
        _exit = new CountingExit();
        _ui.SetApplicationExit(_exit);
        yield return WaitFor(GameState.Ready);
        var transition = Object.FindFirstObjectByType<ScreenTransition>();
        float until = Time.realtimeSinceStartup + 5f;
        while (transition != null && transition.BlocksInput && Time.realtimeSinceStartup < until) yield return null;
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        // Back to the real exit before the scene goes away (the stand-in must not outlive the test).
        if (_ui != null) _ui.SetApplicationExit(null);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ExitSitsTopRightAwayFromStartAndAClickLeavesTheGame()
    {
        var start = Object.FindFirstObjectByType<StartView>();
        Button exit = Find<Button>(start.transform, "ExitButton");
        Button startButton = Find<Button>(start.transform, "StartButton");
        Assert.IsTrue(exit.gameObject.activeInHierarchy, "The start screen shows EXIT.");
        Assert.AreEqual("EXIT", Find<TMP_Text>(exit.transform, "Label").text);
        var rect = (RectTransform)exit.transform;
        Assert.AreEqual(new Vector2(1f, 1f), rect.anchorMin, "Anchored to the top right corner, like the dialogue's SKIP.");
        Assert.AreEqual(new Vector2(1f, 1f), rect.anchorMax);
        Assert.AreEqual(Navigation.Mode.None, exit.navigation.mode, "Enter must never press a clicked EXIT again.");
        Rect exitRect = WorldRect(rect);
        foreach (string other in new[] { "StartButton", "Title", "TitleFrame", "ControlHintPanel" })
            Assert.IsFalse(exitRect.Overlaps(WorldRect(Find<RectTransform>(start.transform, other))), "EXIT overlaps " + other);
        Assert.Greater(Vector2.Distance(exitRect.center, WorldRect((RectTransform)startButton.transform).center), 300f,
            "EXIT is well away from START.");

        Click(exit);
        Assert.AreEqual(1, _exit.Calls, "A click leaves the game.");
        yield return Frames(5);
        Assert.AreEqual(GameState.Ready, _state.CurrentState, "Leaving is not a game state: nothing was requested.");
        Click(startButton);
        yield return WaitFor(GameState.IntroDialogue);
        Assert.AreEqual(1, _exit.Calls, "START does not leave.");
    }

    [UnityTest]
    public IEnumerator SpaceAndEnterPressOnlyStartAndExitIsGoneOnEveryOtherScreen()
    {
        var start = Object.FindFirstObjectByType<StartView>();
        Button exit = Find<Button>(start.transform, "ExitButton");
        // Space/Enter (the UI layer's confirm) presses START, never EXIT.
        Confirm();
        yield return WaitFor(GameState.IntroDialogue);
        Assert.AreEqual(0, _exit.Calls);
        Assert.IsFalse(exit.gameObject.activeInHierarchy, "The start screen and its EXIT are hidden in the dialogue.");
        Click(exit);
        Assert.AreEqual(0, _exit.Calls, "A hidden EXIT cannot be pressed.");

        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        Click(exit);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Click(exit);
        Assert.AreEqual(0, _exit.Calls, "Not in battle, not in the pause menu.");

        // The pause menu's QUIT still only returns to the start screen, where EXIT is there again.
        Find<Button>(Object.FindFirstObjectByType<PauseView>().transform, "QuitButton").onClick.Invoke();
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, _exit.Calls, "QUIT returns to the start screen, it does not leave the game.");
        var transition = Object.FindFirstObjectByType<ScreenTransition>();
        float until = Time.realtimeSinceStartup + 5f;
        while (transition != null && transition.BlocksInput && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(exit.gameObject.activeInHierarchy);
        Click(exit);
        Assert.AreEqual(1, _exit.Calls);
    }

    // A real mouse click: the button ignores it when it is hidden, switched off or its screen takes no input.
    private static void Click(Button button)
    {
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    // The Space/Enter handler of the UI layer (the keyboard is read in UiLayer.Update).
    private void Confirm()
    {
        var layer = (UiLayer)typeof(CoreBootstrap).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bootstrap);
        typeof(UiLayer).GetMethod("HandleConfirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);
    }

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }
}

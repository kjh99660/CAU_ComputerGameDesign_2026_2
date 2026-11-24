using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Safe Area in the real scene: UIRoot fits every screen view's content to the safe area while full-screen backgrounds and dims
// keep covering the whole screen. On a desktop screen the safe area is the whole screen and nothing moves. A notched or rounded
// screen is simulated through SafeAreaFitter.Simulate, walking the real state flow so every view is checked as the game shows it.
public sealed class SafeAreaSceneTests
{
    private static readonly string[] ScreenNames =
        { "Start", "Dialogue", "Countdown", "Battle", "Pause", "RetryConfirmation", "Finishing", "Result" };
    // The full-screen backgrounds the views have today (prefab dims and the ones the views create at runtime).
    private static readonly string[] KnownBackgrounds = { "DimmedOverlay", "AdvanceArea", "StartFlash", "PlayerHitFlash" };

    private GameStateManager _state;
    private EventManager _events;
    private UIRoot _ui;

    [TearDown]
    public void TearDown()
    {
        SafeAreaFitter.StopSimulating();
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator EveryScreenViewIsFittedAndNothingMovesOnADesktopScreen()
    {
        yield return Load();
        Assert.AreEqual(new Rect(0f, 0f, Screen.width, Screen.height), Screen.safeArea, "This check assumes a desktop screen.");
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), SafeAreaFitter.CurrentNormalizedSafeArea());
        foreach (string name in ScreenNames)
        {
            Transform view = _ui.transform.Find(name);
            Assert.IsNotNull(view, "Missing screen view " + name);
            var fitter = view.GetComponent<SafeAreaFitter>();
            Assert.IsNotNull(fitter, name + " must be fitted to the safe area by UIRoot.");
            var rect = (RectTransform)view;
            Assert.AreEqual(Vector2.zero, rect.anchorMin, name + " must not move on a desktop screen.");
            Assert.AreEqual(Vector2.one, rect.anchorMax, name + " must not move on a desktop screen.");
            foreach (Transform child in view)
            {
                if (!fitter.IsBackground((RectTransform)child)) continue;
                Assert.AreEqual(Vector2.zero, ((RectTransform)child).anchorMin, $"{name}/{child.name} must not move on a desktop screen.");
                Assert.AreEqual(Vector2.one, ((RectTransform)child).anchorMax, $"{name}/{child.name} must not move on a desktop screen.");
            }
        }
        Assert.AreEqual(1, _ui.GetComponents<ScreenTransition>().Length);
        Assert.IsNull(_ui.transform.Find("TransitionLayer").GetComponent<SafeAreaFitter>(), "The transition covers the whole screen.");
    }

    [UnityTest]
    public IEnumerator ContentFitsASimulatedSafeAreaWhileBackgroundsStillCoverTheScreen()
    {
        yield return Load();
        var screen = new Vector2(Screen.width, Screen.height);
        // Uneven insets on every side (provisional test values: a notch on the left, rounded corners elsewhere).
        Rect safe = Rect.MinMaxRect(0.06f * screen.x, 0.03f * screen.y, 0.95f * screen.x, 0.92f * screen.y);
        SafeAreaFitter.Simulate(safe, screen);
        yield return null;
        var seen = new HashSet<string>();
        AssertFitted("Start", safe, screen, seen);

        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        yield return null;
        AssertFitted("Dialogue", safe, screen, seen);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        yield return null;
        AssertFitted("Countdown", safe, screen, seen);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        yield return null;
        AssertFitted("Battle", safe, screen, seen);

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        yield return null;
        AssertFitted("Pause", safe, screen, seen);
        Find<Button>(_ui.PauseScreen.transform, "RetryButton").onClick.Invoke();
        yield return null;
        AssertFitted("RetryConfirmation", safe, screen, seen);
        Find<Button>(_ui.RetryConfirmationScreen.transform, "CancelButton").onClick.Invoke();
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        yield return null;
        AssertFitted("Finishing", safe, screen, seen);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        yield return null;
        AssertFitted("Result", safe, screen, seen);
        // Today: the dims of Start, Pause, RetryConfirmation and Result, the dialogue's click-anywhere area, the countdown's
        // START flash and the battle HUD's hit flash.
        Assert.Greater(seen.Count, 2, "The screens' full-screen backgrounds must have been checked.");

        // A different safe area while the screen stays up (e.g. the device rotated): refitted on the next frame.
        Rect other = Rect.MinMaxRect(0.02f * screen.x, 0.08f * screen.y, 0.9f * screen.x, 0.97f * screen.y);
        SafeAreaFitter.Simulate(other, screen);
        yield return null;
        AssertFitted("Result", other, screen, seen);

        // Back to the real (desktop) safe area: the authored layout returns.
        SafeAreaFitter.StopSimulating();
        yield return null;
        var result = (RectTransform)_ui.ResultScreen.transform;
        Assert.AreEqual(Vector2.zero, result.anchorMin);
        Assert.AreEqual(Vector2.one, result.anchorMax);
        var dim = (RectTransform)result.Find("DimmedOverlay");
        Assert.AreEqual(Vector2.zero, dim.anchorMin);
        Assert.AreEqual(Vector2.one, dim.anchorMax);
    }

    [UnityTest]
    public IEnumerator AFullScreenTextIsContentAndStaysInsideTheSafeArea()
    {
        yield return Load();
        // A text stretched over the whole screen (a direct child of the view, anchors 0..1, no offsets), the way another lane might
        // centre a banner or pin a notice to a corner. It must stay readable inside the safe area, not be widened like a dim.
        Transform view = _ui.StartScreen.transform;
        var fitter = view.GetComponent<SafeAreaFitter>();
        var banner = new GameObject("BannerProbe", typeof(RectTransform), typeof(TextMeshProUGUI));
        banner.layer = view.gameObject.layer;
        var rect = (RectTransform)banner.transform;
        rect.SetParent(view, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = banner.GetComponent<TextMeshProUGUI>();
        var title = Find<TMP_Text>(view, "Title");
        text.font = title.font;
        text.text = title.text;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        // A full-screen image next to it is still a background (the control case).
        var dimProbe = new GameObject("DimProbe", typeof(RectTransform), typeof(Image));
        var dimRect = (RectTransform)dimProbe.transform;
        dimRect.SetParent(view, false);
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
        dimProbe.GetComponent<Image>().raycastTarget = false;

        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Rect.MinMaxRect(0.06f * screen.x, 0.03f * screen.y, 0.95f * screen.x, 0.92f * screen.y);
        SafeAreaFitter.Simulate(safe, screen);
        yield return null;
        Assert.IsFalse(fitter.IsBackground(rect), "A text is content, never a full-screen background.");
        Assert.IsTrue(fitter.IsBackground(dimRect), "A full-screen image without children is a background.");
        AssertSameRect(safe, WorldRect(rect), "A full-screen text is fitted to the safe area like the rest of the content");
        AssertSameRect(new Rect(Vector2.zero, screen), WorldRect(dimRect), "A full-screen image still covers the whole screen");
        AssertFitted("Start", safe, screen, new HashSet<string>());
        Object.Destroy(banner);
        Object.Destroy(dimProbe);
    }

    // The view's content lies inside the safe area; its full-screen backgrounds still cover the whole screen (overlay canvas:
    // world units are screen pixels).
    private void AssertFitted(string name, Rect safe, Vector2 screen, HashSet<string> seen)
    {
        Transform view = _ui.transform.Find(name);
        Assert.IsTrue(view.gameObject.activeInHierarchy, name + " must be on screen here.");
        var fitter = view.GetComponent<SafeAreaFitter>();
        AssertSameRect(safe, WorldRect((RectTransform)view), name + " content area");
        foreach (Transform child in view)
        {
            var rect = (RectTransform)child;
            bool known = System.Array.IndexOf(KnownBackgrounds, child.name) >= 0;
            if (!fitter.IsBackground(rect))
            {
                Assert.IsFalse(known, $"{name}/{child.name} is a full-screen background and must cover the whole screen.");
                continue;
            }
            seen.Add(child.name);
            AssertSameRect(new Rect(Vector2.zero, screen), WorldRect(rect), $"{name}/{child.name} must cover the whole screen");
        }
        int content = 0;
        foreach (Graphic graphic in view.GetComponentsInChildren<Graphic>())
        {
            // Backgrounds cover the whole screen; a text is always content and is checked wherever it sits.
            if (!(graphic is TMP_Text) && IsInsideBackground(graphic.transform, view, fitter)) continue;
            content++;
            Rect drawn = WorldRect(graphic.rectTransform);
            Assert.IsTrue(drawn.xMin >= safe.xMin - 0.5f && drawn.xMax <= safe.xMax + 0.5f &&
                          drawn.yMin >= safe.yMin - 0.5f && drawn.yMax <= safe.yMax + 0.5f,
                $"{name}/{graphic.name} {drawn} must lie inside the safe area {safe}.");
        }
        Assert.Greater(content, 0, name + " has no content to check.");
    }

    private static bool IsInsideBackground(Transform node, Transform view, SafeAreaFitter fitter)
    {
        for (Transform current = node; current != null && current != view; current = current.parent)
            if (current.parent == view && fitter.IsBackground((RectTransform)current)) return true;
        return false;
    }

    private static void AssertSameRect(Rect expected, Rect actual, string message)
    {
        Assert.Less(Vector2.Distance(expected.min, actual.min), 0.5f, $"{message}: {actual} instead of {expected}");
        Assert.Less(Vector2.Distance(expected.max, actual.max), 0.5f, $"{message}: {actual} instead of {expected}");
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

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _ui = Object.FindFirstObjectByType<UIRoot>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_events);
        Assert.IsNotNull(_ui);
        yield return WaitFor(GameState.Ready);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }
}

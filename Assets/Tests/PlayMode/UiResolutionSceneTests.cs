using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// UI resolution check (AGENTS.md §11, §16). Every screen is shown through the real state flow and laid out at 1920x1080,
// 1280x720, 1366x768, 2560x1440 (16:9), 1920x1200 (16:10) and 2560x1080 (21:9), once with the screen's own safe area and once
// with a simulated notch/rounded-corner safe area. At each size every visible text, image and button of the screen must lie
// inside the safe area, no text may be cut off, full-screen backgrounds must still cover the whole screen, and the screen's
// top-level elements (its direct children) must not overlap. Elements are discovered by walking the screen's Graphics and
// Selectables, so elements added later are checked too. The only overlaps allowed are listed below, each with its reason:
// an element drawn on its own decorative frame or panel (Frames), and design overlaps or known issues that may only cover a
// bounded strip at one edge of the element behind (IntendedOverlaps, KnownIssues). Full-screen backgrounds (dims, flashes,
// click-anywhere areas) are left out of the overlap check because they are meant to lie under everything.
// Set REBOOT_UI_CAPTURE_DIR to also save a picture of every screen at every size (scene behind, UI on top) into that folder.
public sealed class UiResolutionSceneTests
{
    private static readonly Vector2Int[] Resolutions =
    {
        new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1366, 768),
        new Vector2Int(2560, 1440), new Vector2Int(1920, 1200), new Vector2Int(2560, 1080)
    };

    // Simulated safe area as screen fractions: a notch on both sides and rounded corners top and bottom (provisional test values).
    private static readonly Rect SimulatedSafeArea = Rect.MinMaxRect(0.04f, 0.03f, 0.96f, 0.97f);

    // Elements each screen must show in its state ("A|B": either name), so the checks never pass on an empty screen.
    private static readonly Dictionary<string, string[]> KeyElements = new Dictionary<string, string[]>
    {
        { "Start", new[] { "Title", "StartButton", "ControlHint", "ExitButton" } },
        { "Dialogue", new[] { "LeftPortrait", "RightPortrait", "DialogueBox", "SkipButton" } },
        { "Countdown", new[] { "Number", "ReadyText" } },
        { "Battle", new[] { "PauseButton", "ScorePanel", "TimerPanel", "SpecialGaugeFrame|SpecialGaugeFill" } },
        { "Pause", new[] { "ResumeButton", "RetryButton", "QuitButton" } },
        { "RetryConfirmation", new[] { "ConfirmButton", "CancelButton" } },
        { "Finishing", new[] { "ScorePanel", "TimerPanel", "TimeUp" } },
        { "Result", new[] { "KillsTile", "MaxComboTile", "HitsTakenTile", "SpecialUsesTile", "RetryButton" } }
    };

    // Elements drawn on a decorative frame or panel that is their sibling in the prefab: { screen, frame, members, reason }.
    // A listed member may lie on its frame, and must then lie inside it. Any other overlap with a frame is reported, and no entry
    // lets one key element sit inside another. A label that is a child of its panel needs no entry (it is part of that element).
    private static readonly string[][] Frames =
    {
        new[] { "Start", "TitleFrame", "Title,Subtitle", "the title and the subtitle are drawn on their frame" },
        new[] { "Start", "ControlHintPanel", "ControlHint", "the controls line is drawn on its panel" },
        new[] { "Countdown", "NumberFrame", "Number", "the countdown number is drawn on its frame" },
        new[] { "Countdown", "ReadyFrame", "ReadyText", "READY / START is drawn on its frame" },
        new[] { "Finishing", "TimeUpFrame", "TimeUp", "TIME UP is drawn on its frame" },
        new[] { "Pause", "MenuPanel", "HeaderFrame,ResumeButton,RetryButton,QuitButton", "the menu panel holds the header and the three buttons" },
        new[] { "RetryConfirmation", "ModalPanel", "WarningIcon,Title,Message,CancelButton,ConfirmButton", "the modal panel holds its icon, texts and buttons" }
    };

    // Overlaps that are part of the design. Only the bottom fifth of a portrait may lie under the dialogue box (the box's speaker
    // name and text included); a box that reaches higher covers the faces and is reported.
    private static readonly Allowance[] IntendedOverlaps =
    {
        new Allowance("Dialogue", "LeftPortrait", "DialogueBox", Edge.Bottom, 0.2f, null,
            "the portraits stand behind the dialogue box (RebootUiThemeBuilder.EnsureDialogueLayout)"),
        new Allowance("Dialogue", "RightPortrait", "DialogueBox", Edge.Bottom, 0.2f, null,
            "the portraits stand behind the dialogue box (RebootUiThemeBuilder.EnsureDialogueLayout)")
    };

    // Overlaps found in today's prefab layouts that this lane does not own. They are reported (logged) but do not fail the run,
    // as long as they occur only at the listed sizes and do not grow beyond the listed strip. Skip may reach the top fifth of the
    // right portrait (65 of 605 units today with the notch safe area; 103 of 630 if K-7 spoke first and stood at full size).
    private static readonly Allowance[] KnownIssues =
    {
        new Allowance("Dialogue", "RightPortrait", "SkipButton", Edge.Top, 0.2f, "2560x1080,2560x1080 safe",
            "Dialogue.prefab layout: on 21:9 the canvas is 935 units tall, so Skip (top-right, 78 tall, 36 from the top) reaches " +
            "below the top of the right portrait (238 + 630 from the bottom). With the screen's own safe area only the portrait's " +
            "transparent top margin is under Skip; with a notch safe area Skip sits on the portrait's hair.")
    };

    private const float Tolerance = 1f;
    private static readonly Vector3[] Corners = new Vector3[4];

    private GameStateManager _state;
    private EventManager _events;
    private UIRoot _ui;
    private Canvas _canvas;
    private CanvasScaler _scaler;
    private Sizer _sizer;
    private readonly List<string> _violations = new List<string>();
    // Listed overlaps, grouped by screen and element pair: where they occur (size, overlap and rects).
    private readonly Dictionary<string, List<string>> _reported = new Dictionary<string, List<string>>();
    private readonly HashSet<string> _knownSeen = new HashSet<string>();
    private readonly HashSet<string> _framesSeen = new HashSet<string>();
    private int _layouts;

    [TearDown]
    public void TearDown()
    {
        _sizer?.Dispose();
        _sizer = null;
        SafeAreaFitter.StopSimulating();
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator EveryScreenFitsEverySupportedResolution()
    {
        AssertListsAreSound();
        yield return Load();
        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, _canvas.renderMode);
        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, _scaler.uiScaleMode, "The UI scales with the screen size.");
        Assert.AreEqual(new Vector2(1920f, 1080f), _scaler.referenceResolution);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, _scaler.screenMatchMode);
        Assert.AreEqual(0.5f, _scaler.matchWidthOrHeight, 1e-4f);
        _sizer = new Sizer(_canvas, Environment.GetEnvironmentVariable("REBOOT_UI_CAPTURE_DIR"));
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();

        CheckScreen("Start", "01");
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        yield return null;
        CheckScreen("Dialogue", "02");
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        yield return null;
        CheckScreen("Countdown", "03");
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        // A round in progress: some score and a partly filled gauge.
        for (int i = 0; i < 6; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        yield return new WaitForSecondsRealtime(0.3f);
        CheckScreen("Battle", "04");

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        yield return null;
        CheckScreen("Pause", "05");
        Find<Button>(_ui.PauseScreen.transform, "RetryButton").onClick.Invoke();
        yield return null;
        CheckScreen("RetryConfirmation", "06");
        Find<Button>(_ui.RetryConfirmationScreen.transform, "CancelButton").onClick.Invoke();
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        yield return null;
        CheckScreen("Finishing", "07");
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        yield return null;
        CheckScreen("Dialogue", "08");
        CheckEveryDialogueLine();
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        // Let the count-up and the result fade finish so the screen shows its final values.
        yield return new WaitForSecondsRealtime(1.6f);
        CheckScreen("Result", "09");
        Assert.AreEqual(1f, Time.timeScale);

        var report = new StringBuilder();
        report.AppendLine($"UI resolution check: {_layouts} layouts, {_violations.Count} violations, {_reported.Count} listed overlaps.");
        foreach (KeyValuePair<string, List<string>> listed in _reported)
        {
            report.AppendLine("  " + listed.Key);
            foreach (string where in listed.Value) report.AppendLine("      " + where);
        }
        foreach (Allowance issue in KnownIssues)
            if (!_knownSeen.Contains(issue.Key))
                report.AppendLine($"  known issue no longer found (remove it from KnownIssues): {issue.Screen} {issue.Behind} / {issue.Front}");
        foreach (string[] frame in Frames)
            foreach (string member in frame[2].Split(','))
                if (!_framesSeen.Contains(frame[0] + "|" + frame[1] + "|" + member))
                    report.AppendLine($"  frame entry no longer used (update Frames): {frame[0]} {member} on {frame[1]}");
        foreach (string line in _violations) report.AppendLine("  VIOLATION: " + line);
        Debug.Log(report.ToString());
        _sizer.WriteReport(report.ToString());
        Assert.IsEmpty(_violations, report.ToString());
    }

    // The check itself reports what it must not allow (the review findings it was hardened against): a key element moved inside
    // another key element, a dialogue box raised over the portraits' faces, and a text stretched over the whole screen, which is
    // content (fitted to the safe area and checked), not a background.
    [UnityTest]
    public IEnumerator TheCheckReportsOverlapsItMustNotAllow()
    {
        yield return Load();
        _sizer = new Sizer(_canvas, null);
        var fullHd = new Vector2Int(1920, 1080);

        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        yield return null;
        Assert.IsEmpty(AnalyzeOnce("Dialogue", fullHd, false), "Today's dialogue screen passes.");
        var box = Find<RectTransform>(_ui.transform.Find("Dialogue"), "DialogueBox");
        Vector2 boxSize = box.sizeDelta;
        box.sizeDelta = new Vector2(boxSize.x, 520f);
        List<string> found = AnalyzeOnce("Dialogue", fullHd, false);
        box.sizeDelta = boxSize;
        AssertFound(found, "a dialogue box raised over the portraits' faces", "DialogueBox", "Portrait", "deeper than");

        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        yield return null;
        Transform battle = _ui.transform.Find("Battle");
        Assert.IsEmpty(AnalyzeOnce("Battle", fullHd, true), "Today's battle HUD passes.");
        var pause = Find<RectTransform>(battle, "PauseButton");
        Vector2 pausePosition = pause.anchoredPosition;
        pause.anchoredPosition = new Vector2(200f, pausePosition.y);
        found = AnalyzeOnce("Battle", fullHd, false);
        pause.anchoredPosition = pausePosition;
        AssertFound(found, "the pause button moved inside the score panel", "PauseButton", "ScorePanel", "overlaps");

        // A banner text stretched over the whole battle screen and pinned to its top-left corner, where the pause button is.
        var banner = new GameObject("BannerProbe", typeof(RectTransform), typeof(TextMeshProUGUI));
        banner.layer = battle.gameObject.layer;
        var bannerRect = (RectTransform)banner.transform;
        bannerRect.SetParent(battle, false);
        bannerRect.anchorMin = Vector2.zero;
        bannerRect.anchorMax = Vector2.one;
        bannerRect.offsetMin = bannerRect.offsetMax = Vector2.zero;
        var bannerText = banner.GetComponent<TextMeshProUGUI>();
        var score = Find<TMP_Text>(battle, "ScoreValue");
        bannerText.font = score.font;
        bannerText.fontSize = 120f;
        bannerText.text = score.text;
        bannerText.alignment = TextAlignmentOptions.TopLeft;
        bannerText.raycastTarget = false;
        found = AnalyzeOnce("Battle", fullHd, true);
        Assert.IsFalse(battle.GetComponent<SafeAreaFitter>().IsBackground(bannerRect), "A text is never a full-screen background.");
        AssertFound(found, "a full-screen text running into the pause button", "BannerProbe", "PauseButton", "overlaps");
        foreach (string line in found)
            Assert.IsFalse(line.Contains("BannerProbe") && line.Contains("safe area"), "The full-screen text is fitted to the safe area: " + line);
        Object.Destroy(banner);
    }

    // Lays the screen out at every size (own safe area, then the simulated one), checks it and puts the canvas back.
    private void CheckScreen(string name, string order)
    {
        Transform view = _ui.transform.Find(name);
        Assert.IsNotNull(view, "Missing screen " + name);
        Assert.IsTrue(view.gameObject.activeInHierarchy, name + " must be on screen in " + _state.CurrentState);
        try
        {
            foreach (Vector2Int size in Resolutions)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool simulated = pass == 1;
                    LayOutAndAnalyze(name, view, size, simulated);
                    _sizer.Capture($"{order}_{name}_{size.x}x{size.y}{(simulated ? "_safe" : string.Empty)}");
                    _layouts++;
                }
            }
        }
        finally
        {
            SafeAreaFitter.StopSimulating();
            _sizer.Restore();
            ApplyFitters();
        }
    }

    private void LayOutAndAnalyze(string name, Transform view, Vector2Int size, bool simulated)
    {
        LayOut(size, simulated);
        Settle(view);
        Canvas.ForceUpdateCanvases();
        Analyze(name, view, $"{size.x}x{size.y}{(simulated ? " safe" : string.Empty)}");
    }

    // One layout of one screen, for checking the check: returns its violations and leaves the suite's lists as they were.
    private List<string> AnalyzeOnce(string name, Vector2Int size, bool simulated)
    {
        Transform view = _ui.transform.Find(name);
        Assert.IsTrue(view.gameObject.activeInHierarchy, name + " must be on screen in " + _state.CurrentState);
        _violations.Clear();
        try
        {
            LayOutAndAnalyze(name, view, size, simulated);
            return new List<string>(_violations);
        }
        finally
        {
            _violations.Clear();
            SafeAreaFitter.StopSimulating();
            _sizer.Restore();
            ApplyFitters();
        }
    }

    private static void AssertFound(List<string> violations, string what, params string[] parts)
    {
        foreach (string line in violations)
        {
            bool all = true;
            foreach (string part in parts) all &= line.Contains(part);
            if (all) return;
        }
        Assert.Fail($"The check must report {what} (a line with {string.Join(", ", parts)}). It reported:\n  " + string.Join("\n  ", violations));
    }

    // The lists can only allow what they explain: a frame entry never pairs two key elements, and every list names its reason.
    private static void AssertListsAreSound()
    {
        foreach (string[] frame in Frames)
        {
            Assert.AreEqual(4, frame.Length);
            Assert.IsTrue(KeyElements.ContainsKey(frame[0]), "Unknown screen " + frame[0]);
            foreach (string member in frame[2].Split(','))
                Assert.IsFalse(IsKey(frame[0], frame[1]) && IsKey(frame[0], member),
                    $"{frame[0]}: {member} and {frame[1]} are both key elements; one may not hide inside the other.");
        }
        foreach (Allowance allowance in IntendedOverlaps) allowance.AssertSound();
        foreach (Allowance allowance in KnownIssues) allowance.AssertSound();
    }

    private static bool IsKey(string screen, string element)
    {
        foreach (string key in KeyElements[screen])
            if (Array.IndexOf(key.Split('|'), element) >= 0) return true;
        return false;
    }

    private void LayOut(Vector2Int size, bool simulated)
    {
        _sizer.SetSize(size);
        if (simulated)
            SafeAreaFitter.Simulate(new Rect(SimulatedSafeArea.x * size.x, SimulatedSafeArea.y * size.y,
                SimulatedSafeArea.width * size.x, SimulatedSafeArea.height * size.y), size);
        else
            SafeAreaFitter.StopSimulating();
        ApplyFitters();
        Canvas.ForceUpdateCanvases();
        // The canvas really is laid out at this size: the CanvasScaler's match-width-or-height factor and the canvas size follow.
        float expected = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(size.x / _scaler.referenceResolution.x, 2f),
            Mathf.Log(size.y / _scaler.referenceResolution.y, 2f), _scaler.matchWidthOrHeight));
        Assert.AreEqual(expected, _canvas.scaleFactor, 1e-3f, $"Canvas scale at {size}");
        Vector2 canvasSize = ((RectTransform)_canvas.transform).rect.size;
        Assert.AreEqual(size.x / expected, canvasSize.x, 0.5f, $"Canvas width at {size}");
        Assert.AreEqual(size.y / expected, canvasSize.y, 0.5f, $"Canvas height at {size}");
    }

    private void ApplyFitters()
    {
        foreach (Transform child in _ui.transform)
            if (child.gameObject.activeInHierarchy && child.TryGetComponent(out SafeAreaFitter fitter))
                fitter.Apply();
    }

    // Animated screens are measured at rest: the countdown number between its pop and its fade, a dialogue line fully typed.
    private static void Settle(Transform view)
    {
        if (view.TryGetComponent(out CountdownView countdown)) countdown.AnimateNumber(0.4f);
        if (view.TryGetComponent(out DialogueView dialogue)) dialogue.SetVisibleCharacters(int.MaxValue);
    }

    // Every line of both dialogues fits its box at every size (dialogue text is the longest text in the UI).
    private void CheckEveryDialogueLine()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        Assert.IsNotNull(data);
        Transform view = _ui.transform.Find("Dialogue");
        var dialogue = view.GetComponent<DialogueView>();
        var box = Find<RectTransform>(view, "DialogueBox");
        var lines = new List<DialogueLine>(data.intro);
        lines.AddRange(data.outro);
        Assert.Greater(lines.Count, 0);
        try
        {
            foreach (Vector2Int size in Resolutions)
            {
                LayOut(size, false);
                RectTransform canvas = (RectTransform)_canvas.transform;
                Rect boxRect = CanvasRect(box, canvas);
                foreach (DialogueLine line in lines)
                {
                    dialogue.Render(new DialogueUiModel(line.speaker, line.text, line.leftSpeaker));
                    dialogue.SetVisibleCharacters(int.MaxValue);
                    foreach (TMP_Text text in box.GetComponentsInChildren<TMP_Text>())
                    {
                        if (!TextBounds(text, canvas, out Rect drawn)) continue;
                        if (text.isTextTruncated)
                            _violations.Add($"{size.x}x{size.y} Dialogue: {text.name} cuts off \"{line.text}\"");
                        if (!Contains(boxRect, drawn, Tolerance))
                            _violations.Add($"{size.x}x{size.y} Dialogue: {text.name} {Format(drawn)} leaves the dialogue box {Format(boxRect)} for \"{line.text}\"");
                    }
                }
            }
        }
        finally
        {
            _sizer.Restore();
            ApplyFitters();
        }
    }

    private void Analyze(string name, Transform view, string label)
    {
        RectTransform canvas = (RectTransform)_canvas.transform;
        Vector2 canvasSize = canvas.rect.size;
        Rect normalized = SafeAreaFitter.CurrentNormalizedSafeArea();
        Rect safe = Rect.MinMaxRect(normalized.xMin * canvasSize.x, normalized.yMin * canvasSize.y,
            normalized.xMax * canvasSize.x, normalized.yMax * canvasSize.y);
        Rect screen = new Rect(Vector2.zero, canvasSize);
        var fitter = view.GetComponent<SafeAreaFitter>();
        Assert.IsNotNull(fitter, name + " is not fitted to the safe area.");
        string where = $"{label} {name}";

        // Full-screen backgrounds cover the whole screen, safe area or not.
        foreach (Transform child in view)
        {
            if (!child.gameObject.activeInHierarchy || !fitter.IsBackground((RectTransform)child)) continue;
            Rect rect = CanvasRect((RectTransform)child, canvas);
            if (!Contains(rect, screen, Tolerance))
                _violations.Add($"{where}: background {child.name} {Format(rect)} does not cover the screen {Format(screen)}");
        }

        List<Element> elements = Collect(view, canvas, fitter);
        var present = new HashSet<string>();
        foreach (Element element in elements)
        {
            present.Add(element.Name);
            present.Add(element.Top.name);
            if (!Contains(safe, element.Bounds, 0.5f))
                _violations.Add($"{where}: {element.Path} {Format(element.Bounds)} leaves the safe area {Format(safe)}");
            if (element.Text != null && element.Text.isTextTruncated)
                _violations.Add($"{where}: {element.Path} cuts off \"{element.Text.text}\"");
        }
        foreach (string key in KeyElements[name])
        {
            bool found = false;
            foreach (string option in key.Split('|')) found |= present.Contains(option);
            if (!found) _violations.Add($"{where}: key element {key} is not on screen");
        }

        // Top-level elements (direct children of the screen) must not overlap, whether they cross or one lies inside the other,
        // unless the pair is listed (Frames, IntendedOverlaps, KnownIssues). Inside one top-level element, texts must not overlap
        // other texts and buttons other buttons.
        var groups = new List<Transform>();
        var bounds = new Dictionary<Transform, Rect>();
        foreach (Element element in elements)
        {
            if (bounds.TryGetValue(element.Top, out Rect union))
                bounds[element.Top] = Rect.MinMaxRect(Mathf.Min(union.xMin, element.Bounds.xMin), Mathf.Min(union.yMin, element.Bounds.yMin),
                    Mathf.Max(union.xMax, element.Bounds.xMax), Mathf.Max(union.yMax, element.Bounds.yMax));
            else
            {
                bounds[element.Top] = element.Bounds;
                groups.Add(element.Top);
            }
        }
        for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
        {
            Transform a = groups[i], b = groups[j];
            if (!WorstOverlap(elements, a, b, false, out Element ea, out Element eb, out Rect overlap)) continue;
            string detail = $"{ea.Path} {Format(ea.Bounds)} overlaps {eb.Path} {Format(eb.Bounds)} by {overlap.width:0}x{overlap.height:0}";
            if (IsFramed(name, a.name, b.name, out bool aIsFrame))
            {
                Transform frame = aIsFrame ? a : b, member = aIsFrame ? b : a;
                _framesSeen.Add(name + "|" + frame.name + "|" + member.name);
                if (!Contains(bounds[frame], bounds[member], Tolerance))
                    _violations.Add($"{where}: {member.name} {Format(bounds[member])} reaches out of its frame {frame.name} {Format(bounds[frame])}");
                continue;
            }
            Allowance allowance = Allowance.Find(IntendedOverlaps, name, a.name, b.name, label);
            bool known = allowance == null;
            if (known) allowance = Allowance.Find(KnownIssues, name, a.name, b.name, label);
            if (allowance == null)
            {
                _violations.Add($"{where}: {detail}");
                continue;
            }
            Transform behind = allowance.Behind == a.name ? a : b, front = behind == a ? b : a;
            if (!CheckStrip(elements, allowance, behind, front, bounds[behind], where, out string within)) continue;
            Report($"{name}: {allowance.Behind} / {allowance.Front} ({(known ? "known issue" : "intended")}: {allowance.Reason})",
                $"{label}: {detail}; {within}");
            if (known) _knownSeen.Add(allowance.Key);
        }
        foreach (Transform group in groups)
        {
            if (WorstOverlap(elements, group, group, true, out Element ea, out Element eb, out Rect overlap))
                _violations.Add($"{where}: {ea.Path} {Format(ea.Bounds)} overlaps {eb.Path} {Format(eb.Bounds)} by {overlap.width:0}x{overlap.height:0}");
        }
    }

    // A listed overlap may only reach the allowed strip of the element behind. Every pair of an element in front (a box, its
    // texts) and an element behind is checked on its own, so a text cannot hide in an allowed box. Returns whether all of them stay
    // in the strip (otherwise each one that does not is a violation) and, for the report, how deep the overlap goes and which texts
    // lie on the element behind.
    private bool CheckStrip(List<Element> elements, Allowance allowance, Transform behind, Transform front, Rect behindBounds,
        string where, out string within)
    {
        float allowed = allowance.Depth * behindBounds.height;
        float deepest = 0f;
        bool ok = true;
        var texts = new StringBuilder();
        foreach (Element back in elements)
        {
            if (back.Top != behind) continue;
            foreach (Element over in elements)
            {
                if (over.Top != front || !Intersect(back.Bounds, over.Bounds, out Rect overlap)) continue;
                // How far into the element behind the overlap reaches, from the allowed edge.
                float depth = allowance.Edge == Edge.Bottom ? overlap.yMax - behindBounds.yMin : behindBounds.yMax - overlap.yMin;
                deepest = Mathf.Max(deepest, depth);
                if (depth > allowed + Tolerance)
                {
                    ok = false;
                    _violations.Add($"{where}: {over.Path} {Format(over.Bounds)} covers {back.Path} {Format(back.Bounds)} " +
                                    $"{depth:0} units from its {allowance.EdgeName} edge, deeper than the allowed {allowance.Depth * 100f:0}% ({allowed:0})");
                }
                else if (over.Text != null || back.Text != null)
                    texts.Append($"; text {(over.Text != null ? over.Path : back.Path)} lies on {(over.Text != null ? back.Path : over.Path)} by {overlap.width:0}x{overlap.height:0}");
            }
        }
        within = $"{deepest:0} deep from the {allowance.EdgeName} edge of {allowance.Behind} (allowed {allowance.Depth * 100f:0}% = {allowed:0}){texts}";
        return ok;
    }

    // The largest overlap between an element of a and an element of b. Inside one group only text-text and button-button count.
    private static bool WorstOverlap(List<Element> elements, Transform a, Transform b, bool sameGroup, out Element worstA, out Element worstB, out Rect worst)
    {
        worstA = worstB = default;
        worst = default;
        float worstArea = 0f;
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i].Top != a) continue;
            for (int j = sameGroup ? i + 1 : 0; j < elements.Count; j++)
            {
                if (elements[j].Top != b) continue;
                Element ea = elements[i], eb = elements[j];
                if (sameGroup)
                {
                    bool texts = ea.Text != null && eb.Text != null;
                    bool buttons = ea.Selectable && eb.Selectable;
                    if (!texts && !buttons) continue;
                    if (ea.Transform.IsChildOf(eb.Transform) || eb.Transform.IsChildOf(ea.Transform)) continue;
                }
                if (!Intersect(ea.Bounds, eb.Bounds, out Rect overlap)) continue;
                float area = overlap.width * overlap.height;
                if (area <= worstArea) continue;
                worstArea = area;
                worstA = ea;
                worstB = eb;
                worst = overlap;
            }
        }
        return worstArea > 0f;
    }

    // Two rects overlap when they share more than the tolerance in both directions.
    private static bool Intersect(Rect a, Rect b, out Rect overlap)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin), xMax = Mathf.Min(a.xMax, b.xMax);
        float yMin = Mathf.Max(a.yMin, b.yMin), yMax = Mathf.Min(a.yMax, b.yMax);
        overlap = Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        return xMax - xMin > Tolerance && yMax - yMin > Tolerance;
    }

    // The intro and the outro show the same dialogue screen, so the same finding is kept once.
    private void Report(string key, string where)
    {
        if (!_reported.TryGetValue(key, out List<string> places)) _reported[key] = places = new List<string>();
        if (!places.Contains(where)) places.Add(where);
    }

    // Whether one of the two top-level elements is a listed frame of the other (and which).
    private static bool IsFramed(string screen, string a, string b, out bool aIsFrame)
    {
        aIsFrame = false;
        foreach (string[] frame in Frames)
        {
            if (frame[0] != screen) continue;
            string[] members = frame[2].Split(',');
            if (frame[1] == a && Array.IndexOf(members, b) >= 0) { aIsFrame = true; return true; }
            if (frame[1] == b && Array.IndexOf(members, a) >= 0) return true;
        }
        return false;
    }

    private enum Edge { Bottom, Top }

    // An overlap accepted only within a strip at one edge of the element behind: on Screen, the elements of Front may cover
    // Behind only up to Depth (a fraction of Behind's height) from its Edge, and only at the listed sizes (null: every size).
    private sealed class Allowance
    {
        public readonly string Screen, Behind, Front, Reason;
        public readonly Edge Edge;
        public readonly float Depth;
        private readonly string[] _sizes;

        public Allowance(string screen, string behind, string front, Edge edge, float depth, string sizes, string reason)
        {
            Screen = screen;
            Behind = behind;
            Front = front;
            Edge = edge;
            Depth = depth;
            _sizes = sizes?.Split(',');
            Reason = reason;
        }

        public string Key => Screen + "|" + Behind + "|" + Front;
        public string EdgeName => Edge == Edge.Bottom ? "bottom" : "top";

        public static Allowance Find(Allowance[] list, string screen, string a, string b, string label)
        {
            foreach (Allowance entry in list)
            {
                if (entry.Screen != screen) continue;
                if (!(entry.Behind == a && entry.Front == b) && !(entry.Behind == b && entry.Front == a)) continue;
                if (entry._sizes != null && Array.IndexOf(entry._sizes, label) < 0) continue;
                return entry;
            }
            return null;
        }

        public void AssertSound()
        {
            Assert.IsTrue(KeyElements.ContainsKey(Screen), "Unknown screen " + Screen);
            Assert.IsTrue(Depth > 0f && Depth <= 0.5f, $"{Key}: an allowance covers a strip of at most half the element behind.");
            Assert.IsFalse(string.IsNullOrEmpty(Reason), Key + " needs a reason.");
        }
    }

    private struct Element
    {
        public string Name;
        public string Path;
        public Transform Transform;
        public Transform Top;
        public Rect Bounds;
        public TMP_Text Text;
        public bool Selectable;
    }

    // Visible texts (their drawn glyphs), images and buttons of the screen in canvas units from its lower-left corner.
    // Full-screen backgrounds are left out; they are checked separately. A text is never left out: it must stay inside the safe
    // area and clear of other elements wherever it sits (SafeAreaFitter never treats a text as a background either).
    private static List<Element> Collect(Transform view, RectTransform canvas, SafeAreaFitter fitter)
    {
        var elements = new List<Element>();
        var seen = new HashSet<Transform>();
        foreach (Graphic graphic in view.GetComponentsInChildren<Graphic>())
        {
            // A text's extra glyph meshes (bold or fallback fonts) are measured with the text itself.
            if (graphic is TMP_SubMeshUI || !graphic.enabled || !IsVisible(graphic)) continue;
            Transform top = TopLevel(graphic.transform, view);
            var text = graphic as TMP_Text;
            if (top == null || (text == null && fitter.IsBackground((RectTransform)top))) continue;
            Rect rect;
            if (text != null)
            {
                if (!TextBounds(text, canvas, out rect)) continue;
            }
            else
                rect = DrawnRect(graphic, canvas);
            seen.Add(graphic.transform);
            elements.Add(new Element
            {
                Name = graphic.name, Path = PathOf(graphic.transform, view), Transform = graphic.transform, Top = top, Bounds = rect,
                Text = text, Selectable = graphic.TryGetComponent(out Selectable selectable) && selectable.enabled
            });
        }
        foreach (Selectable selectable in view.GetComponentsInChildren<Selectable>())
        {
            if (!selectable.enabled || seen.Contains(selectable.transform)) continue;
            Transform top = TopLevel(selectable.transform, view);
            if (top == null || fitter.IsBackground((RectTransform)top)) continue;
            elements.Add(new Element
            {
                Name = selectable.name, Path = PathOf(selectable.transform, view), Transform = selectable.transform, Top = top,
                Bounds = CanvasRect((RectTransform)selectable.transform, canvas), Selectable = true
            });
        }
        return elements;
    }

    private static bool IsVisible(Graphic graphic)
    {
        CanvasRenderer renderer = graphic.canvasRenderer;
        if (renderer.cull) return false;
        float alpha = graphic.color.a * renderer.GetAlpha() * renderer.GetInheritedAlpha();
        Rect rect = graphic.rectTransform.rect;
        return alpha > 0.02f && rect.width > 0.5f && rect.height > 0.5f;
    }

    private static Transform TopLevel(Transform node, Transform view)
    {
        for (Transform current = node; current != null; current = current.parent)
            if (current.parent == view) return current;
        return null;
    }

    private static string PathOf(Transform node, Transform view)
    {
        string path = node.name;
        for (Transform current = node.parent; current != null && current != view; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    // The ink of a text: the union of its visible glyph boxes (glyph metrics, without the font's line height or SDF padding),
    // not its whole rect. Empty texts draw nothing.
    private static bool TextBounds(TMP_Text text, RectTransform canvas, out Rect rect)
    {
        rect = default;
        if (string.IsNullOrEmpty(text.text)) return false;
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        if (info == null || info.characterCount == 0) return false;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible || character.textElement == null || character.textElement.glyph == null) continue;
            var metrics = character.textElement.glyph.metrics;
            float left = character.origin + metrics.horizontalBearingX * character.scale;
            float top = character.baseLine + metrics.horizontalBearingY * character.scale;
            xMin = Mathf.Min(xMin, left);
            xMax = Mathf.Max(xMax, left + metrics.width * character.scale);
            yMax = Mathf.Max(yMax, top);
            yMin = Mathf.Min(yMin, top - metrics.height * character.scale);
        }
        if (xMax <= xMin || yMax <= yMin) return false;
        rect = LocalToCanvas(text.rectTransform, Rect.MinMaxRect(xMin, yMin, xMax, yMax), canvas);
        return true;
    }

    // Where an image is drawn: its rect, narrowed to the sprite's aspect when the image preserves it (as Image does).
    private static Rect DrawnRect(Graphic graphic, RectTransform canvas)
    {
        RectTransform transform = graphic.rectTransform;
        Rect local = transform.rect;
        if (graphic is Image image && image.preserveAspect && image.sprite != null &&
            (image.type == Image.Type.Simple || image.type == Image.Type.Filled))
        {
            Vector2 sprite = image.sprite.rect.size;
            float spriteRatio = sprite.x / sprite.y, rectRatio = local.width / local.height;
            if (spriteRatio > rectRatio)
            {
                float height = local.width / spriteRatio;
                local.y += (local.height - height) * transform.pivot.y;
                local.height = height;
            }
            else
            {
                float width = local.height * spriteRatio;
                local.x += (local.width - width) * transform.pivot.x;
                local.width = width;
            }
        }
        return LocalToCanvas(transform, local, canvas);
    }

    private static Rect CanvasRect(RectTransform rect, RectTransform canvas) => LocalToCanvas(rect, rect.rect, canvas);

    private static Rect LocalToCanvas(Transform transform, Rect local, RectTransform canvas)
    {
        Corners[0] = transform.TransformPoint(new Vector3(local.xMin, local.yMin, 0f));
        Corners[1] = transform.TransformPoint(new Vector3(local.xMin, local.yMax, 0f));
        Corners[2] = transform.TransformPoint(new Vector3(local.xMax, local.yMax, 0f));
        Corners[3] = transform.TransformPoint(new Vector3(local.xMax, local.yMin, 0f));
        return ToCanvas(canvas);
    }

    private static Rect ToCanvas(RectTransform canvas)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in Corners)
        {
            Vector3 local = canvas.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        Rect canvasRect = canvas.rect;
        return Rect.MinMaxRect(min.x - canvasRect.xMin, min.y - canvasRect.yMin, max.x - canvasRect.xMin, max.y - canvasRect.yMin);
    }

    private static bool Contains(Rect outer, Rect inner, float tolerance) =>
        inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance &&
        inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;

    private static string Format(Rect rect) => $"[{rect.xMin:0},{rect.yMin:0} → {rect.xMax:0},{rect.yMax:0}]";

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
        _canvas = _ui.GetComponent<Canvas>();
        _scaler = _ui.GetComponent<CanvasScaler>();
        Assert.IsNotNull(_canvas);
        Assert.IsNotNull(_scaler);
        yield return WaitFor(GameState.Ready);
        // The fade in from black is over before the Start screen is measured and pictured.
        var transition = _ui.GetComponent<ScreenTransition>();
        float deadline = Time.realtimeSinceStartup + 3f;
        while (transition != null && transition.Opacity > 0f && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }

    // Lays the UI canvas out at a given pixel size by drawing it with a camera into a render texture of that size
    // (Screen Space - Camera), and optionally saves a picture: the scene camera's view behind, the UI on top.
    private sealed class Sizer : IDisposable
    {
        private readonly Canvas _canvas;
        private readonly RenderMode _mode;
        private readonly Camera _previousCamera;
        private readonly float _planeDistance;
        private readonly string _captureDirectory;
        private readonly Camera _uiCamera;
        private readonly Dictionary<Vector2Int, RenderTexture> _targets = new Dictionary<Vector2Int, RenderTexture>();
        private readonly Dictionary<Vector2Int, RenderTexture> _scenes = new Dictionary<Vector2Int, RenderTexture>();
        private RawImage _backdrop;
        private Vector2Int _size;

        public Sizer(Canvas canvas, string captureDirectory)
        {
            _canvas = canvas;
            _mode = canvas.renderMode;
            _previousCamera = canvas.worldCamera;
            _planeDistance = canvas.planeDistance;
            _captureDirectory = string.IsNullOrEmpty(captureDirectory) ? null : captureDirectory;
            var cameraObject = new GameObject("ResolutionCheckCamera");
            _uiCamera = cameraObject.AddComponent<Camera>();
            _uiCamera.enabled = false;
            _uiCamera.orthographic = true;
            _uiCamera.clearFlags = CameraClearFlags.SolidColor;
            _uiCamera.backgroundColor = Color.black;
            _uiCamera.cullingMask = 1 << canvas.gameObject.layer;
            _uiCamera.nearClipPlane = 0.1f;
            _uiCamera.farClipPlane = 10f;
            if (_captureDirectory != null) Directory.CreateDirectory(_captureDirectory);
        }

        public void SetSize(Vector2Int size)
        {
            _size = size;
            _uiCamera.targetTexture = Target(_targets, size);
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _uiCamera;
            _canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
        }

        public void Restore()
        {
            _canvas.renderMode = _mode;
            _canvas.worldCamera = _previousCamera;
            _canvas.planeDistance = _planeDistance;
            _uiCamera.targetTexture = null;
            Canvas.ForceUpdateCanvases();
        }

        public void Capture(string name)
        {
            if (_captureDirectory == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Camera scene = Camera.main;
            if (scene != null)
            {
                RenderTexture sceneTarget = Target(_scenes, _size);
                RenderTexture previous = scene.targetTexture;
                scene.targetTexture = sceneTarget;
                scene.Render();
                scene.targetTexture = previous;
                Backdrop().texture = sceneTarget;
                _backdrop.enabled = true;
                Canvas.ForceUpdateCanvases();
            }
            _uiCamera.Render();
            if (_backdrop != null) _backdrop.enabled = false;
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = _uiCamera.targetTexture;
            var picture = new Texture2D(_size.x, _size.y, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, _size.x, _size.y), 0, 0);
            picture.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(Path.Combine(_captureDirectory, name + ".png"), picture.EncodeToPNG());
            Object.Destroy(picture);
        }

        public void WriteReport(string report)
        {
            if (_captureDirectory != null) File.WriteAllText(Path.Combine(_captureDirectory, "resolution_report.txt"), report);
        }

        // A picture of the scene behind the UI, drawn first so the UI composes over it as on screen (no post-processing on the UI).
        private RawImage Backdrop()
        {
            if (_backdrop != null) return _backdrop;
            var backdrop = new GameObject("ResolutionCheckBackdrop", typeof(RectTransform), typeof(RawImage));
            backdrop.layer = _canvas.gameObject.layer;
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(_canvas.transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _backdrop = backdrop.GetComponent<RawImage>();
            _backdrop.raycastTarget = false;
            return _backdrop;
        }

        private static RenderTexture Target(Dictionary<Vector2Int, RenderTexture> pool, Vector2Int size)
        {
            if (!pool.TryGetValue(size, out RenderTexture texture))
            {
                texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                texture.Create();
                pool[size] = texture;
            }
            return texture;
        }

        public void Dispose()
        {
            if (_canvas != null) Restore();
            if (_backdrop != null) Object.Destroy(_backdrop.gameObject);
            if (_uiCamera != null) Object.Destroy(_uiCamera.gameObject);
            foreach (RenderTexture texture in _targets.Values) { texture.Release(); Object.Destroy(texture); }
            foreach (RenderTexture texture in _scenes.Values) { texture.Release(); Object.Destroy(texture); }
            _targets.Clear();
            _scenes.Clear();
        }
    }
}

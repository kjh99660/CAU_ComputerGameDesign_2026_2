using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 성능 도구를 실제 씬에서 확인한다: F3 오버레이는 게임 상태와 클릭을 건드리지 않고, F4 이웃 검색 전환과 F5 실험용 상한은
// 판을 멈추지 않으며 Reset이 되돌린다. 라운드 기록은 Pause에서 쉬고, Result에서 쓰이고, Retry에서 버려진다.
// 벤치마크는 요청만으로 한 판을 끝까지 돌린다. 기록은 테스트마다 임시 폴더에 쓰고 지운다.
public sealed class PerfToolsSceneTests
{
    // 격자와 전수를 견줄 때 필요한 적 수. 생성 단계(초반 상한)가 바뀌어도 맞도록 작게 둔다.
    private const int MinimumCrowd = 12;

    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private PerfRuntime _perf;
    private int _oldCaptureFramerate;
    private string _output;

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
        _output = Path.Combine(Application.temporaryCachePath, "PerfToolsSceneTests");
        if (Directory.Exists(_output)) Directory.Delete(_output, true);
    }

    [TearDown]
    public void TearDown()
    {
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        if (Directory.Exists(_output)) Directory.Delete(_output, true);
    }

    [UnityTest]
    public IEnumerator TheOverlayTogglesInAnyStateWithoutChangingTheGameOrTakingClicks()
    {
        yield return Load();
        Assert.IsFalse(_perf.OverlayVisible, "Hidden by default.");
        Assert.IsNull(_perf.OverlayCanvas, "Nothing is built before it is first shown.");
        int tick = _state.Tick;

        _perf.ToggleOverlay();
        Assert.IsTrue(_perf.OverlayVisible);
        yield return Frames(3);
        Assert.AreEqual(GameState.Ready, _state.CurrentState);
        Assert.AreEqual(tick, _state.Tick);
        EnemyPerfProbe probe = _enemies.Perf;
        AssertNoMeasurementFlags(probe, "Showing the overlay");
        Assert.IsTrue(probe.LiveTiming, "The overlay times the enemy AI with its own switch.");

        Canvas canvas = _perf.OverlayCanvas;
        Assert.IsNotNull(canvas);
        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        foreach (Canvas other in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (other != canvas && other.isRootCanvas)
                Assert.Less(other.sortingOrder, canvas.sortingOrder, other.name + " must draw below the overlay.");
        Assert.IsNull(canvas.GetComponent<GraphicRaycaster>(), "Without a raycaster the overlay never takes clicks.");
        Assert.IsFalse(canvas.GetComponent<CanvasGroup>().blocksRaycasts);
        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
            Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not be a raycast target.");
        // 오버레이 한가운데를 눌러도 맞는 것은 그 아래의 게임 UI뿐이다.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            var panel = (RectTransform)canvas.transform.GetChild(0);
            var pointer = new PointerEventData(eventSystem) { position = panel.TransformPoint(panel.rect.center) };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            foreach (RaycastResult hit in hits)
                Assert.IsFalse(hit.gameObject.transform.IsChildOf(canvas.transform), "The overlay took a click: " + hit.gameObject.name);
        }

        yield return new WaitForSecondsRealtime(0.4f);
        string text = _perf.OverlayText;
        StringAssert.Contains("FPS ", text);
        StringAssert.Contains("(last 1 s)", text);
        StringAssert.Contains(" ms worst", text);
        StringAssert.Contains("Enemies " + _enemies.ActiveEnemyCount + " / 100 cap", text);
        StringAssert.Contains("spatial grid", text);
        StringAssert.Contains("Draw calls ", text);
        StringAssert.Contains("State Ready", text);
        StringAssert.Contains("Round recording off", text);
        StringAssert.Contains("Last round: none yet", text);
        StringAssert.Contains("hold fn with F3", text);
        foreach (char c in text)
            Assert.Less((int)c, 128, "The overlay font only has ASCII.");
        foreach (string line in text.Split('\n'))
            Assert.LessOrEqual(VisibleLength(line), 50, "Each overlay line fits the panel: " + line);

        // Battle과 Paused에서도 켜고 꺼도 상태와 Tick은 그대로다.
        yield return EnterBattle();
        tick = _state.Tick;
        _perf.ToggleOverlay();
        Assert.IsFalse(_perf.OverlayVisible);
        Assert.IsFalse(probe.LiveTiming, "Hiding the overlay stops its timing.");
        yield return Frames(3);
        _perf.ToggleOverlay();
        Assert.IsTrue(_perf.OverlayVisible);
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(GameState.Battle, _state.CurrentState);
        StringAssert.Contains("State Battle", _perf.OverlayText);
        StringAssert.Contains("Round recording on", _perf.OverlayText);
        StringAssert.Contains("AI update ", _perf.OverlayText);
        StringAssert.DoesNotContain("AI update n/a", _perf.OverlayText, "The AI time is measured while the overlay is shown.");
        AssertNoMeasurementFlags(probe, "Showing the overlay in Battle");
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _perf.ToggleOverlay();
        yield return Frames(2);
        _perf.ToggleOverlay();
        yield return Frames(2);
        Assert.AreEqual(GameState.Paused, _state.CurrentState);
        StringAssert.Contains("Round recording held", _perf.OverlayText);
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        Assert.AreEqual(tick, _state.Tick, "Toggling the overlay never resets the round.");
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, _perf.SearchMode, "Showing the overlay changes no setting.");
        Assert.AreEqual(100, _perf.EnemyCap);
        AssertNoMeasurementFlags(probe, "Toggling the overlay");
    }

    [UnityTest]
    public IEnumerator SwitchingTheSearchKeepsTheRoundRunningAndResetReturnsToTheGrid()
    {
        yield return Load();
        yield return EnterBattle();
        _perf.SetOverlayVisible(true);
        EnemyPerfProbe probe = _enemies.Perf;
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, probe.Mode);
        // 격자와 전수의 차이가 분명하도록 적이 12명 넘게 나올 때까지 기다린다(게임의 생성 규칙 그대로).
        yield return Until(() => _enemies.ActiveEnemyCount >= MinimumCrowd, 1800);
        yield return ExpectCandidates(grid: true);

        _perf.ToggleSearchMode();
        Assert.AreEqual(EnemyCandidateMode.BruteForce, probe.Mode);
        Assert.AreEqual(EnemyCandidateMode.BruteForce, _perf.SearchMode);
        float clock = _combat.RoundRemaining;
        yield return ExpectCandidates(grid: false);
        Assert.AreEqual(GameState.Battle, _state.CurrentState);
        Assert.Less(_combat.RoundRemaining, clock, "The round clock keeps running in brute force.");
        yield return Frames(2);
        StringAssert.Contains("brute force", _perf.OverlayText);

        _perf.ToggleSearchMode();
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, probe.Mode);
        yield return ExpectCandidates(grid: true);

        // 전수로 둔 채 Pause의 Retry를 하면 공간 분할로 돌아오고, 다음 판의 격자도 제대로 돈다.
        _perf.ToggleSearchMode();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, probe.Mode, "Reset returns to the spatial grid.");
        yield return Frames(2);
        StringAssert.Contains("spatial grid", _perf.OverlayText);
        yield return EnterBattle();
        yield return Until(() => _enemies.ActiveEnemyCount >= MinimumCrowd, 1800);
        yield return ExpectCandidates(grid: true);
    }

    [UnityTest]
    public IEnumerator TheEnemyCapCyclesForExperimentsAndResetRestoresTheGameCap()
    {
        yield return Load();
        Assert.AreEqual(100, _enemies.Settings.MaximumEnemyCount, "Normal play is capped at 100 enemies.");
        Assert.AreEqual(100, _perf.EnemyCap);
        yield return EnterBattle();
        _perf.SetOverlayVisible(true);

        _perf.CycleEnemyCap();
        Assert.AreEqual(200, _perf.EnemyCap);
        Assert.AreEqual(200, _enemies.Perf.ExperimentMaximumEnemies);
        Assert.AreEqual(100, _enemies.Settings.MaximumEnemyCount, "The settings asset is never changed.");
        yield return Frames(2);
        StringAssert.Contains(" / 200 cap", _perf.OverlayText);
        StringAssert.Contains("experiment", _perf.OverlayText);
        _perf.CycleEnemyCap();
        Assert.AreEqual(300, _perf.EnemyCap);
        _perf.CycleEnemyCap();
        Assert.AreEqual(100, _perf.EnemyCap);
        Assert.AreEqual(0, _enemies.Perf.ExperimentMaximumEnemies);
        _perf.CycleEnemyCap();
        Assert.AreEqual(200, _perf.EnemyCap);
        Assert.AreEqual(GameState.Battle, _state.CurrentState);

        // Pause의 Retry.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(100, _perf.EnemyCap, "Reset restores the game cap.");
        Assert.AreEqual(0, _enemies.Perf.ExperimentMaximumEnemies);

        // Pause의 Quit와 Result의 Retry도 같은 Reset이다.
        _perf.CycleEnemyCap();
        yield return EnterBattle();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new QuitToStartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(100, _perf.EnemyCap);
        _perf.CycleEnemyCap();
        _perf.CycleEnemyCap();
        Assert.AreEqual(300, _perf.EnemyCap);
        yield return EnterBattle();
        yield return FinishRound();
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(100, _perf.EnemyCap);
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, _perf.SearchMode);
    }

    [UnityTest]
    public IEnumerator TheRecorderHoldsInPauseAndWritesASummaryWhenTheRoundReachesResult()
    {
        yield return Load();
        _perf.OutputDirectory = _output;
        Assert.IsFalse(_perf.IsRecording);
        yield return Frames(10);
        Assert.AreEqual(0, _perf.RecordedFrames, "Ready is not recorded.");

        yield return EnterBattle();
        Assert.IsTrue(_perf.IsRecording);
        yield return Frames(30);
        Assert.GreaterOrEqual(_perf.RecordedFrames, 29);

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        long held = _perf.RecordedFrames;
        yield return Frames(20);
        Assert.AreEqual(held, _perf.RecordedFrames, "Pause holds the recording.");
        Assert.IsTrue(_perf.IsRecording);
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        yield return Frames(10);
        Assert.Greater(_perf.RecordedFrames, held);

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        long beforeFinishing = _perf.RecordedFrames;
        yield return Frames(10);
        Assert.Greater(_perf.RecordedFrames, beforeFinishing, "Finishing is recorded.");
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        long recorded = _perf.RecordedFrames;
        yield return Frames(5);
        Assert.AreEqual(recorded, _perf.RecordedFrames, "The outro dialogue is not recorded.");
        Assert.IsFalse(Directory.Exists(_output), "Nothing is written before Result.");

        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        Assert.IsFalse(_perf.IsRecording);
        Assert.IsTrue(_perf.LastRoundSaved);
        Assert.AreEqual(2, _perf.LastWrittenFiles.Count);
        string csv = _perf.LastWrittenFiles[0];
        string txt = _perf.LastWrittenFiles[1];
        Assert.AreEqual(Path.GetFullPath(_output), Path.GetFullPath(Path.GetDirectoryName(csv)));
        StringAssert.EndsWith(".csv", csv);
        StringAssert.EndsWith(".txt", txt);
        Assert.AreEqual(2, Directory.GetFiles(_output).Length, "One CSV and one text file per round.");

        string[] lines = File.ReadAllText(csv, Encoding.UTF8).TrimEnd('\n').Split('\n');
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual(RoundPerfRecord.CsvHeader, lines[0]);
        Dictionary<string, string> row = Columns(lines[0], lines[1]);
        Assert.AreEqual(recorded.ToString(), row["frames"]);
        Assert.AreEqual("result", row["end_reason"]);
        Assert.AreEqual("0", row["benchmark"]);
        Assert.AreEqual("SpatialGrid", row["search_mode"]);
        Assert.AreEqual("100", row["enemy_cap_max"]);
        Assert.AreEqual("1", row["is_editor"]);
        Assert.Greater(double.Parse(row["avg_fps"], System.Globalization.CultureInfo.InvariantCulture), 0d);
        Assert.Greater(double.Parse(row["one_percent_low_fps"], System.Globalization.CultureInfo.InvariantCulture), 0d);
        Assert.IsNotEmpty(row["device_model"]);
        Assert.IsNotEmpty(row["graphics_device"]);
        string summary = File.ReadAllText(txt, Encoding.UTF8);
        StringAssert.Contains("Average FPS: ", summary);
        StringAssert.Contains("1% low FPS: ", summary);
        StringAssert.Contains("Minimum FPS: ", summary);
        StringAssert.Contains("Frames: " + recorded + " in ", summary);
        StringAssert.Contains("measured in the Unity Editor", summary);

        _perf.SetOverlayVisible(true);
        yield return Frames(2);
        StringAssert.Contains("Last round: avg ", _perf.OverlayText);
        StringAssert.Contains("Round recording written", _perf.OverlayText);
        foreach (string line in _perf.OverlayText.Split('\n'))
            Assert.LessOrEqual(VisibleLength(line), 50, "Each overlay line fits the panel: " + line);

        // Result의 Retry는 쓴 파일을 그대로 두고 메모리의 기록만 비운다.
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, _perf.RecordedFrames);
        Assert.IsTrue(File.Exists(csv));
        StringAssert.Contains("Last round: avg ", _perf.OverlayText);
    }

    [UnityTest]
    public IEnumerator RetryAndQuitDiscardTheRoundRecording()
    {
        yield return Load();
        _perf.OutputDirectory = _output;
        yield return EnterBattle();
        yield return Frames(20);
        Assert.Greater(_perf.RecordedFrames, 0);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.IsFalse(_perf.IsRecording);
        Assert.AreEqual(0, _perf.RecordedFrames, "Retry discards the recording.");

        yield return EnterBattle();
        yield return Frames(10);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new QuitToStartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, _perf.RecordedFrames, "Quit to the start screen discards it too.");
        Assert.IsFalse(Directory.Exists(_output), "A discarded round writes nothing.");

        // 다음 판은 0에서 다시 센다.
        yield return EnterBattle();
        yield return Frames(5);
        Assert.That(_perf.RecordedFrames, NUnit.Framework.Is.InRange(1L, 8L));
        Assert.IsNull(_perf.LastRecord);
    }

    // A/B 측정기(-rebootEnemyAb)가 도는 플레이어는 라운드 기록을 끈다(PerfBenchmark.IsEnemyAbRun, EditMode에서 확인).
    // 측정기가 끝나며 Battle에 남은 판에서 프로그램을 닫아도 쓰지 않는다. 여기서는 같은 스위치를 직접 끈다.
    [UnityTest]
    public IEnumerator NothingIsRecordedOrWrittenWhileRoundRecordingIsDisabled()
    {
        yield return Load();
        _perf.OutputDirectory = _output;
        Assert.IsTrue(_perf.RoundRecordingEnabled, "Recording is on by default.");
        _perf.RoundRecordingEnabled = false;
        _perf.SetOverlayVisible(true);
        yield return EnterBattle();
        yield return Frames(20);
        Assert.IsFalse(_perf.IsRecording);
        Assert.AreEqual(0, _perf.RecordedFrames);
        StringAssert.Contains("Round recording disabled", _perf.OverlayText);
        yield return FinishRound();
        Assert.IsFalse(_perf.IsRecording);
        Assert.IsNull(_perf.LastRecord);
        Assert.AreEqual(0, _perf.LastWrittenFiles.Count);
        Assert.IsFalse(Directory.Exists(_output), "Nothing is written.");

        // 다시 켜면 다음 판부터 기록한다.
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        _perf.RoundRecordingEnabled = true;
        yield return EnterBattle();
        yield return Frames(5);
        Assert.IsTrue(_perf.IsRecording);
        Assert.Greater(_perf.RecordedFrames, 0);
    }

    // 기록 폴더를 만들 수 없으면(권한, 디스크 공간 등) 오버레이가 "written" 대신 저장되지 않았다고 알린다.
    [UnityTest]
    public IEnumerator ARoundThatCannotBeWrittenIsShownAsNotSaved()
    {
        yield return Load();
        Directory.CreateDirectory(_output);
        string blocker = Path.Combine(_output, "not-a-folder");
        File.WriteAllText(blocker, "A file where the output folder would be, so the folder cannot be made.");
        _perf.OutputDirectory = Path.Combine(blocker, "perf");
        _perf.SetOverlayVisible(true);
        yield return EnterBattle();
        yield return Frames(10);
        yield return FinishRound();
        Assert.IsFalse(_perf.IsRecording);
        Assert.IsNotNull(_perf.LastRecord, "The round is still summarised.");
        Assert.AreEqual(0, _perf.LastWrittenFiles.Count);
        Assert.IsFalse(_perf.LastRoundSaved);
        Assert.IsFalse(Directory.Exists(_perf.OutputDirectory));
        yield return Frames(2);
        string text = _perf.OverlayText;
        StringAssert.Contains("NOT SAVED", text);
        StringAssert.DoesNotContain("Round recording written", text);
        StringAssert.Contains("frames, NOT SAVED (see the log)", text);
        foreach (string line in text.Split('\n'))
            Assert.LessOrEqual(VisibleLength(line), 50, "Each overlay line fits the panel: " + line);
    }

    [UnityTest]
    public IEnumerator TheBenchmarkPlaysARoundThroughRequestsAndWritesTheRecording()
    {
        var settings = Resources.Load<CombatSettings>("CombatSettings");
        float duration = settings.roundDuration;
        float finishing = settings.finishingDuration;
        bool runInBackground = Application.runInBackground;
        try
        {
            settings.roundDuration = 1.5f;
            settings.finishingDuration = 0.3f;
            yield return Load();
            _perf.OutputDirectory = _output;
            _perf.BeginBenchmark(false, 200, EnemyCandidateMode.BruteForce);
            Assert.IsTrue(_perf.BenchmarkRunning);
            Assert.IsTrue(_perf.OverlayVisible, "The benchmark shows the overlay.");
            Assert.IsTrue(Application.runInBackground, "The benchmark keeps running without focus.");
            Assert.AreEqual(200, _perf.EnemyCap);
            Assert.AreEqual(EnemyCandidateMode.BruteForce, _perf.SearchMode);

            var visited = new List<GameState> { _state.CurrentState };
            EnemyPerfProbe probe = _enemies.Perf;
            bool measurementFlagSeen = false;
            float deadline = Time.realtimeSinceStartup + 90f;
            while (!_perf.BenchmarkFinished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (visited[visited.Count - 1] != _state.CurrentState)
                    visited.Add(_state.CurrentState);
                // 오버레이가 켜진 채 도는 벤치마크도 A/B 측정 판 표시는 켜지 않는다(적 생성은 실제 판과 같아야 한다).
                if (probe.MeasureTime || probe.ShadowVerify)
                    measurementFlagSeen = true;
            }
            Assert.IsFalse(measurementFlagSeen, "The benchmark never turns on MeasureTime or ShadowVerify.");
            Assert.IsTrue(_perf.BenchmarkFinished, "The benchmark did not reach Result in time.");
            Assert.IsTrue(_perf.BenchmarkSucceeded);
            Assert.IsFalse(_perf.BenchmarkRunning);
            CollectionAssert.AreEqual(new[]
            {
                GameState.Ready, GameState.IntroDialogue, GameState.Countdown, GameState.Battle,
                GameState.Finishing, GameState.OutroDialogue, GameState.Result,
            }, visited, "The benchmark goes through every state of a normal round.");
            Assert.AreEqual(runInBackground, Application.runInBackground, "The benchmark restores runInBackground.");
            Assert.AreEqual(1f, Time.timeScale);

            Assert.AreEqual(2, _perf.LastWrittenFiles.Count);
            string[] lines = File.ReadAllText(_perf.LastWrittenFiles[0], Encoding.UTF8).TrimEnd('\n').Split('\n');
            Dictionary<string, string> row = Columns(lines[0], lines[1]);
            Assert.AreEqual("1", row["benchmark"]);
            Assert.AreEqual("200", row["enemy_cap_max"]);
            Assert.AreEqual("100", row["enemy_cap_in_settings"]);
            Assert.AreEqual("BruteForce", row["search_mode"]);
            StringAssert.Contains("Benchmark mode", File.ReadAllText(_perf.LastWrittenFiles[1], Encoding.UTF8));
        }
        finally
        {
            settings.roundDuration = duration;
            settings.finishingDuration = finishing;
            Application.runInBackground = runInBackground;
        }
    }

    // 실제 키 입력 경로: F3는 언제나, F4와 F5는 오버레이가 보일 때만 동작한다.
    [UnityTest]
    public IEnumerator FunctionKeysGoThroughTheInputSystem()
    {
        yield return Load();
        InputSettings settings = InputSystem.settings;
        InputSettings.BackgroundBehavior oldBackground = settings.backgroundBehavior;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("PerfTestKeyboard");
        try
        {
            yield return Frames(3);
            yield return Press(keyboard, Key.F4);
            yield return Press(keyboard, Key.F5);
            Assert.IsFalse(_perf.OverlayVisible);
            Assert.AreEqual(EnemyCandidateMode.SpatialGrid, _perf.SearchMode, "F4 does nothing while the overlay is hidden.");
            Assert.AreEqual(100, _perf.EnemyCap, "F5 does nothing while the overlay is hidden.");

            yield return Press(keyboard, Key.F3);
            Assert.IsTrue(_perf.OverlayVisible);
            yield return Press(keyboard, Key.F4);
            Assert.AreEqual(EnemyCandidateMode.BruteForce, _perf.SearchMode);
            yield return Press(keyboard, Key.F5);
            Assert.AreEqual(200, _perf.EnemyCap);
            yield return Press(keyboard, Key.F3);
            Assert.IsFalse(_perf.OverlayVisible);
            Assert.AreEqual(GameState.Ready, _state.CurrentState);
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            settings.backgroundBehavior = oldBackground;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode = oldEditorBehavior;
#endif
        }
    }

    // ---------------------------------------------------------------- 도우미

    // A/B 측정기가 켜는 값들. 적 쪽 코드는 이것으로 "측정 판"을 가려 생성 방식을 바꿀 수 있으므로, 오버레이는 켜지 않는다
    // (실험용 상한은 F5와 -rebootBenchmarkCap으로 일부러 걸 때만 쓴다).
    private static void AssertNoMeasurementFlags(EnemyPerfProbe probe, string when)
    {
        Assert.IsFalse(probe.MeasureTime, when + " must not turn on the A/B measurement timing.");
        Assert.IsFalse(probe.ShadowVerify, when + " must not turn on the shadow comparison.");
        Assert.AreEqual(0, probe.ExperimentMaximumEnemies, when + " must not set an experiment cap.");
    }

    // 몇 프레임 동안 AI 질의마다 본 후보 수를 확인한다. 전수 방식은 질의마다 활성 적 전부를 본다(질의 수 = 적 수).
    private IEnumerator ExpectCandidates(bool grid)
    {
        EnemyPerfProbe probe = _enemies.Perf;
        int checkedFrames = 0;
        long candidateSum = 0, bruteSum = 0;
        for (int i = 0; i < 30; i++)
        {
            EnemyPerfCounters before = probe.Counters;
            yield return null;
            EnemyPerfCounters after = probe.Counters;
            long queries = after.AiQueries - before.AiQueries;
            long candidates = after.AiCandidates - before.AiCandidates;
            if (queries < MinimumCrowd)
                continue;
            checkedFrames++;
            candidateSum += candidates;
            bruteSum += queries * queries;
            if (!grid)
                Assert.AreEqual(queries * queries, candidates, "Brute force looks at every enemy for every query.");
        }
        Assert.Greater(checkedFrames, 10, "The enemy AI must keep running.");
        if (grid)
            Assert.Less(candidateSum * 2, bruteSum, "The grid looks at far fewer enemies than brute force would.");
    }

    // 서식 태그(<b>, <color=...> 등)를 뺀, 화면에 보이는 글자 수.
    private static int VisibleLength(string line)
    {
        int length = 0;
        bool inTag = false;
        foreach (char c in line)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) length++;
        }
        return length;
    }

    private static IEnumerator Press(Keyboard keyboard, Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    private IEnumerator FinishRound()
    {
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
    }

    private static Dictionary<string, string> Columns(string header, string row)
    {
        string[] names = header.Split(',');
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < row.Length && row[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        Assert.AreEqual(names.Length, cells.Count, "One value per column.");
        var values = new Dictionary<string, string>();
        for (int i = 0; i < names.Length; i++)
            values[names[i]] = cells[i];
        return values;
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        _perf = Object.FindFirstObjectByType<PerfRuntime>();
        Assert.IsNotNull(_perf, "CoreBootstrap attaches the performance tools.");
        yield return WaitFor(GameState.Ready);
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

    private static IEnumerator Until(System.Func<bool> condition, int maximumFrames)
    {
        for (int i = 0; i < maximumFrames && !condition(); i++)
            yield return null;
        Assert.IsTrue(condition(), "Timed out after " + maximumFrames + " frames.");
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }
}

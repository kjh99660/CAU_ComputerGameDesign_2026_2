using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

// 시연 PC 성능 확인 도구의 Unity Adapter. CoreBootstrap이 붙이고 Reset 참여자로 등록한다.
// - F3: 성능 오버레이를 켜고 끈다(어느 상태에서나). 게임 상태와 입력에는 손대지 않는다.
// - F4: 적 이웃 검색을 공간 분할 <-> 전수로 바꾼다. F5: 실험용 적 상한 100 -> 200 -> 300 -> 100.
//   둘 다 오버레이가 보일 때만 키로 동작한다(모르고 눌러 보이지 않게 바뀌는 일을 막는다).
//   Retry/Reset은 공간 분할과 게임 설정 상한(100명)으로 되돌린다.
// - 라운드 FPS 기록: Battle과 Finishing의 매 프레임 실제 시간을 미리 잡은 버퍼에 쌓는다. Result에 닿으면(또는 플레이어를
//   판 도중에 닫으면) 요약을 PerfOutput 폴더에 CSV와 글로 쓴다. Pause 동안은 쉬고, Retry/Reset은 기록을 버린다.
//   A/B 측정기("-rebootEnemyAb")가 도는 플레이어에서는 그 측정 판들이 게임 판이 아니므로 기록하지 않는다.
// - "-rebootBenchmark"(PerfBenchmark): 게임과 같은 요청 경로로 한 판을 자동으로 돌리고 기록을 쓴 뒤 종료한다.
// 상태는 GameStateManager만 바꾼다. 이 컴포넌트는 알림을 받고, 벤치마크에서도 요청만 보낸다.
// 오버레이는 EnemyPerfProbe.LiveTiming으로만 AI 시간을 잰다. A/B 측정 판 표시(MeasureTime, ShadowVerify)는 건드리지 않는다.
[DisallowMultipleComponent]
public sealed class PerfRuntime : MonoBehaviour, IRoundResettable
{
    public const string DrawCallsCounter = "Draw Calls Count";
    // F5가 도는 실험용 적 상한(0은 게임 설정). AGENTS.md 13절: 일반 플레이 100명, 성능 실험에서만 200명과 300명.
    private static readonly int[] ExperimentCaps = { 0, 200, 300 };
    private static readonly string[] StateNames = Enum.GetNames(typeof(GameState));
    private const float SampleSeconds = 0.25f;
    private const int RingCapacity = 2048;
    private const int DrawCallSamples = 16;
    private const int TextCapacity = 1400;
    private const int BenchmarkResendFrames = 90;
    private const float BenchmarkTimeoutSeconds = 900f;

    private GameStateManager _stateManager;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private bool _configured;

    // 라운드 기록.
    private RoundFrameRecorder _frames;
    private GameState _state = GameState.Initializing;
    private int _tick;
    private bool _recordRounds = true;
    private bool _roundStarted, _roundFinished, _focusLost, _usedGrid, _usedBrute;
    private int _capMin, _capMax;

    // 오버레이.
    private readonly PerfOverlayView _overlay = new PerfOverlayView();
    private readonly float[] _ring = new float[RingCapacity];
    private PerfTextBuffer _text;
    private int _ringHead, _ringCount;
    private float _sampleElapsed;
    private int _framesSinceSample;
    private EnemyPerfCounters _lastCounters;
    private ProfilerRecorder _drawCalls;
    private string _lastRoundLine;

    // 벤치마크.
    private bool _benchmarkActive, _benchmarkQuitWhenDone, _benchmarkRoundWritten;
    private int _benchmarkCap;
    private EnemyCandidateMode _benchmarkSearch;
    private bool _savedRunInBackground, _runInBackgroundChanged;
    private GameState _benchmarkRequestState = GameState.Initializing;
    private int _benchmarkRequestTick = -1;
    private int _benchmarkFramesSinceRequest;
    private float _benchmarkElapsed;

    // CoreBootstrap에서 한 줄로 붙인다: 컴포넌트를 더하고, 연결하고, Reset 참여자로 등록한다.
    public static PerfRuntime Attach(GameObject host, GameStateManager stateManager, EventManager events,
        EnemyRuntime enemies, RoundResetCoordinator reset)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        var perf = host.AddComponent<PerfRuntime>();
        perf.Configure(stateManager, events, enemies);
        if (reset != null)
            reset.RegisterParticipant(perf);
        return perf;
    }

    public void Configure(GameStateManager stateManager, EventManager events, EnemyRuntime enemies)
    {
        if (stateManager == null) throw new ArgumentNullException(nameof(stateManager));
        if (events == null) throw new ArgumentNullException(nameof(events));
        if (enemies == null) throw new ArgumentNullException(nameof(enemies));
        _stateManager = stateManager;
        _events = events;
        _enemies = enemies;
        _frames ??= new RoundFrameRecorder();
        _text ??= new PerfTextBuffer(TextCapacity);
        _configured = true;

        string[] args = Environment.GetCommandLineArgs();
        // A/B 측정기가 씬을 다시 불러 가며 측정 판을 돌린다. 그 판은 게임 판이 아니고, 측정기가 끝나며 프로그램을 닫을 때
        // 마지막 판이 Battle에 남아 있으므로, 이 실행에서는 라운드 기록을 하지 않는다(닫을 때도 쓰지 않는다).
        bool enemyAbRun = PerfBenchmark.IsEnemyAbRun(args, Application.isEditor);
        if (enemyAbRun)
            RoundRecordingEnabled = false;
        if (Application.isEditor || !PerfBenchmark.IsRequested(args))
            return;
        if (enemyAbRun)
        {
            Debug.LogWarning("[Perf] " + PerfBenchmark.Argument + " is ignored while the enemy A/B benchmark (" +
                             EnemyAbBootstrap.RunArgument + ") runs.");
            return;
        }
        BeginBenchmark(true, PerfBenchmark.CapFromArguments(args), PerfBenchmark.SearchFromArguments(args));
    }

    // ---------------------------------------------------------------- 테스트와 키 입력이 함께 쓰는 창구

    public bool OverlayVisible => _overlay.IsVisible;
    public Canvas OverlayCanvas => _overlay.Canvas;
    // 지금 오버레이에 보이는 글(진단과 테스트용, 부를 때마다 문자열을 만든다).
    public string OverlayText => _text != null ? _text.ToString() : string.Empty;

    public EnemyCandidateMode SearchMode => Probe != null ? Probe.Mode : EnemyCandidateMode.SpatialGrid;
    // 지금 적용되는 활성 적 상한(게임 설정 또는 실험용 상한).
    public int EnemyCap => Probe != null ? Probe.MaximumEnemies(SettingsCap) : SettingsCap;
    public int EnemyCapInSettings => SettingsCap;

    // 라운드 기록을 하는가. 기본은 켜짐이고, "-rebootEnemyAb"로 A/B 측정기가 도는 플레이어에서는 꺼진다.
    // 끄면 지금 모으던 판도 버린다. 켜는 것은 다음 판부터 적용되므로 판 밖(Ready)에서 바꾼다.
    public bool RoundRecordingEnabled
    {
        get => _recordRounds;
        set
        {
            if (_recordRounds == value)
                return;
            _recordRounds = value;
            if (!value)
                DiscardRound();
            RefreshSoon();
        }
    }

    // 지금 판을 기록 중인가(Battle에서 시작해 Result나 Reset까지). Pause 중에도 참이지만 프레임은 쌓지 않는다.
    public bool IsRecording => _roundStarted && !_roundFinished;
    // 마지막으로 끝난 판의 기록 파일을 썼는가(쓰지 못했으면 오버레이가 "NOT SAVED"를 보인다). Retry 뒤에도 남는다.
    public bool LastRoundSaved { get; private set; }
    public long RecordedFrames => _frames != null ? _frames.FrameCount : 0;
    // 기록을 쓸 폴더. 비어 있으면 PerfOutput.ResolveDirectory()(기본 persistentDataPath/perf).
    public string OutputDirectory { get; set; }
    public RoundPerfRecord LastRecord { get; private set; }
    public IReadOnlyList<string> LastWrittenFiles { get; private set; } = Array.Empty<string>();

    public bool BenchmarkRunning => _benchmarkActive;
    public bool BenchmarkFinished { get; private set; }
    public bool BenchmarkSucceeded { get; private set; }

    public void ToggleOverlay() => SetOverlayVisible(!_overlay.IsVisible);

    public void SetOverlayVisible(bool visible)
    {
        if (!_configured || visible == _overlay.IsVisible)
            return;
        _overlay.SetVisible(visible);
        if (visible)
        {
            _ringHead = _ringCount = 0;
            _framesSinceSample = 0;
            _sampleElapsed = 0f;
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, DrawCallsCounter, DrawCallSamples);
            SetTiming(true);
            _lastCounters = Probe != null ? Probe.Counters : default;
            RebuildText();
        }
        else
        {
            StopDrawCalls();
            SetTiming(false);
        }
    }

    // F4. 판 도중에 바꿔도 라운드는 그대로 이어진다. 격자로 돌아올 때 EnemyPerfProbe가 지금 위치로 격자를 다시 만든다.
    public void ToggleSearchMode()
    {
        EnemyPerfProbe probe = Probe;
        if (probe == null)
            return;
        probe.SetMode(probe.Mode == EnemyCandidateMode.SpatialGrid ? EnemyCandidateMode.BruteForce : EnemyCandidateMode.SpatialGrid);
        RefreshSoon();
    }

    // F5. 실험용 상한만 바꾼다(게임 설정 에셋은 그대로). 상한을 낮춰도 이미 나와 있는 적은 그대로 두고 생성만 멈춘다.
    public void CycleEnemyCap()
    {
        EnemyPerfProbe probe = Probe;
        if (probe == null)
            return;
        int current = probe.ExperimentMaximumEnemies;
        int next = 0;
        for (int i = 0; i < ExperimentCaps.Length; i++)
        {
            if (ExperimentCaps[i] > current)
            {
                next = ExperimentCaps[i];
                break;
            }
        }
        probe.SetExperimentMaximumEnemies(next);
        RefreshSoon();
    }

    // 벤치마크를 시작한다. 플레이어에서는 "-rebootBenchmark"가 부르고, 테스트는 quitWhenDone 없이 부른다.
    public void BeginBenchmark(bool quitWhenDone, int experimentCap = 0, EnemyCandidateMode search = EnemyCandidateMode.SpatialGrid)
    {
        if (!_configured)
            return;
        _benchmarkActive = true;
        _benchmarkQuitWhenDone = quitWhenDone;
        _benchmarkRoundWritten = false;
        _benchmarkCap = Mathf.Clamp(experimentCap, 0, EnemyPerfProbe.ExperimentEnemyLimit);
        _benchmarkSearch = search;
        _benchmarkElapsed = 0f;
        _benchmarkRequestTick = -1;
        _benchmarkFramesSinceRequest = 0;
        BenchmarkFinished = BenchmarkSucceeded = false;
        // 창이 포커스를 잃어도 판이 멈추지 않게 한다(그런 판은 기록에 focus_lost로 남는다). 끝나면 되돌린다.
        if (!_runInBackgroundChanged)
        {
            _savedRunInBackground = Application.runInBackground;
            _runInBackgroundChanged = true;
        }
        Application.runInBackground = true;
        ApplyBenchmarkSettings();
        SetOverlayVisible(true);
        Debug.Log("[Perf] Benchmark started: one round plays by itself (enemy cap " + EnemyCap + ", search " + SearchMode +
                  "). The round recording goes to " + ResolveOutputDirectory());
    }

    // ---------------------------------------------------------------- 상태와 Reset

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification == null || notification.Tick < _tick)
            return;
        if (notification.Tick != _tick)
        {
            DiscardRound();
            _tick = notification.Tick;
        }
        _state = notification.Current;
        switch (_state)
        {
            case GameState.Ready:
                DiscardRound();
                break;
            case GameState.Battle:
            case GameState.Finishing:
                if (!_roundStarted && _recordRounds)
                    BeginRound();
                break;
            case GameState.Result:
                if (IsRecording)
                    FinishRound(RoundPerfRecord.EndedAtResult);
                break;
        }
        if (_overlay.IsVisible)
            RefreshSoon();
    }

    public void StopProducing() { }

    // Retry, Pause의 Retry, Quit 모두 여기를 지난다: 기록을 버리고 공간 분할과 게임 설정 상한으로 되돌린다.
    // 벤치마크 중이면 벤치마크에 준 설정을 다시 건다.
    public IEnumerator ResetRound(int tick)
    {
        DiscardRound();
        EnemyPerfProbe probe = Probe;
        if (probe != null)
        {
            probe.SetMode(EnemyCandidateMode.SpatialGrid);
            probe.SetExperimentMaximumEnemies(0);
        }
        if (_benchmarkActive)
            ApplyBenchmarkSettings();
        RefreshSoon();
        yield break;
    }

    // ---------------------------------------------------------------- 프레임

    private void Update()
    {
        if (!_configured)
            return;
        HandleKeys();
        float unscaledDelta = Time.unscaledDeltaTime;
        RecordFrame(unscaledDelta);
        if (_overlay.IsVisible)
            UpdateOverlay(unscaledDelta);
        UpdateBenchmark(unscaledDelta);
    }

    private void HandleKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;
        if (keyboard.f3Key.wasPressedThisFrame)
            ToggleOverlay();
        if (!_overlay.IsVisible)
            return;
        if (keyboard.f4Key.wasPressedThisFrame)
            ToggleSearchMode();
        if (keyboard.f5Key.wasPressedThisFrame)
            CycleEnemyCap();
    }

    private void RecordFrame(float unscaledDelta)
    {
        if (!IsRecording || (_state != GameState.Battle && _state != GameState.Finishing) || _stateManager.Tick != _tick)
            return;
        _frames.Add(unscaledDelta, _enemies.ActiveEnemyCount);
        if (!Application.isFocused)
            _focusLost = true;
        EnemyPerfProbe probe = Probe;
        if (probe == null)
            return;
        if (probe.Mode == EnemyCandidateMode.SpatialGrid) _usedGrid = true;
        else _usedBrute = true;
        int cap = probe.MaximumEnemies(SettingsCap);
        if (cap < _capMin) _capMin = cap;
        if (cap > _capMax) _capMax = cap;
    }

    private void BeginRound()
    {
        DiscardRound();
        _roundStarted = true;
        _capMin = int.MaxValue;
        _capMax = 0;
    }

    private void DiscardRound()
    {
        _frames?.Clear();
        _roundStarted = _roundFinished = false;
        _focusLost = _usedGrid = _usedBrute = false;
    }

    private void FinishRound(string endReason)
    {
        _roundFinished = true;
        LastRoundSaved = false;
        LastWrittenFiles = Array.Empty<string>();
        RoundFrameSummary summary = _frames.Summarize();
        if (!summary.HasFrames)
        {
            Debug.LogWarning("[Perf] The round ended without a frame to record; no round recording was written.");
            return;
        }
        string search = _usedGrid && _usedBrute
            ? RoundPerfRecord.MixedSearchMode
            : (_usedBrute ? EnemyCandidateMode.BruteForce : EnemyCandidateMode.SpatialGrid).ToString();
        int capMin = _capMin == int.MaxValue ? SettingsCap : _capMin;
        int capMax = _capMax == 0 ? SettingsCap : _capMax;
        RoundPerfRecord record = RoundPerfRecord.Capture(summary, endReason, _benchmarkActive, _focusLost,
            capMin, capMax, SettingsCap, search);
        LastRecord = record;
        try
        {
            LastWrittenFiles = record.Write(ResolveOutputDirectory());
            LastRoundSaved = true;
            _lastRoundLine = record.OverlayLine();
            if (_benchmarkActive)
                _benchmarkRoundWritten = true;
            Debug.Log("[Perf] " + _lastRoundLine + " Written to " + LastWrittenFiles[0]);
        }
        catch (Exception exception)
        {
            // 쓰지 못한 판도 오버레이에는 남기되 저장되지 않았다고 밝히고, 숫자는 로그에 남긴다.
            _lastRoundLine = record.OverlayLine(saved: false);
            Debug.LogWarning("[Perf] Could not write the round recording to " + ResolveOutputDirectory() + ": " +
                             exception.Message + "\n" + record.SummaryText());
        }
    }

    // 플레이어를 판 도중에 닫으면 거기까지의 기록을 남긴다. Editor에서 Play Mode를 끝내는 것은 프로그램 종료가 아니므로
    // 그 판은 버린다.
    private void OnApplicationQuit()
    {
        if (!Application.isEditor && IsRecording && _frames.FrameCount > 0)
            FinishRound(RoundPerfRecord.EndedByQuit);
    }

    private void OnDestroy()
    {
        StopDrawCalls();
        SetTiming(false);
        _overlay.Destroy();
        RestoreRunInBackground();
    }

    // ---------------------------------------------------------------- 오버레이

    private void UpdateOverlay(float unscaledDelta)
    {
        _ring[_ringHead] = unscaledDelta;
        _ringHead = (_ringHead + 1) % RingCapacity;
        if (_ringCount < RingCapacity)
            _ringCount++;
        _framesSinceSample++;
        _sampleElapsed += unscaledDelta;
        if (_sampleElapsed >= SampleSeconds)
            RebuildText();
    }

    // 다음 프레임에 글을 다시 쓴다(키 입력과 상태 변경이 바로 보이게).
    private void RefreshSoon() => _sampleElapsed = SampleSeconds;

    // 가장 최근 프레임부터 거꾸로 1초어치(또는 링에 남은 만큼)를 모은다.
    private void MeasureLastSecond(out int frames, out double seconds, out float worst)
    {
        frames = 0;
        seconds = 0d;
        worst = 0f;
        int index = _ringHead;
        while (frames < _ringCount && seconds < 1d)
        {
            index = index == 0 ? RingCapacity - 1 : index - 1;
            float delta = _ring[index];
            frames++;
            seconds += delta;
            if (delta > worst)
                worst = delta;
        }
    }

    private void RebuildText()
    {
        EnemyPerfProbe probe = Probe;
        EnemyPerfCounters counters = probe != null ? probe.Counters : default;
        int sampleFrames = _framesSinceSample;
        MeasureLastSecond(out int frames, out double seconds, out float worst);
        bool hasWindow = frames > 0 && seconds > 0d;

        // 한 줄은 50자 안쪽으로 둔다(패널 폭 510px, 글자 19px).
        PerfTextBuffer t = _text;
        t.Clear();
        t.Append("<b>REBOOT perf</b>  F3 hide | F4 search | F5 cap\n");

        t.Append("<b>FPS ");
        t.Append(hasWindow ? frames / seconds : double.NaN, 1);
        t.Append("</b>  (last 1 s)\nFrame ");
        t.Append(hasWindow ? seconds * 1000d / frames : double.NaN, 2);
        t.Append(" ms avg, ");
        t.Append(hasWindow ? worst * 1000d : double.NaN, 2);
        t.Append(" ms worst\n");

        t.Append("Enemies ");
        t.Append(_enemies.ActiveEnemyCount);
        t.Append(" / ");
        t.Append(EnemyCap);
        t.Append(EnemyCap > SettingsCap ? " cap  <color=#FFB45A>experiment</color>\n" : " cap\n");

        t.Append("Neighbour search: ");
        if (probe == null) t.Append("n/a");
        else if (probe.Mode == EnemyCandidateMode.SpatialGrid) t.Append("<color=#7CF0A0>spatial grid</color>");
        else t.Append("<color=#FFB45A>brute force</color>");
        // 다른 도구(A/B 측정기)가 누적 수치를 0으로 되돌렸으면 이번 표본은 비운다.
        bool counted = probe != null && sampleFrames > 0 && counters.AiQueries >= _lastCounters.AiQueries &&
                       counters.AiCandidates >= _lastCounters.AiCandidates && counters.AiTicks >= _lastCounters.AiTicks;
        long queries = counters.AiQueries - _lastCounters.AiQueries;
        long candidates = counters.AiCandidates - _lastCounters.AiCandidates;
        t.Append("\nCandidates ");
        t.Append(counted ? (double)candidates / sampleFrames : double.NaN, 0);
        t.Append(" per frame (");
        t.Append(counted && queries > 0 ? (double)candidates / queries : double.NaN, 1);
        t.Append(" per enemy)\nAI update ");
        long aiTicks = counters.AiTicks - _lastCounters.AiTicks;
        bool timed = counted && probe.IsTiming;
        t.Append(timed ? EnemyPerfProbe.TicksToMicroseconds(aiTicks) / 1000d / sampleFrames : double.NaN, 3);
        t.Append(" ms per frame\n");

        t.Append("Draw calls ");
        if (_drawCalls.Valid) t.Append(AverageDrawCalls(), 0);
        else t.Append("n/a");
        t.Append('\n');

        t.Append("State ");
        t.Append(StateName(_stateManager.CurrentState));
        if (_benchmarkActive)
            t.Append("  <color=#FFB45A>BENCHMARK</color>");
        t.Append("\nRound recording ");
        if (!_recordRounds) t.Append("disabled");
        else if (!_roundStarted) t.Append("off");
        else if (_roundFinished) t.Append(LastRoundSaved ? "written" : "<color=#FFB45A>NOT SAVED</color>");
        else if (_state == GameState.Battle || _state == GameState.Finishing) t.Append("on");
        else t.Append("held");
        if (_roundStarted)
        {
            t.Append(", ");
            t.Append(_frames.FrameCount);
            t.Append(" frames");
        }
        t.Append('\n');

        t.Append(_lastRoundLine ?? "Last round: none yet");
        t.Append("\n<size=80%><color=#9AA4B2>Mac laptop keyboard: hold fn with F3 / F4 / F5</color></size>");

        _overlay.SetText(t.Chars, t.Length);
        _lastCounters = counters;
        _framesSinceSample = 0;
        _sampleElapsed = 0f;
    }

    // 렌더 카운터는 한 프레임이 0, 다음 프레임이 두 프레임치로 나올 때가 있어 최근 몇 프레임을 평균한다.
    private double AverageDrawCalls()
    {
        int count = _drawCalls.Count;
        if (count <= 0)
            return double.NaN;
        long sum = 0;
        for (int i = 0; i < count; i++)
            sum += _drawCalls.GetSample(i).Value;
        return (double)sum / count;
    }

    private void StopDrawCalls()
    {
        if (_drawCalls.Valid)
            _drawCalls.Dispose();
        _drawCalls = default;
    }

    // 오버레이가 보이는 동안 적 AI 갱신 시간을 잰다(프레임당 시각 읽기 두 번). 오버레이 전용 스위치(LiveTiming)만 쓴다.
    // A/B 측정기의 MeasureTime은 "측정 판" 표시로도 읽히므로(적 쪽 코드가 그 값으로 생성 방식을 바꿀 수 있다) 켜지 않는다.
    private void SetTiming(bool on)
    {
        EnemyPerfProbe probe = Probe;
        if (probe != null)
            probe.LiveTiming = on;
    }

    // ---------------------------------------------------------------- 벤치마크

    // 지금 상태에 맞는 요청을 한 번 보낸다(같은 상태에 오래 머물면 다시 보낸다). Battle과 Finishing은 게임이 스스로 진행한다.
    private void UpdateBenchmark(float unscaledDelta)
    {
        if (!_benchmarkActive)
            return;
        _benchmarkElapsed += unscaledDelta;
        if (_benchmarkElapsed > BenchmarkTimeoutSeconds)
        {
            EndBenchmark(false, "the round did not reach Result within " + BenchmarkTimeoutSeconds + " s");
            return;
        }
        GameState current = _stateManager.CurrentState;
        if (current == GameState.Result)
        {
            EndBenchmark(_benchmarkRoundWritten, "the round recording could not be written");
            return;
        }
        if (_stateManager.IsResetting)
            return;
        int tick = _stateManager.Tick;
        if (current == _benchmarkRequestState && tick == _benchmarkRequestTick && ++_benchmarkFramesSinceRequest < BenchmarkResendFrames)
            return;
        IGameRequest request;
        switch (current)
        {
            case GameState.Ready:
                request = new StartRequest(tick);
                break;
            case GameState.IntroDialogue:
            case GameState.OutroDialogue:
                request = new DialogueCompleteRequest(tick);
                break;
            case GameState.Countdown:
                request = new CountdownCompleteRequest(tick);
                break;
            default:
                return;
        }
        _events.EnqueueRequest(request);
        _benchmarkRequestState = current;
        _benchmarkRequestTick = tick;
        _benchmarkFramesSinceRequest = 0;
    }

    private void EndBenchmark(bool succeeded, string problem)
    {
        _benchmarkActive = false;
        BenchmarkFinished = true;
        BenchmarkSucceeded = succeeded;
        RestoreRunInBackground();
        if (succeeded)
            Debug.Log("[Perf] Benchmark finished. " + _lastRoundLine + " Results: " + ResolveOutputDirectory());
        else
            Debug.LogWarning("[Perf] Benchmark failed: " + problem + ".");
        RefreshSoon();
        if (_benchmarkQuitWhenDone)
            Application.Quit(succeeded ? 0 : 1);
    }

    private void ApplyBenchmarkSettings()
    {
        EnemyPerfProbe probe = Probe;
        if (probe == null)
            return;
        probe.SetExperimentMaximumEnemies(_benchmarkCap);
        probe.SetMode(_benchmarkSearch);
    }

    private void RestoreRunInBackground()
    {
        if (!_runInBackgroundChanged)
            return;
        _runInBackgroundChanged = false;
        Application.runInBackground = _savedRunInBackground;
    }

    // ---------------------------------------------------------------- 도우미

    private EnemyPerfProbe Probe => _enemies != null ? _enemies.Perf : null;

    private int SettingsCap => _enemies != null && _enemies.Settings != null ? _enemies.Settings.MaximumEnemyCount : 0;

    private string ResolveOutputDirectory() =>
        string.IsNullOrEmpty(OutputDirectory) ? PerfOutput.ResolveDirectory() : OutputDirectory;

    private static string StateName(GameState state)
    {
        int index = (int)state;
        return index >= 0 && index < StateNames.Length ? StateNames[index] : "?";
    }
}

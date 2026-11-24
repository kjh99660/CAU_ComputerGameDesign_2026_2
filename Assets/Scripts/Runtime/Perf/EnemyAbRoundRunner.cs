using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

// "실제 한 판" A/B 측정기. 게임 씬을 다시 불러 같은 조건의 판을 방식별로 돌리고 프레임마다 수치를 남긴다.
// - 판마다 씬을 새로 불러 같은 tick(= 같은 적 생성 시드)에서 시작한다. 게임 시간은 프레임당 1/60초로 고정한다.
// - 플레이어는 움직이지 않고, 정해진 프레임에 플레이어 위치로 특수 공격 범위 판정(EnemyRuntime.ApplySpecial)을 한다.
//   입력 장치와 키 설정은 건드리지 않는다. 상태 전환은 게임과 같은 요청(Request)으로만 한다.
// - 검증 판: 공간 분할로 돌리면서 질의마다 전수 검사와 비교한다(같은 위치에서의 판정 일치와 두 방식의 후보 수).
// - 시간 판: 방식 하나만 켜고 AI 갱신 시간을 잰다. 두 방식의 판은 따로 진행되므로 궤적이 같다는 보장은 없다
//   (이웃을 더하는 순서가 달라 부동소수점 끝자리가 달라질 수 있다). 같았는지는 비교 결과로 남긴다.
[DisallowMultipleComponent]
public sealed class EnemyAbRoundRunner : MonoBehaviour
{
    // Unity Profiler의 Render 범주 카운터 이름. 이 Unity 버전에 있는 이름인지는 EditMode 테스트가 확인한다.
    public const string DrawCallsCounter = "Draw Calls Count";
    public const string BatchesCounter = "Batches Count";
    public const string SetPassCallsCounter = "SetPass Calls Count";

    private const int StateWaitFrames = 6000;
    private const ulong HashSeed = 14695981039346656037UL;
    private const ulong HashPrime = 1099511628211UL;

    private EnemyAbRoundConfig _config;
    private Action<EnemyAbRoundReport> _completed;
    private bool _settingsSaved, _savedRunInBackground;
    private int _savedCaptureFramerate, _savedVSync, _savedTargetFrameRate;
    private ProfilerRecorder _drawCalls, _batches, _setPass;
    private EnemyPerfProbe _activeProbe;

    public bool IsDone { get; private set; }
    public EnemyAbRoundReport Report { get; private set; }

    public static EnemyAbRoundRunner Run(EnemyAbRoundConfig config, Action<EnemyAbRoundReport> completed = null)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        var go = new GameObject("EnemyAbRoundRunner");
        DontDestroyOnLoad(go);
        var runner = go.AddComponent<EnemyAbRoundRunner>();
        runner._config = config;
        runner._completed = completed;
        runner.Report = new EnemyAbRoundReport { Config = config, Environment = PerfEnvironment.Capture(config.Label) };
        runner.StartCoroutine(runner.RunAll());
        return runner;
    }

    private readonly struct PassSpec
    {
        public readonly string Name;
        public readonly EnemyCandidateMode Mode;
        public readonly bool Verify;
        public PassSpec(string name, EnemyCandidateMode mode, bool verify) { Name = name; Mode = mode; Verify = verify; }
    }

    private List<PassSpec> BuildPasses()
    {
        var passes = new List<PassSpec>();
        if (_config.RunVerifyPass)
            passes.Add(new PassSpec("verify-grid", EnemyCandidateMode.SpatialGrid, true));
        for (int repeat = 0; repeat < Mathf.Max(0, _config.TimingRepeats); repeat++)
        {
            // 격자, 전수, 전수, 격자 ... 순서로 돌려 앞뒤 순서의 영향을 줄인다.
            bool gridFirst = repeat % 2 == 0;
            string suffix = "-" + (repeat + 1);
            passes.Add(gridFirst ? new PassSpec("grid" + suffix, EnemyCandidateMode.SpatialGrid, false)
                                 : new PassSpec("brute" + suffix, EnemyCandidateMode.BruteForce, false));
            passes.Add(gridFirst ? new PassSpec("brute" + suffix, EnemyCandidateMode.BruteForce, false)
                                 : new PassSpec("grid" + suffix, EnemyCandidateMode.SpatialGrid, false));
        }
        return passes;
    }

    private IEnumerator RunAll()
    {
        SaveAndApplySettings();
        foreach (PassSpec spec in BuildPasses())
        {
            var result = new EnemyAbPassResult { Name = spec.Name, Mode = spec.Mode, Verify = spec.Verify };
            Report.Passes.Add(result);
            yield return RunPass(spec, result);
            if (!result.Completed)
            {
                Report.Error = $"Pass {spec.Name} failed: {result.Error}";
                break;
            }
        }
        RestoreSettings();

        if (!string.IsNullOrEmpty(_config.OutputDirectory))
        {
            try { Report.Write(_config.OutputDirectory); }
            catch (Exception exception) { Report.Error = "Could not write results: " + exception.Message; }
        }
        IsDone = true;
        _completed?.Invoke(Report);
    }

    private IEnumerator RunPass(PassSpec spec, EnemyAbPassResult result)
    {
        yield return RunPassBody(spec, result);
        // 판이 어느 경로로 끝났든 렌더 카운터를 닫고, 후보 수집 방식과 실험용 상한을 평소 상태로 되돌린다.
        EndPassMetrics();
        ReleaseProbe();
    }

    private void ReleaseProbe()
    {
        if (_activeProbe == null) return;
        _activeProbe.MeasureTime = false;
        _activeProbe.ShadowVerify = false;
        _activeProbe.SetMode(EnemyCandidateMode.SpatialGrid);
        _activeProbe.SetExperimentMaximumEnemies(0);
        _activeProbe = null;
    }

    // The player build holds only the Neon City stage (since 2026-11-25); the flat arena stays in the project. In the
    // Editor any scene loads by path. In a player, a scene outside the build falls back to the first build scene, and
    // the report names the scene that was actually measured.
    private AsyncOperation LoadRoundScene()
    {
#if UNITY_EDITOR
        return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(_config.ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
#else
        if (SceneUtility.GetBuildIndexByScenePath(_config.ScenePath) < 0 && SceneManager.sceneCountInBuildSettings > 0)
            _config.ScenePath = SceneUtility.GetScenePathByBuildIndex(0);
        return SceneManager.LoadSceneAsync(_config.ScenePath, LoadSceneMode.Single);
#endif
    }

    private IEnumerator RunPassBody(PassSpec spec, EnemyAbPassResult result)
    {
        AsyncOperation load = LoadRoundScene();
        if (load == null)
        {
            result.Error = "Scene is not in the build settings: " + _config.ScenePath;
            yield break;
        }
        while (!load.isDone) yield return null;
        yield return null;

        var state = FindFirstObjectByType<GameStateManager>();
        var events = FindFirstObjectByType<EventManager>();
        var enemies = FindFirstObjectByType<EnemyRuntime>();
        var combat = FindFirstObjectByType<CombatRoundRuntime>();
        var player = FindFirstObjectByType<PlayerRuntime>();
        if (state == null || events == null || enemies == null || combat == null || player == null)
        {
            result.Error = "The scene is missing GameStateManager, EventManager, EnemyRuntime, CombatRoundRuntime or PlayerRuntime.";
            yield break;
        }
        // 히트스톱은 시간 배율을 바꿔 두 방식의 프레임 시간 비교를 흐트러뜨리므로, 측정하는 판(매번 새로 불러온 씬)에서는 끈다.
        combat.HitStopsEnabled = false;

        yield return WaitFor(state, GameState.Ready, result);
        if (result.Error != null) yield break;

        EnemyPerfProbe probe = enemies.Perf;
        if (probe == null)
        {
            result.Error = "EnemyRuntime is not configured.";
            yield break;
        }
        _activeProbe = probe;

        // 일반 플레이의 상한(설정 에셋)을 넘는 적 수는 성능 실험용 상한으로만 채운다. 설정 에셋은 바꾸지 않는다.
        EnemySettings enemySettings = enemies.Settings;
        Report.EnemyCapInSettings = enemySettings.MaximumEnemyCount;
        if (_config.TargetEnemies > enemySettings.MaximumEnemyCount)
        {
            probe.SetExperimentMaximumEnemies(_config.TargetEnemies);
            Report.ExperimentEnemyCap = probe.ExperimentMaximumEnemies;
        }
        int target = Mathf.Min(_config.TargetEnemies, probe.MaximumEnemies(enemySettings.MaximumEnemyCount));
        int captureFramerate = Mathf.Max(1, _config.CaptureFramerate);
        int estimatedFill = EnemyAbRoundConfig.EstimateFillFrames(target, enemySettings.InitialEnemyCount,
            enemySettings.SpawnCountPerWave, enemySettings.SpawnInterval, captureFramerate);
        // 적이 다 차기를 기다리는 시간과 측정 구간이 한 라운드 안에 들어가야 한다(라운드 길이는 바꾸지 않는다).
        float neededSeconds = (estimatedFill + _config.WindowFrames) / (float)captureFramerate;
        if (neededSeconds >= combat.Settings.roundDuration)
        {
            result.Error = $"{target} enemies need about {estimatedFill / (float)captureFramerate:0.#} s to spawn plus a " +
                           $"{_config.WindowFrames / (float)captureFramerate:0.#} s window but the round lasts {combat.Settings.roundDuration:0.#} s.";
            yield break;
        }
        int fillLimit = Mathf.Max(_config.MaxFillFrames, estimatedFill + estimatedFill / 4 + 120);

        UnityEngine.Random.InitState(_config.Seed);
        // 프레임(1/60초)과 물리 고정 스텝(0.02초)은 6프레임마다 같은 어긋남으로 돌아온다. 판마다 그 주기의 같은 지점에서
        // 시작해, 어느 프레임에 물리 스텝이 도는지가 판 사이에 같아지게 한다(결과의 physics_phase_ms로 확인한다).
        // 이 기다림은 반드시 이 자리에서 직접 한다. 따로 뗀 코루틴이 한 번도 쉬지 않고 끝나면 Unity가 한 프레임을
        // 더 넘긴 뒤 돌아오므로, 이미 맞아 있던 판만 한 프레임 어긋난다.
        for (int i = 0; i < 12 && !IsAtPhysicsCycleStart(); i++)
            yield return null;
        probe.SetMode(spec.Mode);
        probe.ShadowVerify = spec.Verify;
        probe.MeasureTime = !spec.Verify;
        BeginPassMetrics(spec, result);

        // 게임과 같은 요청 경로로 Battle까지 간다(대화와 카운트다운은 완료 요청으로 넘긴다).
        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue, result);
        if (result.Error != null) yield break;
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown, result);
        if (result.Error != null) yield break;
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle, result);
        if (result.Error != null) yield break;
        result.Tick = state.Tick;

        // 적이 목표 수만큼 찰 때까지 기다린다(게임의 생성 규칙 그대로).
        int fill = 0;
        while (enemies.ActiveEnemyCount < target && fill < fillLimit && state.CurrentState == GameState.Battle)
        {
            yield return null;
            fill++;
        }
        result.FillFrames = fill;
        result.EnemiesAtWindowStart = enemies.ActiveEnemyCount;
        if (enemies.ActiveEnemyCount < target)
        {
            result.Error = $"Only {enemies.ActiveEnemyCount} of {target} enemies were active after {fill} frames.";
            yield break;
        }

        yield return Measure(spec, result, state, enemies, combat, player, probe);
        result.Completed = result.Error == null;
    }

    // 이 판에서 어떤 지표가 유효한지 정하고, 유효하면 렌더 카운터를 켠다.
    // 카운터는 켠 프레임의 값을 내지 못하므로(첫 표본이 0), 측정 구간보다 한참 앞서 전투가 시작되기 전에 켜 둔다.
    private void BeginPassMetrics(PassSpec spec, EnemyAbPassResult result)
    {
        PerfEnvironment environment = Report.Environment;
        result.CpuValid = !spec.Verify;
        result.CpuNote = spec.Verify
            ? "verification pass: the shadow comparison runs inside the timed code; CPU time is not recorded"
            : "Stopwatch time of EnemyRuntime.UpdateAI per frame on this machine";
        result.FrameMetricsValid = environment.FrameMetricsSupported && !spec.Verify;
        result.UnsupportedNote = environment.FrameMetricsSupported
            ? (spec.Verify ? "verification pass: frame time is distorted by the shadow comparison" : string.Empty)
            : environment.FrameMetricsNote;
        if (!result.FrameMetricsValid)
            return;

        _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, DrawCallsCounter);
        _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, BatchesCounter);
        _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, SetPassCallsCounter);
        result.RenderCountersValid = _drawCalls.Valid && _batches.Valid && _setPass.Valid;
        if (!result.RenderCountersValid)
            result.UnsupportedNote = "render counters are not available in this build";
    }

    private void EndPassMetrics()
    {
        if (_drawCalls.Valid) _drawCalls.Dispose();
        if (_batches.Valid) _batches.Dispose();
        if (_setPass.Valid) _setPass.Dispose();
        _drawCalls = _batches = _setPass = default;
    }

    private IEnumerator Measure(PassSpec spec, EnemyAbPassResult result, GameStateManager state, EnemyRuntime enemies,
        CombatRoundRuntime combat, PlayerRuntime player, EnemyPerfProbe probe)
    {
        Transform playerTransform = player.transform;
        Vector3 playerStart = playerTransform.position;
        Camera camera = Camera.main;
        Quaternion cameraStart = camera != null ? camera.transform.rotation : Quaternion.identity;
        float specialRadius = combat.Settings.specialRadius;
        float specialForce = combat.Settings.specialKnockbackMultiplier;
        bool specialRestoresGauge = combat.Settings.specialKillsRestoreGauge;
        var specialFrames = new HashSet<int>(_config.SpecialFrames ?? Array.Empty<int>());

        // 궤적은 첫 공간 분할 판에서만, 적 구성이 바뀌지 않는 동안(공격 전)만 저장한다.
        int traceFrames = Report.Trace == null && spec.Mode == EnemyCandidateMode.SpatialGrid
            ? Mathf.Min(_config.TraceFrames, _config.WindowFrames) : 0;
        foreach (int frame in specialFrames)
            if (frame < traceFrames) traceFrames = Mathf.Max(0, frame);
        EnemyAbTrace trace = null;
        EnemyAgent[] traceEnemies = null;
        if (traceFrames > 0)
        {
            IReadOnlyList<EnemyAgent> initial = probe.ActiveEnemies;
            traceEnemies = new EnemyAgent[initial.Count];
            for (int i = 0; i < traceEnemies.Length; i++) traceEnemies[i] = initial[i];
            trace = new EnemyAbTrace(traceEnemies.Length, traceFrames, 1f / Mathf.Max(1, _config.CaptureFramerate),
                $"recorded round: scene={_config.ScenePath} tick={result.Tick} spawnSeed={result.SpawnSeed} pass={spec.Name} enemies={traceEnemies.Length}");
        }

        result.Frames.Capacity = Mathf.Max(result.Frames.Capacity, _config.WindowFrames);
        result.KillsAtWindowStart = combat.Stats.KillCount;
        result.PlayerHitsAtWindowStart = combat.Stats.HitCount;
        probe.ResetCounters();
        EnemyPerfCounters previous = probe.Counters;
        long last = Stopwatch.GetTimestamp();
        bool focusLost = false;
        result.PhysicsPhaseMs = PhysicsPhaseSeconds() * 1000d;

        for (int frame = 0; frame < _config.WindowFrames; frame++)
        {
            yield return null;
            if (state.CurrentState != GameState.Battle)
            {
                result.Error = $"The round left Battle at window frame {frame} ({state.CurrentState}).";
                break;
            }
            // 창이 포커스를 잃으면 운영체제가 화면 표시를 늦출 수 있어, 그 판의 프레임 시간은 믿을 수 없다.
            if (result.FrameMetricsValid && !Application.isFocused)
                focusLost = true;
            long now = Stopwatch.GetTimestamp();
            double frameMs = (now - last) * 1000d / Stopwatch.Frequency;
            last = now;

            int specialHits = -1;
            if (specialFrames.Contains(frame))
                specialHits = enemies.ApplySpecial(playerTransform.position, specialRadius, specialForce, state.Tick, specialRestoresGauge);

            EnemyPerfCounters current = probe.Counters;
            IReadOnlyList<EnemyAgent> active = probe.ActiveEnemies;
            bool tracing = trace != null && frame < traceFrames;
            // 궤적은 구간을 시작할 때 있던 적들만 담는다. 그 뒤에 생성된 적은 목록 끝에 붙으므로 앞쪽 순서는 그대로다.
            // 그중 하나라도 사라지거나 순서가 바뀌면 고정 궤적으로 쓸 수 없다.
            if (tracing && active.Count < traceEnemies.Length)
            {
                trace = null;
                tracing = false;
            }
            ulong hash = HashSeed;
            double distanceSum = 0d;
            Vector3 playerPosition = playerTransform.position;
            unchecked
            {
                for (int i = 0; i < active.Count; i++)
                {
                    Vector3 position = active[i].transform.position;
                    double dx = position.x - playerPosition.x, dz = position.z - playerPosition.z;
                    distanceSum += Math.Sqrt(dx * dx + dz * dz);
                    hash = (hash ^ (uint)EnemyAbTrace.ToMillimetres(position.x)) * HashPrime;
                    hash = (hash ^ (uint)EnemyAbTrace.ToMillimetres(position.z)) * HashPrime;
                    if (!tracing || i >= traceEnemies.Length) continue;
                    if (!ReferenceEquals(active[i], traceEnemies[i]))
                    {
                        trace = null;
                        tracing = false;
                        continue;
                    }
                    trace.SetEnemy(frame, i, position.x, position.z);
                }
            }
            if (tracing)
                trace.SetPlayer(frame, playerTransform.position.x, playerTransform.position.z);

            result.Frames.Add(new EnemyAbFrameSample
            {
                Frame = frame,
                ActiveEnemies = active.Count,
                AiQueries = current.AiQueries - previous.AiQueries,
                AiCandidates = current.AiCandidates - previous.AiCandidates,
                SpecialQueries = current.SpecialQueries - previous.SpecialQueries,
                SpecialCandidates = current.SpecialCandidates - previous.SpecialCandidates,
                SpecialHits = specialHits,
                KillsTotal = combat.Stats.KillCount,
                PlayerHitsTotal = combat.Stats.HitCount,
                Score = combat.Stats.Score,
                AiCpuMicros = EnemyPerfProbe.TicksToMicroseconds(current.AiTicks - previous.AiTicks),
                SpecialCpuMicros = EnemyPerfProbe.TicksToMicroseconds(current.SpecialTicks - previous.SpecialTicks),
                FrameMs = frameMs,
                // 직전 프레임의 값이다. 렌더 스레드가 프레임을 끝낸 시점에 집계되므로 한 프레임은 0, 다음 프레임은
                // 두 프레임치로 나올 수 있다. 합은 보존되므로 구간 평균으로 본다.
                DrawCalls = result.RenderCountersValid ? _drawCalls.LastValue : 0,
                Batches = result.RenderCountersValid ? _batches.LastValue : 0,
                SetPassCalls = result.RenderCountersValid ? _setPass.LastValue : 0,
                PositionHash = hash,
                MeanDistanceToPlayer = active.Count > 0 ? distanceSum / active.Count : 0d,
                ShadowInRadius = current.ShadowInRadius - previous.ShadowInRadius,
                ShadowMissedAi = current.ShadowMissedAi - previous.ShadowMissedAi,
                ShadowMissedSpecial = current.ShadowMissedSpecial - previous.ShadowMissedSpecial,
            });
            previous = current;
        }

        result.ShadowQueries = probe.Counters.ShadowQueries;
        result.ShadowMissedKnockback = probe.Counters.ShadowMissedKnockback;
        result.ShadowMissedMaxDrift = probe.Counters.ShadowMissedMaxDrift;
        result.PlayerMoved = (playerTransform.position - playerStart).sqrMagnitude > 1e-6f;
        result.CameraMoved = camera != null && Quaternion.Angle(camera.transform.rotation, cameraStart) > 0.01f;
        if (focusLost)
            result.InvalidateFrameMetrics(EnemyAbPassResult.FocusLostNote);
        if (trace != null && result.Error == null)
        {
            Report.Trace = trace;
            result.TraceRecorded = true;
        }
    }

    // 이번 프레임의 시각이 마지막 물리 스텝보다 얼마나 앞서 있는가(초). 0 이상 고정 스텝 길이 미만이다.
    private static double PhysicsPhaseSeconds() => Time.timeAsDouble - Time.fixedTimeAsDouble;

    // 6프레임 주기 가운데 어긋남이 가장 작은 프레임인가. 스텝 길이가 정확히 0.02초가 아니라 어긋남이 아주 조금씩
    // 밀리므로(한 판에 0.01ms쯤) 판 사이가 완전히 같아지지는 않는다.
    private static bool IsAtPhysicsCycleStart()
    {
        double phase = PhysicsPhaseSeconds();
        return phase >= 0d && phase < 1d / 300d;
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected, EnemyAbPassResult result)
    {
        int frames = 0;
        while (state.CurrentState != expected && frames < StateWaitFrames)
        {
            yield return null;
            frames++;
        }
        if (state.CurrentState != expected)
            result.Error = $"Timed out waiting for {expected} (still {state.CurrentState}).";
    }

    // 게임 시간을 고정하고, 프레임 시간이 수직 동기나 프레임 제한에 묶이지 않게 한다.
    // 창이 포커스를 잃어도 측정이 멈춘 채 기다리지 않도록 백그라운드 실행을 켠다(그 판의 프레임 지표는 무효로 처리한다).
    // 실행 중의 값만 바꾸고 끝나면 되돌린다.
    private void SaveAndApplySettings()
    {
        _savedCaptureFramerate = Time.captureFramerate;
        _savedVSync = QualitySettings.vSyncCount;
        _savedTargetFrameRate = Application.targetFrameRate;
        _savedRunInBackground = Application.runInBackground;
        _settingsSaved = true;
        Time.captureFramerate = Mathf.Max(1, _config.CaptureFramerate);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;
    }

    private void RestoreSettings()
    {
        if (!_settingsSaved) return;
        _settingsSaved = false;
        Time.captureFramerate = _savedCaptureFramerate;
        QualitySettings.vSyncCount = _savedVSync;
        Application.targetFrameRate = _savedTargetFrameRate;
        Application.runInBackground = _savedRunInBackground;
    }

    private void OnDestroy()
    {
        EndPassMetrics();
        ReleaseProbe();
        RestoreSettings();
    }
}

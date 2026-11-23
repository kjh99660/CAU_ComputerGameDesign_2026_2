using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 적 공간 분할 A/B 측정의 Editor 진입점.
// - 실제 한 판: Main 씬을 Play Mode로 돌려 EnemyAbRoundRunner를 실행한다.
//     메뉴  Tools/REBOOT Perf/Enemy A-B: Run Round In Play Mode  (화면에 그리므로 프레임 시간·Draw Call까지 기록)
//     배치  -batchmode -executeMethod RebootEnemyAbBenchmark.RunRoundBatch -rebootOut <dir> -rebootLabel <name>
//           (화면에 내보내지 않으므로 프레임 시간·FPS·Draw Call은 기록하지 않는다)
//     적 수를 늘린 실험: 메뉴 Enemy A-B: Run 100-200-300 Sweep In Play Mode, 또는 배치에 -rebootEnemies 100,200,300
// - 마이크로벤치마크: 저장된 궤적 위에서 후보 수집과 거리 판정만 되풀이한다(게임 루프와 렌더링 없음).
//     메뉴  Tools/REBOOT Perf/Enemy A-B: Run Microbenchmark
//     배치  -batchmode -executeMethod RebootEnemyAbBenchmark.RunMicroBatch -rebootOut <dir> -rebootLabel <name> [-rebootTrace <file>] [-rebootRepeats <n>]
public static class RebootEnemyAbBenchmark
{
    public const string DefaultOutput = "Docs/Perf/EnemyAb/results/local";
    public const string DefaultTrace = "Docs/Perf/EnemyAb/trace/round100.trace.txt";
    private const string EnemySettingsPath = "Assets/Prefabs/Enemy/EnemySettings.asset";
    private const string RequestKey = "Reboot.EnemyAb.Request";
    private const string CountsKey = "Reboot.EnemyAb.Counts";
    private const string BatchKey = "Reboot.EnemyAb.Batch";
    private const string ExitCodeKey = "Reboot.EnemyAb.ExitCode";
    private const string DeadlineKey = "Reboot.EnemyAb.Deadline";
    private const double BatchTimeoutSeconds = 1500d;

    [MenuItem("Tools/REBOOT Perf/Enemy A-B: Run Round In Play Mode")]
    public static void RunRoundFromMenu() => RunFromMenu(new[] { 100 }, DefaultOutput);

    // 적 100·200·300명을 차례로 돌린다. 100명을 넘는 수는 성능 실험용 상한으로 채우며 게임 설정은 바꾸지 않는다.
    [MenuItem("Tools/REBOOT Perf/Enemy A-B: Run 100-200-300 Sweep In Play Mode")]
    public static void RunSweepFromMenu() => RunFromMenu(new[] { 100, 200, 300 }, DefaultOutput + "-sweep");

    private static void RunFromMenu(int[] counts, string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[EnemyAb] Stop Play Mode before starting the benchmark.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var config = new EnemyAbRoundConfig { OutputDirectory = output, Label = "editor-playmode", TargetEnemies = counts[0] };
        RequestRound(config, counts, false);
    }

    public static void RunRoundBatch()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            EnemyAbRoundConfig config = EnemyAbBootstrap.ConfigFromArguments(args, DefaultOutput);
            RequestRound(config, EnemyAbBootstrap.CountsFromArguments(args, config.TargetEnemies), true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/REBOOT Perf/Enemy A-B: Run Microbenchmark")]
    public static void RunMicroFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[EnemyAb] Stop Play Mode before starting the microbenchmark.");
            return;
        }
        EnemyAbMicroBenchmark.Result result = RunMicro(DefaultTrace, DefaultOutput, "editor", 21);
        Debug.Log(Describe(result));
    }

    public static void RunMicroBatch()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string trace = EnemyAbBootstrap.Argument(args, "-rebootTrace") ?? DefaultTrace;
            string output = EnemyAbBootstrap.Argument(args, "-rebootOut") ?? DefaultOutput;
            string label = EnemyAbBootstrap.Argument(args, "-rebootLabel") ?? "unlabeled";
            int repeats = int.TryParse(EnemyAbBootstrap.Argument(args, "-rebootRepeats"), out int parsed) ? parsed : 21;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnemyAbMicroBenchmark.Result result = RunMicro(trace, output, label, repeats);
            Debug.Log(Describe(result));
            EditorApplication.Exit(result.Equal ? 0 : 3);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    // 궤적 파일을 읽어 마이크로벤치마크를 돌리고 micro_summary.csv와 micro_environment.csv를 쓴다.
    // 거리와 칸 크기는 게임의 설정 에셋에서 읽는다.
    public static EnemyAbMicroBenchmark.Result RunMicro(string tracePath, string outputDirectory, string label, int repeats)
    {
        EnemyAbTrace trace = EnemyAbTrace.Parse(File.ReadAllText(tracePath));
        var options = new EnemyAbMicroBenchmark.Options
        {
            Repeats = Mathf.Max(1, repeats),
            SpecialFrames = new[] { trace.FrameCount / 4, trace.FrameCount / 2, trace.FrameCount * 3 / 4 },
        };
        var enemySettings = AssetDatabase.LoadAssetAtPath<EnemySettings>(EnemySettingsPath);
        if (enemySettings != null)
        {
            options.CellSize = enemySettings.SpatialCellSize;
            options.SeparationDistance = enemySettings.SeparationDistance;
        }
        var combatSettings = Resources.Load<CombatSettings>("CombatSettings");
        if (combatSettings != null)
            options.SpecialRadius = combatSettings.specialRadius;

        EnemyAbMicroBenchmark.Result result = EnemyAbMicroBenchmark.Run(trace, options);
        PerfEnvironment environment = PerfEnvironment.Capture(label);
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        EnemyAbMicroBenchmark.WriteCsv(result, environment, Path.Combine(directory, "micro_summary.csv"));
        environment.WriteCsv(Path.Combine(directory, "micro_environment.csv"), new[]
        {
            Pair("measurement_kind", "micro (isolated candidate search on a fixed trace; no game loop or rendering)"),
            Pair("trace_file", tracePath),
            Pair("trace_source", trace.Source),
            Pair("cell_size", PerfCsv.Number(options.CellSize)),
            Pair("separation_distance", PerfCsv.Number(options.SeparationDistance)),
            Pair("special_radius", PerfCsv.Number(options.SpecialRadius)),
            Pair("special_frames", string.Join(" ", options.SpecialFrames)),
            Pair("repeats", PerfCsv.Number(options.Repeats)),
            Pair("results_equal_between_modes", result.Equal ? "1" : "0"),
        });
        return result;
    }

    private static System.Collections.Generic.KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static string Describe(EnemyAbMicroBenchmark.Result result) =>
        $"[EnemyAb] micro: enemies={result.EnemyCount} frames={result.FrameCount} equal={result.Equal} " +
        $"grid candidates/query={(double)result.Grid.Candidates / Math.Max(1, result.Grid.Queries):0.##} " +
        $"({result.Grid.MedianMicrosPerFrame:0.#} us/frame), " +
        $"brute candidates/query={(double)result.BruteForce.Candidates / Math.Max(1, result.BruteForce.Queries):0.##} " +
        $"({result.BruteForce.MedianMicrosPerFrame:0.#} us/frame)";

    // ---------------------------------------------------------------- play mode round

    private static void RequestRound(EnemyAbRoundConfig config, int[] counts, bool batch)
    {
        config.OutputDirectory = Path.GetFullPath(config.OutputDirectory);
        SessionState.SetString(RequestKey, JsonUtility.ToJson(config));
        SessionState.SetString(CountsKey, string.Join(",", counts));
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetInt(ExitCodeKey, 1);
        // 적 수를 여럿 돌리면 그만큼 오래 걸린다.
        SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + BatchTimeoutSeconds * Math.Max(1, counts.Length)));
        EditorSceneManager.OpenScene(config.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update -= Watchdog;
        EditorApplication.update += Watchdog;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            string json = SessionState.GetString(RequestKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;
            SessionState.EraseString(RequestKey);
            var config = JsonUtility.FromJson<EnemyAbRoundConfig>(json);
            int[] counts = EnemyAbSweep.ParseCounts(SessionState.GetString(CountsKey, string.Empty), config.TargetEnemies);
            SessionState.EraseString(CountsKey);
            Debug.Log($"[EnemyAb] Round benchmark started (enemies: {string.Join(", ", counts)}). Results: {config.OutputDirectory}");
            EnemyAbSweep.Run(config, counts, (reports, succeeded) =>
            {
                Debug.Log(succeeded
                    ? "[EnemyAb] Round benchmark finished. Results: " + config.OutputDirectory
                    : "[EnemyAb] Round benchmark failed: " + (reports.Count > 0 ? reports[reports.Count - 1].Error : "no result"));
                SessionState.SetInt(ExitCodeKey, succeeded ? 0 : 1);
                EditorApplication.ExitPlaymode();
            });
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(BatchKey, false))
        {
            SessionState.EraseBool(BatchKey);
            EditorApplication.Exit(SessionState.GetInt(ExitCodeKey, 1));
        }
    }

    // 배치 실행이 Play Mode에 들어가지 못하거나 끝나지 않으면 Editor가 계속 떠 있지 않도록 종료한다.
    private static void Watchdog()
    {
        if (!SessionState.GetBool(BatchKey, false))
            return;
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(DeadlineKey, float.MaxValue))
            return;
        SessionState.EraseBool(BatchKey);
        Debug.LogError("[EnemyAb] Round benchmark timed out.");
        EditorApplication.Exit(2);
    }
}

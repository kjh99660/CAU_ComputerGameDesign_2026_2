using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// 라운드 성능 기록을 쓰는 폴더. 기본은 Application.persistentDataPath/perf이다.
// PlayMode 테스트는 임시 폴더로 돌려, 사용자의 persistentDataPath에 테스트 기록이 남지 않게 한다.
public static class PerfOutput
{
    public const string FolderName = "perf";

    // 비어 있으면 기본 폴더를 쓴다. Play Mode를 시작할 때마다 비운다(Domain Reload를 끈 Editor에서도 남지 않게).
    public static string DirectoryOverride { get; set; }

    public static string ResolveDirectory() => string.IsNullOrEmpty(DirectoryOverride)
        ? Path.Combine(Application.persistentDataPath, FolderName)
        : DirectoryOverride;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => DirectoryOverride = null;
}

// 한 판의 성능 기록: 프레임 요약과 측정 환경을 CSV(머리줄 + 한 줄)와 짧은 글로 남긴다.
// 서식은 순수 C#이라 EditMode에서 검증하고, Unity에서 값을 읽는 일은 Capture 하나에 모았다.
// 컴퓨터 이름, 사용자 이름, 파일 경로는 기록하지 않는다.
public sealed class RoundPerfRecord
{
    public const int FormatVersion = 1;
    public const string EndedAtResult = "result";
    public const string EndedByQuit = "quit";
    public const string MixedSearchMode = "mixed";

    public const string CsvHeader =
        "format_version,ended_at_local,ended_at_utc,end_reason,benchmark,frames,recorded_seconds," +
        "avg_fps,one_percent_low_fps,min_fps,avg_frame_ms,worst_frame_ms,slowest_one_percent_frames," +
        "enemies_avg,enemies_max,enemy_cap_min,enemy_cap_max,enemy_cap_in_settings,search_mode," +
        "focus_lost,ignored_frames,overflow_frames," +
        "device_model,processor,processor_count,system_memory_mb,graphics_device,graphics_api,graphics_memory_mb," +
        "operating_system,screen,fullscreen_mode,refresh_hz,quality_level,vsync_count,target_frame_rate," +
        "app_version,unity_version,environment_class,is_editor,is_batchmode,is_development_build,scripting_backend";

    public RoundFrameSummary Summary;
    public PerfEnvironment Environment;
    public DateTime EndedLocal, EndedUtc;
    public string EndReason = EndedAtResult;
    public bool Benchmark, FocusLost;
    public int EnemyCapMin, EnemyCapMax, EnemyCapInSettings;
    // EnemyCandidateMode 이름(SpatialGrid, BruteForce) 또는 판 도중에 바꿨으면 mixed.
    public string SearchMode = string.Empty;
    public string FullScreenMode = string.Empty;
    public double RefreshHz = double.NaN;
    public int VSyncCount, TargetFrameRate;
    public string AppVersion = string.Empty;

    // 판이 끝난 지금의 기기와 화면 설정을 읽어 기록을 만든다.
    public static RoundPerfRecord Capture(RoundFrameSummary summary, string endReason, bool benchmark, bool focusLost,
        int enemyCapMin, int enemyCapMax, int enemyCapInSettings, string searchMode)
    {
        return new RoundPerfRecord
        {
            Summary = summary,
            Environment = PerfEnvironment.Capture(benchmark ? "benchmark" : "round"),
            EndedLocal = DateTime.Now,
            EndedUtc = DateTime.UtcNow,
            EndReason = endReason,
            Benchmark = benchmark,
            FocusLost = focusLost,
            EnemyCapMin = enemyCapMin,
            EnemyCapMax = enemyCapMax,
            EnemyCapInSettings = enemyCapInSettings,
            SearchMode = searchMode ?? string.Empty,
            FullScreenMode = Screen.fullScreenMode.ToString(),
            RefreshHz = Screen.currentResolution.refreshRateRatio.value,
            VSyncCount = QualitySettings.vSyncCount,
            TargetFrameRate = Application.targetFrameRate,
            AppVersion = Application.version,
        };
    }

    public string CsvRow()
    {
        RoundFrameSummary s = Summary;
        PerfEnvironment e = Environment ?? new PerfEnvironment();
        var cells = new[]
        {
            PerfCsv.Number(FormatVersion),
            EndedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            EndedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            PerfCsv.Escape(EndReason),
            Benchmark ? "1" : "0",
            PerfCsv.Number(s.Frames),
            PerfCsv.Number(s.TotalSeconds, "0.###"),
            PerfCsv.Number(s.AverageFps, "0.##"),
            PerfCsv.Number(s.OnePercentLowFps, "0.##"),
            PerfCsv.Number(s.MinimumFps, "0.##"),
            PerfCsv.Number(s.AverageFrameMs, "0.###"),
            PerfCsv.Number(s.WorstFrameMs, "0.###"),
            PerfCsv.Number(s.SlowestFrames),
            PerfCsv.Number(s.EnemiesAverage, "0.#"),
            PerfCsv.Number(s.EnemiesMax),
            PerfCsv.Number(EnemyCapMin),
            PerfCsv.Number(EnemyCapMax),
            PerfCsv.Number(EnemyCapInSettings),
            PerfCsv.Escape(SearchMode),
            FocusLost ? "1" : "0",
            PerfCsv.Number(s.IgnoredFrames),
            PerfCsv.Number(s.OverflowFrames),
            PerfCsv.Escape(e.DeviceModel),
            PerfCsv.Escape(e.ProcessorType),
            PerfCsv.Number(e.ProcessorCount),
            PerfCsv.Number(e.SystemMemoryMb),
            PerfCsv.Escape(e.GraphicsDeviceName),
            PerfCsv.Escape(e.GraphicsDeviceType),
            PerfCsv.Number(e.GraphicsMemoryMb),
            PerfCsv.Escape(e.OperatingSystem),
            PerfCsv.Number(e.ScreenWidth) + "x" + PerfCsv.Number(e.ScreenHeight),
            PerfCsv.Escape(FullScreenMode),
            PerfCsv.Number(RefreshHz, "0.##"),
            PerfCsv.Escape(e.QualityLevel),
            PerfCsv.Number(VSyncCount),
            PerfCsv.Number(TargetFrameRate),
            PerfCsv.Escape(AppVersion),
            PerfCsv.Escape(e.UnityVersion),
            PerfCsv.Escape(e.EnvironmentClass),
            e.IsEditor ? "1" : "0",
            e.IsBatchMode ? "1" : "0",
            e.IsDevelopmentBuild ? "1" : "0",
            PerfCsv.Escape(e.ScriptingBackend),
        };
        return string.Join(",", cells);
    }

    // 사람이 읽는 짧은 요약(시연 PC에서 그대로 옮겨 적는 용도).
    public string SummaryText()
    {
        RoundFrameSummary s = Summary;
        PerfEnvironment e = Environment ?? new PerfEnvironment();
        string nl = System.Environment.NewLine;
        var text = new StringBuilder(1024);
        text.Append("REBOOT round performance").Append(nl);
        text.Append("Ended: ").Append(EndedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(EndReason == EndedByQuit ? " (the game was closed in the middle of the round)" : " (the round reached Result)")
            .Append(nl);
        text.Append("Average FPS: ").Append(Fixed(s.AverageFps, 1)).Append(nl);
        text.Append("1% low FPS: ").Append(Fixed(s.OnePercentLowFps, 1))
            .Append(" (average of the slowest 1% of frames: ").Append(s.SlowestFrames.ToString(CultureInfo.InvariantCulture))
            .Append(" frames)").Append(nl);
        text.Append("Minimum FPS: ").Append(Fixed(s.MinimumFps, 1))
            .Append(" (slowest frame ").Append(Fixed(s.WorstFrameMs, 2)).Append(" ms)").Append(nl);
        text.Append("Frames: ").Append(s.Frames.ToString(CultureInfo.InvariantCulture)).Append(" in ")
            .Append(Fixed(s.TotalSeconds, 2)).Append(" s of Battle and Finishing (Pause is not counted)").Append(nl);
        text.Append("Enemies: average ").Append(Fixed(s.EnemiesAverage, 1)).Append(", max ")
            .Append(s.EnemiesMax.ToString(CultureInfo.InvariantCulture)).Append(" | enemy cap ").Append(CapText())
            .Append(" (game setting ").Append(EnemyCapInSettings.ToString(CultureInfo.InvariantCulture)).Append(')')
            .Append(" | neighbour search ").Append(SearchMode).Append(nl);
        text.Append("Device: ").Append(e.DeviceModel).Append(" | CPU: ").Append(e.ProcessorType)
            .Append(" (").Append(e.ProcessorCount.ToString(CultureInfo.InvariantCulture)).Append(" threads)")
            .Append(" | RAM ").Append(e.SystemMemoryMb.ToString(CultureInfo.InvariantCulture)).Append(" MB").Append(nl);
        text.Append("GPU: ").Append(e.GraphicsDeviceName).Append(" (").Append(e.GraphicsDeviceType).Append(", ")
            .Append(e.GraphicsMemoryMb.ToString(CultureInfo.InvariantCulture)).Append(" MB)").Append(nl);
        text.Append("OS: ").Append(e.OperatingSystem).Append(nl);
        text.Append("Screen: ").Append(e.ScreenWidth.ToString(CultureInfo.InvariantCulture)).Append('x')
            .Append(e.ScreenHeight.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(FullScreenMode)
            .Append(", ").Append(Fixed(RefreshHz, 0)).Append(" Hz | quality ").Append(e.QualityLevel)
            .Append(" | vSync ").Append(VSyncCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | target frame rate ").Append(TargetFrameRate.ToString(CultureInfo.InvariantCulture)).Append(nl);
        text.Append("Build: ").Append(e.EnvironmentClass).Append(" | app ").Append(AppVersion)
            .Append(" | Unity ").Append(e.UnityVersion).Append(" | ").Append(e.ScriptingBackend).Append(nl);
        if (Benchmark)
            text.Append("Benchmark mode: the round was started automatically and nobody played.").Append(nl);
        if (e.IsEditor)
            text.Append("Note: measured in the Unity Editor, which adds its own cost. Use a player build for demo PC numbers.").Append(nl);
        if (e.IsBatchMode)
            text.Append("Note: batch mode does not show frames on a display, so these frame times are not real FPS.").Append(nl);
        if (FocusLost)
            text.Append("Note: the game window lost focus during the round; frame times may include stalls.").Append(nl);
        if (EnemyCapMax > EnemyCapInSettings)
            text.Append("Note: an experiment enemy cap was used (performance test only; normal play stays at ")
                .Append(EnemyCapInSettings.ToString(CultureInfo.InvariantCulture)).Append(").").Append(nl);
        if (SearchMode != EnemyCandidateMode.SpatialGrid.ToString())
            text.Append("Note: the neighbour search was not the spatial grid for the whole round (comparison setting).").Append(nl);
        if (s.OverflowFrames > 0)
            text.Append("Note: ").Append(s.OverflowFrames.ToString(CultureInfo.InvariantCulture))
                .Append(" frames did not fit the buffer; the 1% low uses the first frames only.").Append(nl);
        return text.ToString();
    }

    // 성능 오버레이의 "지난 판" 두 줄. 오버레이 글꼴에 맞춰 ASCII만 쓰고, 패널 폭에 맞춰 한 줄을 50자 안쪽으로 둔다
    // (FPS는 100 이상이면 소수점을 뺀다). 파일을 쓰지 못한 판(saved: false)은 둘째 줄에 적 수 대신 그 사실을 적는다.
    public string OverlayLine(bool saved = true)
    {
        RoundFrameSummary s = Summary;
        string first = "Last round: avg " + Compact(s.AverageFps) + ", 1% low " + Compact(s.OnePercentLowFps) +
                       ", min " + Compact(s.MinimumFps) + " FPS\n  " + s.Frames.ToString(CultureInfo.InvariantCulture) + " frames, ";
        if (!saved)
            return first + "NOT SAVED (see the log)";
        return first + "enemies avg " + Fixed(s.EnemiesAverage, 0) + ", max " + s.EnemiesMax.ToString(CultureInfo.InvariantCulture) +
               (EndReason == EndedByQuit ? " (quit)" : string.Empty);
    }

    // round_<날짜>_<시각>.csv와 같은 이름의 .txt를 쓰고 두 경로를 돌려준다. 같은 초에 끝난 판은 _2, _3을 붙인다.
    public string[] Write(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("An output directory is required.", nameof(directory));
        Directory.CreateDirectory(directory);
        string stem = "round_" + EndedLocal.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string csv = Path.Combine(directory, stem + ".csv");
        for (int suffix = 2; File.Exists(csv) || File.Exists(Path.ChangeExtension(csv, ".txt")); suffix++)
            csv = Path.Combine(directory, stem + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".csv");
        string txt = Path.ChangeExtension(csv, ".txt");
        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(csv, CsvHeader + "\n" + CsvRow() + "\n", utf8);
        File.WriteAllText(txt, SummaryText(), utf8);
        return new[] { csv, txt };
    }

    private string CapText()
    {
        string min = EnemyCapMin.ToString(CultureInfo.InvariantCulture);
        return EnemyCapMin == EnemyCapMax ? min : min + "-" + EnemyCapMax.ToString(CultureInfo.InvariantCulture);
    }

    private static string Fixed(double value, int decimals) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "n/a" : value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static string Compact(double value) => Fixed(value, Math.Abs(value) >= 100d ? 0 : 1);
}

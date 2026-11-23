using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine.Rendering;

// A/B 측정 도구의 순수 부분: 궤적, 마이크로벤치마크의 결과 일치, 유효성 규칙, CSV 모양을 검증한다.
public sealed class EnemyAbBenchmarkTests
{
    [Test]
    public void SyntheticTraceIsDeterministicAndSurvivesTextRoundTrip()
    {
        EnemyAbTrace a = EnemyAbTrace.Synthetic(7, 12, 5);
        EnemyAbTrace b = EnemyAbTrace.Synthetic(7, 12, 5);
        string text = a.Serialize();
        Assert.AreEqual(text, b.Serialize(), "The same seed must give the same trace.");
        Assert.AreNotEqual(text, EnemyAbTrace.Synthetic(8, 12, 5).Serialize());

        EnemyAbTrace parsed = EnemyAbTrace.Parse(text);
        Assert.AreEqual(12, parsed.EnemyCount);
        Assert.AreEqual(5, parsed.FrameCount);
        Assert.AreEqual(1f / 60f, parsed.DeltaTime, 1e-6f);
        StringAssert.Contains("synthetic seed=7", parsed.Source);
        for (int frame = 0; frame < 5; frame++)
        for (int enemy = 0; enemy < 12; enemy++)
        {
            Assert.AreEqual(a.EnemyX(frame, enemy), parsed.EnemyX(frame, enemy));
            Assert.AreEqual(a.EnemyZ(frame, enemy), parsed.EnemyZ(frame, enemy));
        }
        Assert.AreEqual(text, parsed.Serialize());
    }

    [Test]
    public void TraceStoresMillimetresAndRejectsMalformedText()
    {
        var trace = new EnemyAbTrace(1, 1, 0.02f, "unit");
        trace.SetEnemy(0, 0, 1.23449f, -0.0006f);
        trace.SetPlayer(0, -24f, 42f);
        Assert.AreEqual(1.234f, trace.EnemyX(0, 0), 1e-6f);
        Assert.AreEqual(-0.001f, trace.EnemyZ(0, 0), 1e-6f);
        Assert.AreEqual(-24f, trace.PlayerX(0));

        Assert.Throws<FormatException>(() => EnemyAbTrace.Parse("not a trace"));
        string truncated = trace.Serialize().Replace("-24000 42000 | 1234 -1", "-24000 42000 | 1234");
        Assert.Throws<FormatException>(() => EnemyAbTrace.Parse(truncated));
    }

    [Test]
    public void BothModesGiveTheSameJudgmentsWhileBruteForceLooksAtEveryEnemy()
    {
        const int enemies = 60, frames = 40;
        EnemyAbTrace trace = EnemyAbTrace.Synthetic(20261123, enemies, frames, 14f);
        var options = new EnemyAbMicroBenchmark.Options { Repeats = 2, SpecialFrames = new[] { 10, 30 } };
        EnemyAbMicroBenchmark.Result result = EnemyAbMicroBenchmark.Run(trace, options);

        Assert.IsTrue(result.SameSeparationResults, "Separation neighbours must match between the modes.");
        Assert.IsTrue(result.SameSpecialResults, "Special-attack hits must match between the modes.");
        Assert.IsTrue(result.Grid.StableAcrossRepeats && result.BruteForce.StableAcrossRepeats);

        long queries = (long)enemies * frames;
        Assert.AreEqual(queries, result.Grid.Queries);
        Assert.AreEqual(queries, result.BruteForce.Queries);
        Assert.AreEqual(queries * enemies, result.BruteForce.Candidates, "Brute force examines every enemy for every query.");
        Assert.Less(result.Grid.Candidates, result.BruteForce.Candidates, "The grid must narrow the candidates.");
        Assert.GreaterOrEqual(result.Grid.Candidates, result.Grid.Accepted, "Accepted neighbours come from the candidates.");
        Assert.Greater(result.Grid.Accepted, 0, "The crowd must be dense enough to have neighbours.");
        Assert.AreEqual(result.Grid.Accepted, result.BruteForce.Accepted);

        Assert.AreEqual(2, result.Grid.SpecialQueries);
        Assert.AreEqual(2L * enemies, result.BruteForce.SpecialCandidates);
        Assert.Greater(result.Grid.SpecialHits, 0);
        Assert.AreEqual(result.Grid.SpecialHits, result.BruteForce.SpecialHits);

        Assert.AreEqual(2, result.Grid.MicrosPerFrame.Count, "The warm-up round is not recorded.");
        Assert.Greater(result.Grid.MedianMicrosPerFrame, 0d);
        Assert.Greater(result.BruteForce.MedianMicrosPerFrame, 0d);
    }

    [Test]
    public void MicroCsvHasOneRowPerModeAndLeavesFrameMetricsBlank()
    {
        EnemyAbTrace trace = EnemyAbTrace.Synthetic(3, 10, 4, 6f);
        EnemyAbMicroBenchmark.Result result = EnemyAbMicroBenchmark.Run(trace, new EnemyAbMicroBenchmark.Options { Repeats = 1, SpecialFrames = new[] { 2 } });
        string[] lines = EnemyAbMicroBenchmark.ToCsv(result, PerfEnvironment.Capture("unit,test")).TrimEnd('\n').Split('\n');
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual(EnemyAbMicroBenchmark.CsvHeader, lines[0]);
        StringAssert.StartsWith("micro,\"unit,test\",", lines[1]);
        StringAssert.Contains(",SpatialGrid,", lines[1]);
        StringAssert.Contains(",BruteForce,", lines[2]);
        // 프레임 시간, FPS, Draw Call 칸은 비어 있고 이유가 적혀 있다.
        StringAssert.Contains(",,,,microbenchmark: no game loop or rendering", lines[1].Replace("\"", string.Empty));
    }

    [Test]
    public void FrameMetricsAreSupportedOnlyWhenFramesAreActuallyPresented()
    {
        Assert.IsTrue(PerfEnvironment.SupportsFrameMetrics(false, GraphicsDeviceType.Direct3D11, out string shown));
        Assert.IsEmpty(shown);
        Assert.IsFalse(PerfEnvironment.SupportsFrameMetrics(true, GraphicsDeviceType.Metal, out string batch));
        Assert.AreEqual(PerfEnvironment.BatchModeNote, batch);
        Assert.IsFalse(PerfEnvironment.SupportsFrameMetrics(false, GraphicsDeviceType.Null, out string none));
        Assert.AreEqual(PerfEnvironment.NoGraphicsNote, none);

        Assert.AreEqual("editor-batchmode", PerfEnvironment.Classify(true, true, true));
        Assert.AreEqual("editor-playmode", PerfEnvironment.Classify(true, false, true));
        Assert.AreEqual("player-development", PerfEnvironment.Classify(false, false, true));
        Assert.AreEqual("player-release", PerfEnvironment.Classify(false, false, false));
        Assert.AreEqual("player-release-batchmode", PerfEnvironment.Classify(false, true, false));

        PerfEnvironment environment = PerfEnvironment.Capture(null);
        Assert.AreEqual("unlabeled", environment.Label);
        Assert.IsNotEmpty(environment.UnityVersion);
        bool hasValidity = false;
        foreach (var row in environment.ToRows())
            if (row.Key == "frame_metrics_valid") hasValidity = true;
        Assert.IsTrue(hasValidity);
    }

    [Test]
    public void CsvHelpersUseInvariantNumbersAndBlankForInvalidValues()
    {
        Assert.AreEqual("1.5", PerfCsv.Number(1.5d));
        Assert.AreEqual(string.Empty, PerfCsv.Number(double.NaN));
        Assert.AreEqual("12", PerfCsv.Optional(12, true));
        Assert.AreEqual(string.Empty, PerfCsv.Optional(12, false));
        Assert.AreEqual("\"a,\"\"b\"\"\"", PerfCsv.Escape("a,\"b\""));
        Assert.AreEqual("plain", PerfCsv.Escape("plain"));
        var values = new[] { 5d, 1d, 3d, 2d, 4d };
        Assert.AreEqual(3d, PerfCsv.Percentile(values, 0.5));
        Assert.AreEqual(1d, PerfCsv.Percentile(values, 0d));
        Assert.AreEqual(5d, PerfCsv.Percentile(values, 1d));
        Assert.AreEqual(3d, PerfCsv.Mean(values));
        Assert.IsTrue(double.IsNaN(PerfCsv.Mean(Array.Empty<double>())));
    }

    [Test]
    public void CommandLineArgumentsFillTheRoundConfig()
    {
        string[] args = { "game.exe", "-rebootEnemyAb", "-rebootOut", "C:/results", "-rebootLabel", "demo-pc-player",
            "-rebootEnemies", "100", "-rebootRepeats", "3", "-rebootWindowFrames", "450" };
        EnemyAbRoundConfig config = EnemyAbBootstrap.ConfigFromArguments(args, "fallback");
        Assert.AreEqual("C:/results", config.OutputDirectory);
        Assert.AreEqual("demo-pc-player", config.Label);
        Assert.AreEqual(100, config.TargetEnemies);
        Assert.AreEqual(3, config.TimingRepeats);
        Assert.AreEqual(450, config.WindowFrames);
        Assert.AreEqual(150, config.TraceFrames);
        CollectionAssert.AreEqual(new[] { 150, 250, 350 }, config.SpecialFrames);

        EnemyAbRoundConfig defaults = EnemyAbBootstrap.ConfigFromArguments(new[] { "game.exe" }, "fallback");
        Assert.AreEqual("fallback", defaults.OutputDirectory);
        Assert.AreEqual(100, defaults.TargetEnemies);
        Assert.AreEqual(900, defaults.WindowFrames);
        CollectionAssert.AreEqual(new[] { 300, 500, 700 }, defaults.SpecialFrames);
        CollectionAssert.AreEqual(new[] { 100 }, EnemyAbBootstrap.CountsFromArguments(new[] { "game.exe" }, 100));

        // 적 수를 여럿 적으면 차례로 돌릴 목록이 되고, 설정에는 첫 번째가 들어간다.
        string[] sweep = { "game.exe", "-rebootEnemyAb", "-rebootEnemies", "100,200,300" };
        Assert.AreEqual(100, EnemyAbBootstrap.ConfigFromArguments(sweep, "fallback").TargetEnemies);
        CollectionAssert.AreEqual(new[] { 100, 200, 300 }, EnemyAbBootstrap.CountsFromArguments(sweep, 100));
    }

    [Test]
    public void ExperimentEnemyCapOnlyRaisesTheLimitAndIsClamped()
    {
        var probe = new EnemyPerfProbe(new EnemyRegistry(), new EnemySpatialGrid(4f));
        Assert.AreEqual(0, probe.ExperimentMaximumEnemies);
        Assert.AreEqual(100, probe.MaximumEnemies(100), "Normal play keeps the configured cap.");
        probe.SetExperimentMaximumEnemies(200);
        Assert.AreEqual(200, probe.MaximumEnemies(100));
        probe.SetExperimentMaximumEnemies(50);
        Assert.AreEqual(100, probe.MaximumEnemies(100), "The experiment value never lowers the game's cap.");
        probe.SetExperimentMaximumEnemies(999);
        Assert.AreEqual(EnemyPerfProbe.ExperimentEnemyLimit, probe.ExperimentMaximumEnemies);
        Assert.AreEqual(300, probe.MaximumEnemies(100));
        probe.SetExperimentMaximumEnemies(-5);
        Assert.AreEqual(0, probe.ExperimentMaximumEnemies);
        Assert.AreEqual(100, probe.MaximumEnemies(100));
    }

    [Test]
    public void FillEstimateFollowsTheSpawnRules()
    {
        // 처음 5명, 1초마다 5명씩(게임의 현재 설정)이고 프레임당 1/60초일 때.
        Assert.AreEqual(1140, EnemyAbRoundConfig.EstimateFillFrames(100, 5, 5, 1f, 60));
        Assert.AreEqual(2340, EnemyAbRoundConfig.EstimateFillFrames(200, 5, 5, 1f, 60));
        Assert.AreEqual(3540, EnemyAbRoundConfig.EstimateFillFrames(300, 5, 5, 1f, 60));
        Assert.AreEqual(0, EnemyAbRoundConfig.EstimateFillFrames(5, 5, 5, 1f, 60));
        Assert.AreEqual(60, EnemyAbRoundConfig.EstimateFillFrames(6, 5, 5, 1f, 60));
        Assert.AreEqual(90, EnemyAbRoundConfig.EstimateFillFrames(20, 0, 8, 0.5f, 60), "Three waves of eight, half a second apart.");
    }

    [Test]
    public void SweepParsesEnemyCountsAndCombinesSummaries()
    {
        CollectionAssert.AreEqual(new[] { 100, 200, 300 }, EnemyAbSweep.ParseCounts("100,200,300"));
        CollectionAssert.AreEqual(new[] { 200, 300 }, EnemyAbSweep.ParseCounts(" 200 , 200,abc,0,-4,500"));
        CollectionAssert.AreEqual(new[] { 100 }, EnemyAbSweep.ParseCounts(null));
        CollectionAssert.AreEqual(new[] { 70 }, EnemyAbSweep.ParseCounts("", 70));
        Assert.AreEqual("enemies-200", EnemyAbSweep.FolderFor(200));

        var template = new EnemyAbRoundConfig { OutputDirectory = "out", Label = "unit", WindowFrames = 450, SpecialFrames = new[] { 150 } };
        Assert.AreEqual("out", EnemyAbSweep.ConfigFor(template, 100, false).OutputDirectory);
        EnemyAbRoundConfig second = EnemyAbSweep.ConfigFor(template, 200, true);
        Assert.AreEqual(200, second.TargetEnemies);
        StringAssert.EndsWith("enemies-200", second.OutputDirectory);
        Assert.AreEqual(450, second.WindowFrames);
        CollectionAssert.AreEqual(new[] { 150 }, second.SpecialFrames);
        Assert.AreEqual(100, template.TargetEnemies, "The template is not modified.");

        var reports = new List<EnemyAbRoundReport>();
        foreach (int count in new[] { 100, 200 })
        {
            var report = new EnemyAbRoundReport
            {
                Config = EnemyAbSweep.ConfigFor(template, count, true), Environment = PerfEnvironment.Capture("unit"),
                EnemyCapInSettings = 100, ExperimentEnemyCap = count > 100 ? count : 0,
            };
            EnemyAbPassResult pass = Pass("grid-1", EnemyCandidateMode.SpatialGrid, cpuValid: true);
            pass.Frames.Add(Sample(0, 600, 1UL, 12.5));
            report.Passes.Add(pass);
            reports.Add(report);
        }
        string[] lines = EnemyAbSweep.ScalingCsv(reports).TrimEnd('\n').Split('\n');
        Assert.AreEqual(3, lines.Length, "One summary row for each of the two counts.");
        Assert.AreEqual(EnemyAbSweep.ScalingHeader, lines[0]);
        Assert.AreEqual(lines[0].Split(',').Length, lines[1].Split(',').Length);
        StringAssert.StartsWith("100,100,0,round,unit,", lines[1]);
        StringAssert.StartsWith("200,100,200,round,unit,", lines[2]);
    }

    [Test]
    public void RoundReportBlanksInvalidMetricsAndFindsTheFirstDifference()
    {
        var report = new EnemyAbRoundReport { Config = new EnemyAbRoundConfig(), Environment = PerfEnvironment.Capture("unit") };
        EnemyAbPassResult grid = Pass("grid-1", EnemyCandidateMode.SpatialGrid, cpuValid: true);
        EnemyAbPassResult brute = Pass("brute-1", EnemyCandidateMode.BruteForce, cpuValid: true);
        for (int frame = 0; frame < 3; frame++)
        {
            grid.Frames.Add(Sample(frame, 600, 0xAAUL + (ulong)frame, 12.5));
            // 두 번째 프레임부터 위치가 달라진다.
            brute.Frames.Add(Sample(frame, 10000, frame == 0 ? 0xAAUL : 0xFFUL, 80));
        }
        report.Passes.Add(grid);
        report.Passes.Add(brute);
        Assert.IsTrue(report.Succeeded);

        string[] summary = report.SummaryCsv().TrimEnd('\n').Split('\n');
        Assert.AreEqual(3, summary.Length);
        Assert.AreEqual(EnemyAbRoundReport.SummaryHeader.Split(',').Length, summary[1].Split(',').Length);
        string[] header = EnemyAbRoundReport.SummaryHeader.Split(',');
        string[] gridRow = summary[1].Split(',');
        Assert.AreEqual("round", gridRow[0]);
        Assert.AreEqual("600", gridRow[Array.IndexOf(header, "ai_candidates_per_frame")]);
        Assert.AreEqual("12.5", gridRow[Array.IndexOf(header, "ai_cpu_us_median")]);
        Assert.AreEqual("1", gridRow[Array.IndexOf(header, "cpu_valid")]);
        // 프레임 시간과 렌더 카운터는 이 판에서 유효하지 않으므로 빈칸이다.
        Assert.AreEqual(string.Empty, gridRow[Array.IndexOf(header, "frame_ms_avg")]);
        Assert.AreEqual(string.Empty, gridRow[Array.IndexOf(header, "fps_from_avg_frame")]);
        Assert.AreEqual("0", gridRow[Array.IndexOf(header, "frame_metrics_valid")]);
        Assert.AreEqual(string.Empty, gridRow[Array.IndexOf(header, "draw_calls_avg")]);

        string[] frames = report.FramesCsv(grid).TrimEnd('\n').Split('\n');
        Assert.AreEqual(4, frames.Length);
        Assert.AreEqual(EnemyAbRoundReport.FramesHeader.Split(',').Length, frames[1].Split(',').Length);

        string[] comparison = report.ComparisonCsv().TrimEnd('\n').Split('\n');
        Assert.AreEqual(2, comparison.Length);
        string[] compared = comparison[1].Split(',');
        string[] comparisonHeader = EnemyAbRoundReport.ComparisonHeader.Split(',');
        Assert.AreEqual("all", compared[Array.IndexOf(comparisonHeader, "segment")]);
        Assert.AreEqual("1", compared[Array.IndexOf(comparisonHeader, "first_position_hash_difference_frame")]);
        Assert.AreEqual("0", compared[Array.IndexOf(comparisonHeader, "position_hashes_identical")]);
        Assert.AreEqual("0.5", compared[Array.IndexOf(comparisonHeader, "max_mean_distance_difference_m")]);
        Assert.AreEqual("1", compared[Array.IndexOf(comparisonHeader, "active_enemies_identical")]);
    }

    [Test]
    public void RoundReportSplitsTheWindowAtTheFirstAttack()
    {
        var config = new EnemyAbRoundConfig { SpecialFrames = new[] { 2, 3 } };
        var report = new EnemyAbRoundReport { Config = config, Environment = PerfEnvironment.Capture("unit") };
        EnemyAbPassResult pass = Pass("verify-grid", EnemyCandidateMode.SpatialGrid, cpuValid: false);
        pass.Verify = true;
        for (int frame = 0; frame < 4; frame++)
        {
            EnemyAbFrameSample sample = Sample(frame, 500, 1UL, 10);
            // 공격은 2번 프레임에 7명, 3번 프레임에 2명을 맞힌다.
            sample.SpecialHits = frame == 2 ? 7 : frame == 3 ? 2 : -1;
            sample.KillsTotal = frame < 2 ? 0 : frame == 2 ? 7 : 9;
            sample.ShadowInRadius = 300;
            pass.Frames.Add(sample);
        }
        report.Passes.Add(pass);

        string[] header = EnemyAbRoundReport.SummaryHeader.Split(',');
        string[] rows = report.SummaryCsv().TrimEnd('\n').Split('\n');
        Assert.AreEqual(4, rows.Length, "One row each for all, steady and attacks.");
        string[] all = rows[1].Split(','), steady = rows[2].Split(','), attacks = rows[3].Split(',');
        int segment = Array.IndexOf(header, "segment"), frames = Array.IndexOf(header, "segment_frames");
        int kills = Array.IndexOf(header, "kills"), hits = Array.IndexOf(header, "special_hits");
        Assert.AreEqual("all", all[segment]);
        Assert.AreEqual("4", all[frames]);
        Assert.AreEqual("9", all[kills]);
        Assert.AreEqual("steady", steady[segment]);
        Assert.AreEqual("2", steady[frames]);
        Assert.AreEqual("0", steady[kills]);
        Assert.AreEqual("0", steady[hits]);
        Assert.AreEqual("attacks", attacks[segment]);
        Assert.AreEqual("2", attacks[frames]);
        Assert.AreEqual("9", attacks[kills]);
        Assert.AreEqual("9", attacks[hits]);
        // 검증 판은 CPU 시간을 남기지 않고, 전수 비교 수치를 남긴다.
        Assert.AreEqual(string.Empty, all[Array.IndexOf(header, "ai_cpu_us_median")]);
        Assert.AreEqual("1200", all[Array.IndexOf(header, "shadow_in_radius")]);
        Assert.AreEqual("0", all[Array.IndexOf(header, "shadow_missed_ai")]);
    }

    [Test]
    public void RoundReportWritesFrameMetricsWhenValidAndBlanksThemAfterFocusLoss()
    {
        var report = new EnemyAbRoundReport { Config = new EnemyAbRoundConfig(), Environment = PerfEnvironment.Capture("unit") };
        EnemyAbPassResult pass = Pass("grid-1", EnemyCandidateMode.SpatialGrid, cpuValid: true);
        pass.FrameMetricsValid = true;
        pass.RenderCountersValid = true;
        pass.UnsupportedNote = string.Empty;
        pass.PhysicsPhaseMs = 10.0189;
        for (int frame = 0; frame < 4; frame++)
        {
            EnemyAbFrameSample sample = Sample(frame, 600, 1UL, 12.5);
            sample.FrameMs = 4d;
            sample.DrawCalls = 120 + frame;
            sample.Batches = 80;
            sample.SetPassCalls = 30;
            pass.Frames.Add(sample);
        }
        report.Passes.Add(pass);

        string[] header = EnemyAbRoundReport.SummaryHeader.Split(',');
        string[] frameHeader = EnemyAbRoundReport.FramesHeader.Split(',');
        string[] row = report.SummaryCsv().TrimEnd('\n').Split('\n')[1].Split(',');
        Assert.AreEqual("10.019", row[Array.IndexOf(header, "physics_phase_ms")]);
        Assert.AreEqual("4", row[Array.IndexOf(header, "frame_ms_avg")]);
        Assert.AreEqual("4", row[Array.IndexOf(header, "frame_ms_median")]);
        Assert.AreEqual("250", row[Array.IndexOf(header, "fps_from_avg_frame")]);
        Assert.AreEqual("1", row[Array.IndexOf(header, "frame_metrics_valid")]);
        Assert.AreEqual("121.5", row[Array.IndexOf(header, "draw_calls_avg")]);
        Assert.AreEqual("80", row[Array.IndexOf(header, "batches_avg")]);
        Assert.AreEqual("30", row[Array.IndexOf(header, "setpass_calls_avg")]);
        Assert.AreEqual("1", row[Array.IndexOf(header, "render_counters_valid")]);
        Assert.AreEqual(string.Empty, row[Array.IndexOf(header, "unsupported_note")]);
        string[] frameRow = report.FramesCsv(pass).TrimEnd('\n').Split('\n')[1].Split(',');
        Assert.AreEqual("4", frameRow[Array.IndexOf(frameHeader, "frame_ms")]);
        Assert.AreEqual("120", frameRow[Array.IndexOf(frameHeader, "draw_calls")]);

        // 창이 포커스를 잃은 판은 프레임 지표만 비운다. 후보 수와 CPU 시간은 그대로 남는다.
        pass.InvalidateFrameMetrics(EnemyAbPassResult.FocusLostNote);
        row = report.SummaryCsv().TrimEnd('\n').Split('\n')[1].Split(',');
        Assert.AreEqual(header.Length, row.Length, "The note must not add columns.");
        Assert.AreEqual(string.Empty, row[Array.IndexOf(header, "frame_ms_avg")]);
        Assert.AreEqual(string.Empty, row[Array.IndexOf(header, "frame_ms_p95")]);
        Assert.AreEqual(string.Empty, row[Array.IndexOf(header, "fps_from_avg_frame")]);
        Assert.AreEqual("0", row[Array.IndexOf(header, "frame_metrics_valid")]);
        Assert.AreEqual(string.Empty, row[Array.IndexOf(header, "draw_calls_avg")]);
        Assert.AreEqual("0", row[Array.IndexOf(header, "render_counters_valid")]);
        Assert.AreEqual(EnemyAbPassResult.FocusLostNote, row[Array.IndexOf(header, "unsupported_note")]);
        Assert.AreEqual("600", row[Array.IndexOf(header, "ai_candidates_per_frame")]);
        Assert.AreEqual("12.5", row[Array.IndexOf(header, "ai_cpu_us_median")]);
        Assert.AreEqual("1", row[Array.IndexOf(header, "cpu_valid")]);
        frameRow = report.FramesCsv(pass).TrimEnd('\n').Split('\n')[1].Split(',');
        Assert.AreEqual(string.Empty, frameRow[Array.IndexOf(frameHeader, "frame_ms")]);
        Assert.AreEqual(string.Empty, frameRow[Array.IndexOf(frameHeader, "draw_calls")]);
        Assert.AreEqual("12.5", frameRow[Array.IndexOf(frameHeader, "ai_cpu_us")]);
    }

    [Test]
    public void ComparisonAlsoComparesTimingPassesWithEachOtherWhenTheFirstPassIsTheVerifyPass()
    {
        var report = new EnemyAbRoundReport { Config = new EnemyAbRoundConfig(), Environment = PerfEnvironment.Capture("unit") };
        EnemyAbPassResult verify = Pass("verify-grid", EnemyCandidateMode.SpatialGrid, cpuValid: false);
        verify.Verify = true;
        EnemyAbPassResult grid = Pass("grid-1", EnemyCandidateMode.SpatialGrid, cpuValid: true);
        EnemyAbPassResult brute = Pass("brute-1", EnemyCandidateMode.BruteForce, cpuValid: true);
        for (int frame = 0; frame < 3; frame++)
        {
            // 검증 판만 한 프레임에서 적 수가 다르고, 시간 판 둘은 서로 같다.
            EnemyAbFrameSample odd = Sample(frame, 600, 1UL, 10);
            if (frame == 2) odd.ActiveEnemies = 84;
            verify.Frames.Add(odd);
            grid.Frames.Add(Sample(frame, 600, 1UL, 10));
            brute.Frames.Add(Sample(frame, 10000, 1UL, 80));
        }
        report.Passes.Add(verify);
        report.Passes.Add(grid);
        report.Passes.Add(brute);

        string[] header = EnemyAbRoundReport.ComparisonHeader.Split(',');
        string[] rows = report.ComparisonCsv().TrimEnd('\n').Split('\n');
        Assert.AreEqual(4, rows.Length, "verify vs grid, verify vs brute, then grid vs brute.");
        int reference = Array.IndexOf(header, "reference_pass"), other = Array.IndexOf(header, "other_pass");
        int enemies = Array.IndexOf(header, "active_enemies_identical"), otherMode = Array.IndexOf(header, "other_mode");
        string[] verifyVsGrid = rows[1].Split(','), verifyVsBrute = rows[2].Split(','), gridVsBrute = rows[3].Split(',');
        Assert.AreEqual("verify-grid", verifyVsGrid[reference]);
        Assert.AreEqual("grid-1", verifyVsGrid[other]);
        Assert.AreEqual("0", verifyVsGrid[enemies]);
        Assert.AreEqual("brute-1", verifyVsBrute[other]);
        Assert.AreEqual("grid-1", gridVsBrute[reference]);
        Assert.AreEqual("brute-1", gridVsBrute[other]);
        Assert.AreEqual("BruteForce", gridVsBrute[otherMode]);
        Assert.AreEqual("1", gridVsBrute[enemies]);
        Assert.AreEqual("1", gridVsBrute[Array.IndexOf(header, "position_hashes_identical")]);
    }

    [Test]
    public void RenderCounterNamesExistInThisUnityVersion()
    {
        // 이름이 틀리면 화면에 그리는 실행에서 Draw Call 열이 통째로 비게 된다.
        foreach (string name in new[] { EnemyAbRoundRunner.DrawCallsCounter, EnemyAbRoundRunner.BatchesCounter, EnemyAbRoundRunner.SetPassCallsCounter })
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, name);
            bool valid = recorder.Valid;
            if (valid) recorder.Dispose();
            Assert.IsTrue(valid, "Unknown render counter: " + name);
        }
        ProfilerRecorder unknown = ProfilerRecorder.StartNew(ProfilerCategory.Render, "No Such Counter Count");
        bool unknownValid = unknown.Valid;
        if (unknownValid) unknown.Dispose();
        Assert.IsFalse(unknownValid, "The check must be able to tell an unknown counter apart.");
    }

    private static EnemyAbPassResult Pass(string name, EnemyCandidateMode mode, bool cpuValid) => new EnemyAbPassResult
    {
        Name = name, Mode = mode, Completed = true, CpuValid = cpuValid, FrameMetricsValid = false, RenderCountersValid = false,
        UnsupportedNote = PerfEnvironment.BatchModeNote,
    };

    private static EnemyAbFrameSample Sample(int frame, long candidates, ulong hash, double cpu) => new EnemyAbFrameSample
    {
        Frame = frame, ActiveEnemies = 100, AiQueries = 100, AiCandidates = candidates, SpecialHits = -1,
        AiCpuMicros = cpu, FrameMs = 3.3, PositionHash = hash, MeanDistanceToPlayer = hash == 0xFFUL ? 9.5 : 9.0,
    };
}

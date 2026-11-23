using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// 실제 씬에서 A/B 한 판 측정기를 작은 설정으로 돌려 본다(적 70명, 측정 구간 2초).
// 전체 측정(적 100명)은 시간이 걸리므로 배치 명령으로 따로 돌린다: RebootEnemyAbBenchmark.RunRoundBatch.
public sealed class EnemyAbRoundSceneTests
{
    [UnityTest]
    public IEnumerator SmallRoundComparesBothModesAndRestoresSettings()
    {
        string directory = Path.Combine(Application.temporaryCachePath, "EnemyAbRoundSceneTests");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        int captureFramerate = Time.captureFramerate;
        int vSync = QualitySettings.vSyncCount;
        int targetFrameRate = Application.targetFrameRate;
        bool runInBackground = Application.runInBackground;
        var config = new EnemyAbRoundConfig
        {
            OutputDirectory = directory, Label = "playmode-test", TargetEnemies = 70,
            WindowFrames = 120, TraceFrames = 60, SpecialFrames = new[] { 60, 90 }, TimingRepeats = 1,
        };

        EnemyAbRoundRunner runner = EnemyAbRoundRunner.Run(config);
        float deadline = Time.realtimeSinceStartup + 120f;
        while (!runner.IsDone && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(runner.IsDone, "The benchmark did not finish in time.");
        EnemyAbRoundReport report = runner.Report;
        Object.Destroy(runner.gameObject);
        Assert.IsTrue(report.Succeeded, report.Error);

        // 측정이 끝나면 시간·화면 설정과 후보 수집 방식이 원래대로 돌아와 있어야 한다.
        Assert.AreEqual(captureFramerate, Time.captureFramerate);
        Assert.AreEqual(vSync, QualitySettings.vSyncCount);
        Assert.AreEqual(targetFrameRate, Application.targetFrameRate);
        Assert.AreEqual(runInBackground, Application.runInBackground);
        Assert.AreEqual(1f, Time.timeScale);
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, enemies.Perf.Mode);
        Assert.IsFalse(enemies.Perf.MeasureTime);
        Assert.IsFalse(enemies.Perf.ShadowVerify);

        EnemyAbPassResult verify = report.Find("verify-grid");
        EnemyAbPassResult grid = report.Find("grid-1");
        EnemyAbPassResult brute = report.Find("brute-1");
        Assert.IsNotNull(verify);
        Assert.IsNotNull(grid);
        Assert.IsNotNull(brute);
        foreach (EnemyAbPassResult pass in report.Passes)
        {
            Assert.AreEqual(config.WindowFrames, pass.Frames.Count, pass.Name);
            Assert.AreEqual(verify.Tick, pass.Tick, "Every pass must start from the same tick (same spawn seed).");
            Assert.AreEqual(verify.FillFrames, pass.FillFrames, "Every pass must reach the enemy count on the same frame.");
            Assert.GreaterOrEqual(pass.EnemiesAtWindowStart, config.TargetEnemies);
            // 판마다 물리 주기의 같은 지점에서 시작해야 한다. 한 프레임 어긋나면 이 값이 3ms 넘게 달라진다.
            Assert.AreEqual(verify.PhysicsPhaseMs, pass.PhysicsPhaseMs, 0.5d, pass.Name + " did not start at the same point of the physics cycle.");
            Assert.IsFalse(pass.PlayerMoved, "The scripted player stands still.");
        }

        // 같은 프레임에 같은 수의 질의가 나가고, 전수 방식은 질의마다 활성 적 전부를 본다.
        long gridCandidates = 0, bruteCandidates = 0;
        for (int frame = 0; frame < config.WindowFrames; frame++)
        {
            EnemyAbFrameSample g = grid.Frames[frame], b = brute.Frames[frame];
            Assert.AreEqual(g.ActiveEnemies, b.ActiveEnemies, $"Frame {frame}: enemy counts differ between the modes.");
            Assert.AreEqual(g.AiQueries, b.AiQueries, $"Frame {frame}: query counts differ between the modes.");
            // AI 갱신 때의 활성 적 수가 곧 질의 수다(같은 프레임 뒤쪽에서 죽은 적이 풀로 돌아가면 프레임 끝의 수는 더 적다).
            Assert.AreEqual(b.AiQueries * b.AiQueries, b.AiCandidates, $"Frame {frame}: brute force must look at every enemy.");
            Assert.LessOrEqual(g.AiCandidates, b.AiCandidates);
            Assert.AreEqual(g.SpecialHits, b.SpecialHits, $"Frame {frame}: special-attack hits differ between the modes.");
            Assert.AreEqual(g.KillsTotal, b.KillsTotal);
            gridCandidates += g.AiCandidates;
            bruteCandidates += b.AiCandidates;
        }
        Assert.Less(gridCandidates * 2, bruteCandidates, "The grid should examine far fewer candidates than brute force.");

        // 검증 판: 특수 공격 판정에서 격자가 놓친 적이 없어야 한다. 시간은 검증 판에서는 재지 않는다.
        long missedSpecial = 0, inRadius = 0;
        foreach (EnemyAbFrameSample sample in verify.Frames)
        {
            missedSpecial += sample.ShadowMissedSpecial;
            inRadius += sample.ShadowInRadius;
        }
        Assert.Greater(verify.ShadowQueries, 0);
        Assert.Greater(inRadius, 0);
        Assert.AreEqual(0, missedSpecial, "The grid missed an enemy inside the special-attack radius.");
        Assert.IsFalse(verify.CpuValid);
        Assert.IsTrue(grid.CpuValid);
        Assert.IsTrue(brute.CpuValid);
        Assert.AreEqual(2, CountSpecials(grid));
        long hits = 0;
        foreach (EnemyAbFrameSample sample in grid.Frames)
            if (sample.SpecialHits > 0) hits += sample.SpecialHits;
        Assert.Greater(hits, 0, "The scripted special attacks should hit enemies that reached the player.");

        // 화면에 내보내지 않는 실행(배치)에서는 프레임 시간과 렌더 카운터를 기록하지 않는다.
        if (Application.isBatchMode)
        {
            Assert.IsFalse(grid.FrameMetricsValid);
            Assert.IsFalse(grid.RenderCountersValid);
            Assert.IsNotEmpty(grid.UnsupportedNote);
        }

        // 결과 파일과 궤적.
        Assert.IsNotNull(report.Trace, "The first grid pass should record a trace.");
        Assert.AreEqual(config.TraceFrames, report.Trace.FrameCount);
        Assert.AreEqual(verify.EnemiesAtWindowStart, report.Trace.EnemyCount);
        foreach (string name in new[] { "round_environment.csv", "round_summary.csv", "round_comparison.csv",
                     "round_frames_verify-grid.csv", "round_frames_grid-1.csv", "round_frames_brute-1.csv", "round_trace.txt" })
            Assert.IsTrue(File.Exists(Path.Combine(directory, name)), "Missing result file: " + name);
        string summary = File.ReadAllText(Path.Combine(directory, "round_summary.csv"));
        StringAssert.StartsWith(EnemyAbRoundReport.SummaryHeader, summary);
        StringAssert.Contains("round,playmode-test,", summary);
        EnemyAbTrace reread = EnemyAbTrace.Parse(File.ReadAllText(Path.Combine(directory, "round_trace.txt")));
        Assert.AreEqual(report.Trace.EnemyCount, reread.EnemyCount);

        // 저장한 궤적을 그대로 재생하는 마이크로벤치마크에서는 두 방식의 판정이 완전히 같아야 한다.
        EnemyAbMicroBenchmark.Result micro = EnemyAbMicroBenchmark.Run(reread, new EnemyAbMicroBenchmark.Options
        {
            Repeats = 1, CellSize = enemies.Settings.SpatialCellSize, SeparationDistance = enemies.Settings.SeparationDistance,
            SpecialFrames = new[] { 10, 40 },
        });
        Assert.IsTrue(micro.Equal, "Replaying the recorded trace must give identical judgments in both modes.");
        Assert.Less(micro.Grid.Candidates, micro.BruteForce.Candidates);
        Directory.Delete(directory, true);
    }

    // 게임의 상한(100명)을 넘는 적 수는 성능 실험용 상한으로만 채워지고, 측정이 끝나면 그 상한은 꺼진다.
    [UnityTest]
    public IEnumerator ExperimentCapFillsBeyondTheGameLimitAndIsRemovedAfterwards()
    {
        string directory = Path.Combine(Application.temporaryCachePath, "EnemyAbExperimentCapTests");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        var config = new EnemyAbRoundConfig
        {
            OutputDirectory = directory, Label = "playmode-test", TargetEnemies = 110,
            WindowFrames = 60, TraceFrames = 30, SpecialFrames = new[] { 30 }, TimingRepeats = 1,
        };

        EnemyAbRoundRunner runner = EnemyAbRoundRunner.Run(config);
        float deadline = Time.realtimeSinceStartup + 120f;
        while (!runner.IsDone && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(runner.IsDone, "The benchmark did not finish in time.");
        EnemyAbRoundReport report = runner.Report;
        Object.Destroy(runner.gameObject);
        Assert.IsTrue(report.Succeeded, report.Error);

        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        int gameCap = enemies.Settings.MaximumEnemyCount;
        Assert.Less(gameCap, config.TargetEnemies, "This test needs a target above the game's own cap.");
        Assert.AreEqual(gameCap, report.EnemyCapInSettings);
        Assert.AreEqual(config.TargetEnemies, report.ExperimentEnemyCap);
        Assert.AreEqual(0, enemies.Perf.ExperimentMaximumEnemies, "The experiment cap must be switched off after the run.");
        Assert.AreEqual(gameCap, enemies.Perf.MaximumEnemies(gameCap));

        int expectedFill = EnemyAbRoundConfig.EstimateFillFrames(config.TargetEnemies, enemies.Settings.InitialEnemyCount,
            enemies.Settings.SpawnCountPerWave, enemies.Settings.SpawnInterval, config.CaptureFramerate);
        foreach (EnemyAbPassResult pass in report.Passes)
        {
            Assert.AreEqual(config.TargetEnemies, pass.EnemiesAtWindowStart, pass.Name);
            Assert.AreEqual(expectedFill, pass.FillFrames, pass.Name + " should fill by the game's own spawn rules.");
        }

        EnemyAbPassResult verify = report.Find("verify-grid");
        EnemyAbPassResult grid = report.Find("grid-1");
        EnemyAbPassResult brute = report.Find("brute-1");
        // 공격 전에는 적이 목표 수 그대로이고, 전수 방식은 질의마다 그 전부를 본다.
        for (int frame = 0; frame < 30; frame++)
        {
            Assert.AreEqual(config.TargetEnemies, brute.Frames[frame].ActiveEnemies, $"Frame {frame}");
            Assert.AreEqual(brute.Frames[frame].AiQueries * config.TargetEnemies, brute.Frames[frame].AiCandidates, $"Frame {frame}");
            Assert.Less(grid.Frames[frame].AiCandidates, brute.Frames[frame].AiCandidates, $"Frame {frame}");
        }
        long missedSpecial = 0;
        foreach (EnemyAbFrameSample sample in verify.Frames)
            missedSpecial += sample.ShadowMissedSpecial;
        Assert.AreEqual(0, missedSpecial, "The grid missed an enemy inside the special-attack radius.");
        StringAssert.Contains("experiment_enemy_cap,110", File.ReadAllText(Path.Combine(directory, "round_environment.csv")));
        Directory.Delete(directory, true);
    }

    // 적 수를 여럿 주면 차례로 돌리고, 수마다 폴더를 나눠 쓴 뒤 한 파일로 묶는다.
    [UnityTest]
    public IEnumerator SweepRunsEachEnemyCountAndWritesTheScalingSummary()
    {
        string directory = Path.Combine(Application.temporaryCachePath, "EnemyAbSweepTests");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        int captureFramerate = Time.captureFramerate;
        var template = new EnemyAbRoundConfig
        {
            OutputDirectory = directory, Label = "playmode-test", WindowFrames = 30, TraceFrames = 10,
            SpecialFrames = new int[0], TimingRepeats = 1, RunVerifyPass = false,
        };

        IReadOnlyList<EnemyAbRoundReport> reports = null;
        bool succeeded = false, done = false;
        EnemyAbSweep.Run(template, new[] { 15, 25 }, (result, ok) => { reports = result; succeeded = ok; done = true; });
        float deadline = Time.realtimeSinceStartup + 120f;
        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(done, "The sweep did not finish in time.");
        yield return null;   // 끝난 실행기가 치워질 한 프레임

        Assert.IsTrue(succeeded, reports != null && reports.Count > 0 ? reports[reports.Count - 1].Error : "no report");
        Assert.AreEqual(2, reports.Count);
        Assert.AreEqual(15, reports[0].Config.TargetEnemies);
        Assert.AreEqual(25, reports[1].Config.TargetEnemies);
        Assert.AreEqual(0, reports[1].ExperimentEnemyCap, "Counts within the game's cap need no experiment cap.");
        Assert.AreEqual(0, Object.FindObjectsByType<EnemyAbRoundRunner>(FindObjectsSortMode.None).Length, "Finished runners are removed.");
        Assert.AreEqual(captureFramerate, Time.captureFramerate);
        foreach (int count in new[] { 15, 25 })
            Assert.IsTrue(File.Exists(Path.Combine(directory, EnemyAbSweep.FolderFor(count), "round_summary.csv")), "Missing results for " + count);
        string[] lines = File.ReadAllText(Path.Combine(directory, EnemyAbSweep.ScalingFileName)).TrimEnd('\n').Split('\n');
        Assert.AreEqual(EnemyAbSweep.ScalingHeader, lines[0]);
        Assert.AreEqual(5, lines.Length, "Two passes for each of the two counts.");
        StringAssert.StartsWith("15,", lines[1]);
        StringAssert.StartsWith("25,", lines[3]);
        Directory.Delete(directory, true);
    }

    private static int CountSpecials(EnemyAbPassResult pass)
    {
        int count = 0;
        foreach (EnemyAbFrameSample sample in pass.Frames)
            if (sample.SpecialHits >= 0) count++;
        return count;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

// 성능 도구의 순수 부분: 라운드 프레임 통계의 정의, 오버레이 글 버퍼, 기록 CSV/글의 모양, 벤치마크 인자와 실행 파일 내용.
public sealed class PerfToolsTests
{
    // ---------------------------------------------------------------- 라운드 프레임 통계

    [Test]
    public void AverageOnePercentLowAndMinimumFollowTheirDefinitions()
    {
        var recorder = new RoundFrameRecorder(1000);
        // 990프레임은 10ms, 가장 느린 10프레임(1%)은 50ms 다섯 개와 30ms 다섯 개.
        for (int i = 0; i < 990; i++) recorder.Add(0.010f, 40);
        for (int i = 0; i < 5; i++) recorder.Add(0.050f, 100);
        for (int i = 0; i < 5; i++) recorder.Add(0.030f, 100);

        RoundFrameSummary s = recorder.Summarize();
        Assert.IsTrue(s.HasFrames);
        Assert.AreEqual(1000, s.Frames);
        Assert.AreEqual(0, s.IgnoredFrames);
        Assert.AreEqual(0, s.OverflowFrames);
        Assert.AreEqual(9.9 + 0.25 + 0.15, s.TotalSeconds, 1e-4);
        Assert.AreEqual(1000d / 10.3, s.AverageFps, 0.01, "Average FPS is frames divided by their total time.");
        Assert.AreEqual(10, s.SlowestFrames, "1% of 1000 frames.");
        Assert.AreEqual(10d / 0.4, s.OnePercentLowFps, 0.01, "1% low is the average over the slowest 1% of frames.");
        Assert.AreEqual(20d, s.MinimumFps, 0.01, "Minimum FPS comes from the single slowest frame.");
        Assert.AreEqual(10.3, s.AverageFrameMs, 0.001);
        Assert.AreEqual(50d, s.WorstFrameMs, 0.001);
        Assert.AreEqual((990 * 40 + 10 * 100) / 1000d, s.EnemiesAverage, 1e-9);
        Assert.AreEqual(100, s.EnemiesMax);

        // 요약은 다시 해도 같고, 정렬된 버퍼에 이어서 더해도 맞다.
        Assert.AreEqual(s.OnePercentLowFps, recorder.Summarize().OnePercentLowFps, 1e-9);
        recorder.Add(0.1f, 7);
        Assert.AreEqual(1001, recorder.Summarize().Frames);
        Assert.AreEqual(10d, recorder.Summarize().MinimumFps, 1e-3);
    }

    [Test]
    public void TheSlowestOnePercentIsRoundedUpAndNeverEmpty()
    {
        var recorder = new RoundFrameRecorder(500);
        recorder.Add(0.020f, 0);
        Assert.AreEqual(1, recorder.Summarize().SlowestFrames);
        Assert.AreEqual(50d, recorder.Summarize().OnePercentLowFps, 0.01);

        recorder.Clear();
        for (int i = 0; i < 100; i++) recorder.Add(0.010f, 0);
        Assert.AreEqual(1, recorder.Summarize().SlowestFrames);
        recorder.Clear();
        for (int i = 0; i < 148; i++) recorder.Add(0.010f, 0);
        recorder.Add(0.040f, 0);
        recorder.Add(0.020f, 0);
        RoundFrameSummary s = recorder.Summarize();
        Assert.AreEqual(2, s.SlowestFrames, "150 frames: 1.5 rounds up to 2.");
        Assert.AreEqual(2d / 0.06, s.OnePercentLowFps, 0.01);
        Assert.AreEqual(25d, s.MinimumFps, 0.01);
    }

    [Test]
    public void AnEmptyRoundHasNoNumbersAndBadTimesAreNotCounted()
    {
        var recorder = new RoundFrameRecorder(16);
        RoundFrameSummary empty = recorder.Summarize();
        Assert.IsFalse(empty.HasFrames);
        Assert.AreEqual(0, empty.Frames);
        Assert.AreEqual(0, empty.SlowestFrames);
        Assert.IsNaN(empty.AverageFps);
        Assert.IsNaN(empty.OnePercentLowFps);
        Assert.IsNaN(empty.MinimumFps);
        Assert.IsNaN(empty.AverageFrameMs);
        Assert.IsNaN(empty.EnemiesAverage);
        Assert.AreEqual(0, empty.EnemiesMax);

        recorder.Add(0f, 5);
        recorder.Add(-0.01f, 5);
        recorder.Add(float.NaN, 5);
        recorder.Add(float.PositiveInfinity, 5);
        RoundFrameSummary ignored = recorder.Summarize();
        Assert.IsFalse(ignored.HasFrames);
        Assert.AreEqual(4, ignored.IgnoredFrames);

        recorder.Add(0.025f, 3);
        RoundFrameSummary one = recorder.Summarize();
        Assert.AreEqual(1, one.Frames);
        Assert.AreEqual(4, one.IgnoredFrames);
        Assert.AreEqual(40d, one.AverageFps, 0.01);
        Assert.AreEqual(40d, one.OnePercentLowFps, 0.01);
        Assert.AreEqual(40d, one.MinimumFps, 0.01);

        recorder.Clear();
        Assert.AreEqual(0, recorder.FrameCount);
        Assert.AreEqual(0, recorder.IgnoredFrames);
        Assert.IsFalse(recorder.Summarize().HasFrames);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundFrameRecorder(0));
    }

    [Test]
    public void FramesPastTheBufferStillCountExceptInTheOnePercentLow()
    {
        var recorder = new RoundFrameRecorder(4);
        recorder.Add(0.010f, 1);
        recorder.Add(0.010f, 1);
        recorder.Add(0.010f, 1);
        recorder.Add(0.020f, 1);
        recorder.Add(0.050f, 9);   // past the buffer
        recorder.Add(0.010f, 1);   // past the buffer
        RoundFrameSummary s = recorder.Summarize();
        Assert.AreEqual(6, s.Frames);
        Assert.AreEqual(2, s.OverflowFrames);
        Assert.AreEqual(0.11, s.TotalSeconds, 1e-6);
        Assert.AreEqual(6d / 0.11, s.AverageFps, 0.01);
        Assert.AreEqual(20d, s.MinimumFps, 0.01, "The slowest frame counts even past the buffer.");
        Assert.AreEqual(50d, s.OnePercentLowFps, 0.01, "The 1% low uses the stored frames only.");
        Assert.AreEqual(9, s.EnemiesMax);
    }

    [Test]
    public void AddingAFrameDoesNotAllocate()
    {
        var recorder = new RoundFrameRecorder(64);
        NUnit.Framework.TestDelegate add = () => recorder.Add(0.016f, 10);
        add();
        Assert.That(add, Is.Not.AllocatingGCMemory());
        Assert.AreEqual(2, recorder.FrameCount);
    }

    // ---------------------------------------------------------------- 오버레이 글 버퍼

    [Test]
    public void TextBufferWritesNumbersWithoutStrings()
    {
        var text = new PerfTextBuffer(128);
        text.Append("FPS ");
        text.Append(143.25, 1);
        text.Append(' ');
        text.Append(-0.004, 2);
        text.Append(' ');
        text.Append(0.05, 3);
        text.Append(' ');
        text.Append(12.5, 0);
        text.Append(' ');
        text.Append(-3.75, 1);
        text.Append(' ');
        text.Append(7L);
        text.Append(' ');
        text.Append(-12L);
        text.Append(' ');
        text.Append(0L);
        text.Append(' ');
        text.Append(double.NaN, 1);
        text.Append(' ');
        text.Append(1e20, 1);
        text.Append(' ');
        text.Append(long.MinValue);
        Assert.AreEqual("FPS 143.3 0.00 0.050 13 -3.8 7 -12 0 n/a n/a -9223372036854775808", text.ToString());

        text.Clear();
        Assert.AreEqual(0, text.Length);
        text.Append(1234.5678, 9);   // at most six decimals
        Assert.AreEqual("1234.567800", text.ToString());

        var small = new PerfTextBuffer(5);
        small.Append("abcdefgh");
        Assert.AreEqual("abcde", small.ToString(), "Characters that do not fit are dropped.");
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerfTextBuffer(0));
    }

    [Test]
    public void TextBufferDoesNotAllocateWhileWriting()
    {
        var text = new PerfTextBuffer(256);
        NUnit.Framework.TestDelegate write = () =>
        {
            text.Clear();
            text.Append("FPS ");
            text.Append(98.76, 2);
            text.Append(" enemies ");
            text.Append(12345L);
        };
        // 첫 실행은 글자 상수를 처음 만드는 몫이 섞이므로 한 번 돌린 뒤 잰다(오버레이도 같은 글을 되풀이해 쓴다).
        write();
        Assert.That(write, Is.Not.AllocatingGCMemory());
        Assert.AreEqual("FPS 98.76 enemies 12345", text.ToString());
    }

    // ---------------------------------------------------------------- 기록 CSV와 글

    [Test]
    public void TheCsvRowMatchesTheHeaderAndEscapesDeviceNames()
    {
        RoundPerfRecord record = SampleRecord();
        string[] header = RoundPerfRecord.CsvHeader.Split(',');
        List<string> row = SplitCsv(record.CsvRow());
        Assert.AreEqual(header.Length, row.Count, "One value per column.");
        Dictionary<string, string> values = ToColumns(header, row);
        Assert.AreEqual("1", values["format_version"]);
        Assert.AreEqual("2026-11-24 21:05:09", values["ended_at_local"]);
        Assert.AreEqual("2026-11-24T12:05:09Z", values["ended_at_utc"]);
        Assert.AreEqual("result", values["end_reason"]);
        Assert.AreEqual("1", values["benchmark"]);
        Assert.AreEqual("1000", values["frames"]);
        Assert.AreEqual("97.09", values["avg_fps"]);
        Assert.AreEqual("25", values["one_percent_low_fps"]);
        Assert.AreEqual("20", values["min_fps"]);
        Assert.AreEqual("100", values["enemy_cap_min"]);
        Assert.AreEqual("300", values["enemy_cap_max"]);
        Assert.AreEqual("mixed", values["search_mode"]);
        Assert.AreEqual("Lab PC, seat 7", values["device_model"], "A comma inside a value is quoted.");
        Assert.AreEqual("CPU \"X\" 3.2GHz", values["processor"], "Quotes inside a value are doubled.");
        Assert.AreEqual("1920x1080", values["screen"]);
        Assert.AreEqual("59.94", values["refresh_hz"]);
        Assert.AreEqual("0", values["is_editor"]);
        Assert.AreEqual("player-release", values["environment_class"]);

        // 프레임이 없는 판은 숫자 칸을 비운다(0으로 채우지 않는다).
        record.Summary = new RoundFrameRecorder(4).Summarize();
        Dictionary<string, string> empty = ToColumns(header, SplitCsv(record.CsvRow()));
        Assert.AreEqual("0", empty["frames"]);
        Assert.AreEqual(string.Empty, empty["avg_fps"]);
        Assert.AreEqual(string.Empty, empty["one_percent_low_fps"]);
        Assert.AreEqual(string.Empty, empty["min_fps"]);
    }

    [Test]
    public void TheSummaryTextAndOverlayLineSayWhatWasMeasured()
    {
        RoundPerfRecord record = SampleRecord();
        string text = record.SummaryText();
        StringAssert.Contains("Average FPS: 97.1", text);
        StringAssert.Contains("1% low FPS: 25.0 (average of the slowest 1% of frames: 10 frames)", text);
        StringAssert.Contains("Minimum FPS: 20.0 (slowest frame 50.00 ms)", text);
        StringAssert.Contains("Enemies: average 40.6, max 100 | enemy cap 100-300 (game setting 100)", text);
        StringAssert.Contains("Device: Lab PC, seat 7", text);
        StringAssert.Contains("Benchmark mode", text);
        StringAssert.Contains("experiment enemy cap", text);
        StringAssert.Contains("not the spatial grid for the whole round", text);
        StringAssert.DoesNotContain("Unity Editor", text);

        string line = record.OverlayLine();
        Assert.AreEqual("Last round: avg 97.1, 1% low 25.0, min 20.0 FPS\n  1000 frames, enemies avg 41, max 100", line);
        foreach (char c in line)
            Assert.Less((int)c, 128, "The overlay font only has ASCII.");
        foreach (string part in line.Split('\n'))
            Assert.LessOrEqual(part.Length, 50, "Each overlay line fits the panel.");

        // 파일을 쓰지 못한 판은 둘째 줄이 그 사실을 알린다.
        string unsaved = record.OverlayLine(saved: false);
        Assert.AreEqual("Last round: avg 97.1, 1% low 25.0, min 20.0 FPS\n  1000 frames, NOT SAVED (see the log)", unsaved);
        foreach (string part in unsaved.Split('\n'))
            Assert.LessOrEqual(part.Length, 50, "Each overlay line fits the panel.");

        record.EndReason = RoundPerfRecord.EndedByQuit;
        StringAssert.Contains("closed in the middle of the round", record.SummaryText());
        StringAssert.EndsWith("max 100 (quit)", record.OverlayLine());

        // 네 자리 FPS와 아주 많은 프레임에서도 한 줄이 패널 폭을 넘지 않는다.
        var fast = new RoundFrameRecorder(16);
        for (int i = 0; i < 15; i++) fast.Add(0.0004f, 300);
        fast.Add(0.0025f, 300);
        record.Summary = fast.Summarize();
        string fastLine = record.OverlayLine();
        // 16프레임 / 0.0085초 = 1882.4 FPS, 가장 느린 1%(1프레임)와 최저는 2.5ms = 400 FPS.
        StringAssert.StartsWith("Last round: avg 1882, 1% low 400, min 400 FPS", fastLine);
        foreach (string part in fastLine.Split('\n'))
            Assert.LessOrEqual(part.Length, 50, part);
    }

    [Test]
    public void WritingMakesOneTimestampedCsvAndTextPerRound()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RebootPerfRecordTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            RoundPerfRecord record = SampleRecord();
            string[] first = record.Write(directory);
            string[] second = record.Write(directory);
            Assert.AreEqual(Path.Combine(directory, "round_20261124_210509.csv"), first[0]);
            Assert.AreEqual(Path.Combine(directory, "round_20261124_210509.txt"), first[1]);
            Assert.AreEqual(Path.Combine(directory, "round_20261124_210509_2.csv"), second[0], "A round ending in the same second gets its own files.");
            string[] lines = File.ReadAllText(first[0], Encoding.UTF8).TrimEnd('\n').Split('\n');
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual(RoundPerfRecord.CsvHeader, lines[0]);
            Assert.AreEqual(record.CsvRow(), lines[1]);
            Assert.AreEqual(record.SummaryText(), File.ReadAllText(first[1], Encoding.UTF8));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    // ---------------------------------------------------------------- 벤치마크 인자와 실행 파일

    [Test]
    public void BenchmarkArgumentsAreReadSafely()
    {
        Assert.IsTrue(PerfBenchmark.IsRequested(new[] { "REBOOT.exe", "-rebootBenchmark" }));
        Assert.IsFalse(PerfBenchmark.IsRequested(new[] { "REBOOT.exe", "-rebootEnemyAb" }));
        Assert.IsFalse(PerfBenchmark.IsRequested(null));

        Assert.AreEqual(0, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmark" }));
        Assert.AreEqual(300, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmark", "-rebootBenchmarkCap", "300" }));
        Assert.AreEqual(300, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmarkCap", "5000" }), "Never above the experiment limit.");
        Assert.AreEqual(0, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmarkCap", "-20" }));
        Assert.AreEqual(0, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmarkCap", "many" }));
        Assert.AreEqual(0, PerfBenchmark.CapFromArguments(new[] { "-rebootBenchmarkCap" }), "A missing value is ignored.");

        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, PerfBenchmark.SearchFromArguments(new[] { "-rebootBenchmark" }));
        Assert.AreEqual(EnemyCandidateMode.BruteForce, PerfBenchmark.SearchFromArguments(new[] { "-rebootBenchmarkSearch", "brute" }));
        Assert.AreEqual(EnemyCandidateMode.BruteForce, PerfBenchmark.SearchFromArguments(new[] { "-rebootBenchmarkSearch", "BruteForce" }));
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, PerfBenchmark.SearchFromArguments(new[] { "-rebootBenchmarkSearch", "grid" }));

        // A/B 측정기가 도는 실행(플레이어 + -rebootEnemyAb)에서는 라운드 기록을 끈다. Editor에서는 그 측정기가 인자로 돌지 않는다.
        Assert.IsTrue(PerfBenchmark.IsEnemyAbRun(new[] { "REBOOT.exe", "-rebootEnemyAb", "-rebootEnemies", "100,200,300" }, false));
        Assert.IsFalse(PerfBenchmark.IsEnemyAbRun(new[] { "REBOOT.exe", "-rebootEnemyAb" }, true));
        Assert.IsFalse(PerfBenchmark.IsEnemyAbRun(new[] { "REBOOT.exe", "-rebootBenchmark" }, false));
        Assert.IsFalse(PerfBenchmark.IsEnemyAbRun(null, false));
    }

    [Test]
    public void LauncherScriptsStartTheBenchmarkAndPointAtTheResults()
    {
        string bat = PerfBenchmark.WindowsLauncherScript("REBOOT.exe", "DefaultCompany", "CGD");
        StringAssert.StartsWith("@echo off\r\n", bat);
        StringAssert.Contains("start \"\" /wait \"REBOOT.exe\" -rebootBenchmark %*", bat);
        StringAssert.Contains(@"set ""RESULTS=%USERPROFILE%\AppData\LocalLow\DefaultCompany\CGD\perf""", bat);
        StringAssert.Contains("round_*.txt", bat);
        Assert.IsFalse(bat.Replace("\r\n", string.Empty).Contains("\n"), "cmd.exe scripts use CRLF line ends.");
        foreach (char c in bat)
            Assert.Less((int)c, 128, "The batch file stays ASCII so any code page reads it.");
        // 시작 전의 가장 최근 파일을 기억해 두고, 끝난 뒤 그보다 새 파일만 이번 결과로 보여 준다.
        AssertInOrder(bat, "if not defined BEFORE set \"BEFORE=%%F\"", "start \"\" /wait",
            "if not defined LATEST set \"LATEST=%%F\"", "if defined LATEST if /i not \"%LATEST%\"==\"%BEFORE%\" set \"NEW=1\"",
            "if defined NEW (", "type \"%RESULTS%\\%LATEST%\"", ") else (", "echo " + PerfBenchmark.NoNewRecordingMessage);
        StringAssert.DoesNotContain("(", PerfBenchmark.NoNewRecordingMessage, "A parenthesis would end the cmd.exe block early.");
        StringAssert.DoesNotContain(")", PerfBenchmark.NoNewRecordingMessage);

        string command = PerfBenchmark.MacLauncherScript("REBOOT.app", "DefaultCompany", "CGD");
        StringAssert.StartsWith("#!/bin/sh\n", command);
        StringAssert.Contains("open -W -n \"./REBOOT.app\" --args -rebootBenchmark \"$@\"", command);
        StringAssert.Contains("RESULTS=\"$HOME/Library/Application Support/DefaultCompany/CGD/perf\"", command);
        StringAssert.Contains("round_*.txt", command);
        Assert.IsFalse(command.Contains("\r"), "Shell scripts use LF line ends.");
        AssertInOrder(command, "BEFORE=$(ls -t \"$RESULTS\"/round_*.txt", "open -W -n", "LATEST=$(ls -t \"$RESULTS\"/round_*.txt",
            "if [ -n \"$LATEST\" ] && [ \"$LATEST\" != \"$BEFORE\" ]; then", "cat \"$LATEST\"", "else",
            "echo \"" + PerfBenchmark.NoNewRecordingMessage + "\"");
    }

    // ---------------------------------------------------------------- 오버레이의 AI 시간 측정

    // 오버레이는 자기 스위치(LiveTiming)로 AI 시간을 잰다. A/B 측정 판 표시(MeasureTime)는 켜지 않는다:
    // 적 쪽 코드가 그 값으로 측정 판을 가려 생성 방식을 바꿀 수 있기 때문이다.
    [Test]
    public void TheOverlayTimesTheAiWithoutMarkingAMeasurementRound()
    {
        var probe = new EnemyPerfProbe(new EnemyRegistry(), new EnemySpatialGrid(4f));
        Assert.IsFalse(probe.IsTiming);
        Assert.AreEqual(0L, probe.BeginTiming());

        probe.LiveTiming = true;
        Assert.IsTrue(probe.IsTiming);
        Assert.IsFalse(probe.MeasureTime, "The A/B measurement flag stays off.");
        Assert.IsFalse(probe.ShadowVerify);
        Assert.AreEqual(0, probe.ExperimentMaximumEnemies);
        long started = probe.BeginTiming();
        Assert.Greater(started, 0L);
        while (System.Diagnostics.Stopwatch.GetTimestamp() == started) { }
        probe.EndAiTiming(started);
        Assert.Greater(probe.Counters.AiTicks, 0L, "Live timing fills the same AI time counter.");

        // 전수 대조 중에는 그 비용이 섞이므로 재지 않는다. A/B 측정기의 MeasureTime은 예전처럼 언제나 잰다.
        probe.ShadowVerify = true;
        Assert.IsFalse(probe.IsTiming);
        Assert.AreEqual(0L, probe.BeginTiming());
        probe.MeasureTime = true;
        Assert.IsTrue(probe.IsTiming);
        probe.MeasureTime = false;
        probe.ShadowVerify = false;
        probe.LiveTiming = false;
        Assert.IsFalse(probe.IsTiming);
    }

    // ---------------------------------------------------------------- 빌드 메뉴

    // 메뉴 경로의 '/'는 하위 메뉴를 만든다. 빌드 항목은 Tools > REBOOT Build 바로 아래에 있어야 문서의 경로와 맞는다.
    [Test]
    public void TheBuildMenuItemsSitDirectlyUnderREBOOTBuild()
    {
        // Assets/Editor에는 asmdef가 없어 RebootBuild는 Assembly-CSharp-Editor에 들어간다(테스트 어셈블리가 직접 참조할 수 없다).
        Type build = Type.GetType("RebootBuild, Assembly-CSharp-Editor", false);
        Assert.IsNotNull(build, "RebootBuild (Assets/Editor/Build) is compiled into the Editor.");
        var paths = new List<string>();
        foreach (System.Reflection.MethodInfo method in build.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            foreach (UnityEditor.MenuItem item in method.GetCustomAttributes(typeof(UnityEditor.MenuItem), false))
                paths.Add(item.menuItem);
        CollectionAssert.AreEquivalent(new[]
        {
            "Tools/REBOOT Build/Build Mac (REBOOT.app)",
            "Tools/REBOOT Build/Build Windows (REBOOT.exe)",
        }, paths);
    }

    // ---------------------------------------------------------------- 도우미

    // parts가 text 안에 이 순서로 나오는지 확인한다.
    private static void AssertInOrder(string text, params string[] parts)
    {
        int from = 0;
        foreach (string part in parts)
        {
            int at = text.IndexOf(part, from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "Expected \"" + part + "\" after position " + from + " in:\n" + text);
            from = at + part.Length;
        }
    }

    private static RoundPerfRecord SampleRecord()
    {
        var recorder = new RoundFrameRecorder(1000);
        for (int i = 0; i < 990; i++) recorder.Add(0.010f, 40);
        for (int i = 0; i < 5; i++) recorder.Add(0.050f, 100);
        for (int i = 0; i < 5; i++) recorder.Add(0.030f, 100);
        return new RoundPerfRecord
        {
            Summary = recorder.Summarize(),
            Environment = new PerfEnvironment
            {
                DeviceModel = "Lab PC, seat 7",
                ProcessorType = "CPU \"X\" 3.2GHz",
                ProcessorCount = 12,
                SystemMemoryMb = 16384,
                GraphicsDeviceName = "GPU 4060",
                GraphicsDeviceType = "Direct3D11",
                GraphicsMemoryMb = 8192,
                OperatingSystem = "Windows 11 (10.0.22631) 64bit",
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                QualityLevel = "PC",
                UnityVersion = "6000.3.24f1",
                EnvironmentClass = "player-release",
                ScriptingBackend = "Mono",
            },
            EndedLocal = new DateTime(2026, 11, 24, 21, 5, 9),
            EndedUtc = new DateTime(2026, 11, 24, 12, 5, 9, DateTimeKind.Utc),
            EndReason = RoundPerfRecord.EndedAtResult,
            Benchmark = true,
            EnemyCapMin = 100,
            EnemyCapMax = 300,
            EnemyCapInSettings = 100,
            SearchMode = RoundPerfRecord.MixedSearchMode,
            FullScreenMode = "FullScreenWindow",
            RefreshHz = 59.94,
            VSyncCount = 0,
            TargetFrameRate = -1,
            AppVersion = "0.1.0",
        };
    }

    private static Dictionary<string, string> ToColumns(string[] header, List<string> row)
    {
        var values = new Dictionary<string, string>();
        for (int i = 0; i < header.Length; i++)
            values[header[i]] = row[i];
        return values;
    }

    // 따옴표로 묶인 칸(쉼표와 두 번 쓴 따옴표 포함)을 처리하는 작은 CSV 한 줄 분리기.
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}

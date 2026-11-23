using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// "실제 한 판" 측정의 설정. 같은 설정이면 두 방식이 같은 시드, 같은 적 수, 같은 공격 시점으로 돈다.
[Serializable]
public sealed class EnemyAbRoundConfig
{
    public string ScenePath = "Assets/Scenes/Main.unity";
    public string OutputDirectory = "";
    // 결과에 함께 적는 측정 이름(예: mac-editor-batch-verification, demo-pc-player). 어느 기기의 수치인지 구분하는 용도다.
    public string Label = "unlabeled";
    // 게임 시간을 프레임당 1/60초로 고정해, 실제 걸린 시간과 무관하게 같은 순서로 진행시킨다.
    public int CaptureFramerate = 60;
    public int Seed = 20261123;
    // 활성 적이 이만큼 될 때까지 기다린 뒤 측정 구간을 시작한다(설정의 최대 적 수를 넘지 않는다).
    public int TargetEnemies = 100;
    public int MaxFillFrames = 2400;
    public int WindowFrames = 900;
    // 측정 구간의 처음 이만큼을 궤적으로 저장한다(마이크로벤치마크의 입력). 이 구간에는 공격이 없어야 한다.
    public int TraceFrames = 300;
    // 측정 구간의 이 프레임들에서 플레이어 위치에 특수 공격 범위 판정을 한 번씩 한다.
    public int[] SpecialFrames = { 300, 500, 700 };
    // 방식마다 시간을 재는 판 수. 순서 효과를 줄이려고 격자, 전수, 전수, 격자 순으로 돈다.
    public int TimingRepeats = 2;
    public bool RunVerifyPass = true;

    // 게임의 생성 규칙(처음 몇 명 + 일정 간격마다 몇 명씩)으로 목표 수가 차는 데 걸리는 프레임 수.
    public static int EstimateFillFrames(int target, int initialCount, int countPerWave, float spawnInterval, int captureFramerate)
    {
        int missing = Math.Max(0, target - Math.Max(0, initialCount));
        if (missing == 0) return 0;
        int perWave = Math.Max(1, countPerWave);
        int waves = (missing + perWave - 1) / perWave;
        return (int)Math.Ceiling(waves * (double)Math.Max(0f, spawnInterval) * Math.Max(1, captureFramerate));
    }
}

public struct EnemyAbFrameSample
{
    public int Frame, ActiveEnemies;
    public long AiQueries, AiCandidates, SpecialQueries, SpecialCandidates;
    public int SpecialHits;               // 이 프레임에 특수 공격 판정이 없었으면 -1
    public int KillsTotal, PlayerHitsTotal, Score;
    public double AiCpuMicros, SpecialCpuMicros, FrameMs;
    public long DrawCalls, Batches, SetPassCalls;
    public ulong PositionHash;
    public double MeanDistanceToPlayer;   // 활성 적과 플레이어 사이 XZ 거리의 평균(m). 판 사이 위치 차이를 수치로 보는 용도
    public long ShadowInRadius, ShadowMissedAi, ShadowMissedSpecial;
}

public sealed class EnemyAbPassResult
{
    public string Name;
    public EnemyCandidateMode Mode;
    public bool Verify, Completed;
    public string Error;
    public int Tick, FillFrames, EnemiesAtWindowStart, KillsAtWindowStart, PlayerHitsAtWindowStart;
    public bool PlayerMoved, CameraMoved, TraceRecorded;
    public bool CpuValid, FrameMetricsValid, RenderCountersValid;
    public string CpuNote = string.Empty, UnsupportedNote = string.Empty;
    public long ShadowQueries, ShadowMissedKnockback;
    public float ShadowMissedMaxDrift;
    // 측정 구간을 시작할 때 프레임 시각이 마지막 물리 스텝보다 앞선 정도(ms). 판마다 거의 같아야 같은 조건이다.
    public double PhysicsPhaseMs;
    public readonly List<EnemyAbFrameSample> Frames = new();

    // EnemyRuntime.PrepareRound가 적 생성 난수에 쓰는 시드. 같은 tick이면 같은 생성 위치가 나온다.
    public int SpawnSeed => unchecked(7919 + Tick * 104729);

    public const string FocusLostNote =
        "the game window was not focused during this pass: frame time / FPS / render counters are not recorded";

    // 측정 도중 프레임 지표를 믿을 수 없게 됐을 때(창이 포커스를 잃음 등) 이 판의 프레임 지표만 무효로 돌린다.
    // 후보 수와 CPU 시간은 화면 표시와 무관하므로 그대로 둔다.
    public void InvalidateFrameMetrics(string note)
    {
        FrameMetricsValid = false;
        RenderCountersValid = false;
        UnsupportedNote = note ?? string.Empty;
    }
}

// 측정 구간을 나눈 조각. 공격 전(적 수 고정)과 공격 이후(적이 죽고 다시 생성됨)를 따로 집계한다.
public readonly struct EnemyAbSegment
{
    public readonly string Name;
    public readonly int First, Count;
    public EnemyAbSegment(string name, int first, int count) { Name = name; First = first; Count = count; }
}

public sealed class EnemyAbRoundReport
{
    public EnemyAbRoundConfig Config;
    public PerfEnvironment Environment;
    public readonly List<EnemyAbPassResult> Passes = new();
    public EnemyAbTrace Trace;
    public string Error;
    // 게임 설정의 활성 Enemy 상한과, 이번 측정에서 건 성능 실험용 상한(쓰지 않았으면 0).
    public int EnemyCapInSettings, ExperimentEnemyCap;

    public bool Succeeded
    {
        get
        {
            if (!string.IsNullOrEmpty(Error) || Passes.Count == 0) return false;
            foreach (EnemyAbPassResult pass in Passes)
                if (!pass.Completed) return false;
            return true;
        }
    }

    public EnemyAbPassResult Find(string name)
    {
        foreach (EnemyAbPassResult pass in Passes)
            if (pass.Name == name) return pass;
        return null;
    }

    // all: 측정 구간 전체. steady: 첫 공격 전(적 구성이 바뀌지 않는다). attacks: 첫 공격부터 끝까지.
    public List<EnemyAbSegment> Segments(int frames)
    {
        var segments = new List<EnemyAbSegment> { new EnemyAbSegment("all", 0, frames) };
        int firstSpecial = int.MaxValue;
        foreach (int frame in Config.SpecialFrames ?? Array.Empty<int>())
            if (frame >= 0) firstSpecial = Math.Min(firstSpecial, frame);
        if (firstSpecial > 0 && firstSpecial < frames)
        {
            segments.Add(new EnemyAbSegment("steady", 0, firstSpecial));
            segments.Add(new EnemyAbSegment("attacks", firstSpecial, frames - firstSpecial));
        }
        return segments;
    }

    public const string SummaryHeader =
        "kind,label,environment_class,pass,mode,verify_pass,completed,error,tick,spawn_seed,capture_framerate,fill_frames," +
        "physics_phase_ms,segment,segment_first_frame,segment_frames,enemies_min,enemies_max,enemies_avg," +
        "ai_queries,ai_candidates,ai_candidates_per_query,ai_candidates_per_frame," +
        "special_queries,special_candidates,special_hits,kills,player_hits,score_at_end," +
        "ai_cpu_us_avg,ai_cpu_us_median,ai_cpu_us_p95,ai_cpu_us_max,special_cpu_us_total,cpu_valid,cpu_note," +
        "frame_ms_avg,frame_ms_median,frame_ms_p95,fps_from_avg_frame,frame_metrics_valid," +
        "draw_calls_avg,batches_avg,setpass_calls_avg,render_counters_valid,unsupported_note," +
        "shadow_in_radius,shadow_missed_ai,shadow_missed_special,shadow_queries_whole_window," +
        "shadow_missed_knockback_whole_window,shadow_missed_max_drift_m_whole_window," +
        "player_moved,camera_moved";

    public const string FramesHeader =
        "pass,mode,frame,active_enemies,ai_queries,ai_candidates,special_queries,special_candidates,special_hits," +
        "kills_total,player_hits_total,score,ai_cpu_us,special_cpu_us,frame_ms,draw_calls,batches,setpass_calls," +
        "positions_hash,mean_distance_to_player_m,shadow_in_radius,shadow_missed_ai,shadow_missed_special";

    public const string ComparisonHeader =
        "reference_pass,reference_mode,other_pass,other_mode,segment,frames_compared,first_position_hash_difference_frame," +
        "position_hashes_identical,max_mean_distance_difference_m,active_enemies_identical,special_hits_identical," +
        "kills_identical,player_hits_identical,score_identical";

    public string SummaryCsv()
    {
        var text = new StringBuilder(SummaryHeader).Append('\n');
        foreach (string line in SummaryLines())
            text.Append(line).Append('\n');
        return text.ToString();
    }

    // 머리줄을 뺀 요약 행들(판 × 구간). 여러 측정을 한 파일로 묶을 때도 쓴다.
    public List<string> SummaryLines()
    {
        var lines = new List<string>();
        foreach (EnemyAbPassResult pass in Passes)
            foreach (EnemyAbSegment segment in Segments(pass.Frames.Count))
                lines.Add(string.Join(",", SummaryRow(pass, segment)));
        return lines;
    }

    private List<string> SummaryRow(EnemyAbPassResult pass, EnemyAbSegment segment)
    {
        int end = segment.First + segment.Count;
        long aiQueries = 0, aiCandidates = 0, specialQueries = 0, specialCandidates = 0, specialHits = 0;
        long shadowInRadius = 0, shadowMissedAi = 0, shadowMissedSpecial = 0;
        double specialCpu = 0d;
        var enemies = new List<double>(segment.Count);
        var cpu = new List<double>(segment.Count);
        var frameMs = new List<double>(segment.Count);
        var drawCalls = new List<double>(segment.Count);
        var batches = new List<double>(segment.Count);
        var setPass = new List<double>(segment.Count);
        for (int i = segment.First; i < end; i++)
        {
            EnemyAbFrameSample f = pass.Frames[i];
            aiQueries += f.AiQueries; aiCandidates += f.AiCandidates;
            specialQueries += f.SpecialQueries; specialCandidates += f.SpecialCandidates;
            if (f.SpecialHits >= 0) specialHits += f.SpecialHits;
            shadowInRadius += f.ShadowInRadius; shadowMissedAi += f.ShadowMissedAi; shadowMissedSpecial += f.ShadowMissedSpecial;
            specialCpu += f.SpecialCpuMicros;
            enemies.Add(f.ActiveEnemies); cpu.Add(f.AiCpuMicros); frameMs.Add(f.FrameMs);
            drawCalls.Add(f.DrawCalls); batches.Add(f.Batches); setPass.Add(f.SetPassCalls);
        }
        // 구간 안에서 늘어난 처치·피격 수(구간 직전 값과의 차이).
        int killsBefore = segment.First > 0 ? pass.Frames[segment.First - 1].KillsTotal : pass.KillsAtWindowStart;
        int hitsBefore = segment.First > 0 ? pass.Frames[segment.First - 1].PlayerHitsTotal : pass.PlayerHitsAtWindowStart;
        EnemyAbFrameSample last = segment.Count > 0 ? pass.Frames[end - 1] : default;
        double avgFrame = pass.FrameMetricsValid ? PerfCsv.Mean(frameMs) : double.NaN;
        bool whole = segment.Name == "all";

        return new List<string>
        {
            "round", PerfCsv.Escape(Environment.Label), Environment.EnvironmentClass, pass.Name, pass.Mode.ToString(),
            pass.Verify ? "1" : "0", pass.Completed ? "1" : "0", PerfCsv.Escape(pass.Error),
            PerfCsv.Number(pass.Tick), PerfCsv.Number(pass.SpawnSeed), PerfCsv.Number(Config.CaptureFramerate), PerfCsv.Number(pass.FillFrames),
            PerfCsv.Number(pass.PhysicsPhaseMs),
            segment.Name, PerfCsv.Number(segment.First), PerfCsv.Number(segment.Count),
            PerfCsv.Number(PerfCsv.Percentile(enemies, 0d)), PerfCsv.Number(PerfCsv.Percentile(enemies, 1d)), PerfCsv.Number(PerfCsv.Mean(enemies)),
            PerfCsv.Number(aiQueries), PerfCsv.Number(aiCandidates),
            PerfCsv.Number(aiQueries > 0 ? (double)aiCandidates / aiQueries : double.NaN),
            PerfCsv.Number(segment.Count > 0 ? (double)aiCandidates / segment.Count : double.NaN),
            PerfCsv.Number(specialQueries), PerfCsv.Number(specialCandidates), PerfCsv.Number(specialHits),
            PerfCsv.Number(last.KillsTotal - killsBefore), PerfCsv.Number(last.PlayerHitsTotal - hitsBefore), PerfCsv.Number(last.Score),
            pass.CpuValid ? PerfCsv.Number(PerfCsv.Mean(cpu)) : string.Empty,
            pass.CpuValid ? PerfCsv.Number(PerfCsv.Percentile(cpu, 0.5)) : string.Empty,
            pass.CpuValid ? PerfCsv.Number(PerfCsv.Percentile(cpu, 0.95)) : string.Empty,
            pass.CpuValid ? PerfCsv.Number(PerfCsv.Percentile(cpu, 1d)) : string.Empty,
            pass.CpuValid ? PerfCsv.Number(specialCpu) : string.Empty,
            pass.CpuValid ? "1" : "0", PerfCsv.Escape(pass.CpuNote),
            PerfCsv.Number(avgFrame),
            pass.FrameMetricsValid ? PerfCsv.Number(PerfCsv.Percentile(frameMs, 0.5)) : string.Empty,
            pass.FrameMetricsValid ? PerfCsv.Number(PerfCsv.Percentile(frameMs, 0.95)) : string.Empty,
            pass.FrameMetricsValid && avgFrame > 0d ? PerfCsv.Number(1000d / avgFrame, "0.#") : string.Empty,
            pass.FrameMetricsValid ? "1" : "0",
            pass.RenderCountersValid ? PerfCsv.Number(PerfCsv.Mean(drawCalls), "0.#") : string.Empty,
            pass.RenderCountersValid ? PerfCsv.Number(PerfCsv.Mean(batches), "0.#") : string.Empty,
            pass.RenderCountersValid ? PerfCsv.Number(PerfCsv.Mean(setPass), "0.#") : string.Empty,
            pass.RenderCountersValid ? "1" : "0", PerfCsv.Escape(pass.UnsupportedNote),
            PerfCsv.Optional(shadowInRadius, pass.Verify), PerfCsv.Optional(shadowMissedAi, pass.Verify),
            PerfCsv.Optional(shadowMissedSpecial, pass.Verify),
            pass.Verify && whole ? PerfCsv.Number(pass.ShadowQueries) : string.Empty,
            pass.Verify && whole ? PerfCsv.Number(pass.ShadowMissedKnockback) : string.Empty,
            pass.Verify && whole ? PerfCsv.Number(pass.ShadowMissedMaxDrift, "0.#####") : string.Empty,
            pass.PlayerMoved ? "1" : "0", pass.CameraMoved ? "1" : "0",
        };
    }

    public string FramesCsv(EnemyAbPassResult pass)
    {
        var text = new StringBuilder(FramesHeader, pass.Frames.Count * 96).Append('\n');
        foreach (EnemyAbFrameSample f in pass.Frames)
        {
            text.Append(pass.Name).Append(',').Append(pass.Mode).Append(',')
                .Append(f.Frame).Append(',').Append(f.ActiveEnemies).Append(',')
                .Append(f.AiQueries).Append(',').Append(f.AiCandidates).Append(',')
                .Append(f.SpecialQueries).Append(',').Append(f.SpecialCandidates).Append(',')
                .Append(f.SpecialHits >= 0 ? f.SpecialHits.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(',')
                .Append(f.KillsTotal).Append(',').Append(f.PlayerHitsTotal).Append(',').Append(f.Score).Append(',')
                .Append(pass.CpuValid ? PerfCsv.Number(f.AiCpuMicros) : string.Empty).Append(',')
                .Append(pass.CpuValid ? PerfCsv.Number(f.SpecialCpuMicros) : string.Empty).Append(',')
                .Append(pass.FrameMetricsValid ? PerfCsv.Number(f.FrameMs) : string.Empty).Append(',')
                .Append(PerfCsv.Optional(f.DrawCalls, pass.RenderCountersValid)).Append(',')
                .Append(PerfCsv.Optional(f.Batches, pass.RenderCountersValid)).Append(',')
                .Append(PerfCsv.Optional(f.SetPassCalls, pass.RenderCountersValid)).Append(',')
                .Append(f.PositionHash.ToString("X16")).Append(',')
                .Append(PerfCsv.Number(f.MeanDistanceToPlayer, "0.#####")).Append(',')
                .Append(PerfCsv.Optional(f.ShadowInRadius, pass.Verify)).Append(',')
                .Append(PerfCsv.Optional(f.ShadowMissedAi, pass.Verify)).Append(',')
                .Append(PerfCsv.Optional(f.ShadowMissedSpecial, pass.Verify)).Append('\n');
        }
        return text.ToString();
    }

    // 첫 판을 기준으로 다른 판들과 구간별 위치·판정 결과를 비교한다. 판은 따로 진행되므로 위치가 완전히 같다는 보장은 없다.
    // 위치 해시는 밀리미터 단위라 아주 작은 차이에도 달라진다. 실제 차이의 크기는 평균 거리 차이로 본다.
    public string ComparisonCsv()
    {
        var text = new StringBuilder(ComparisonHeader).Append('\n');
        if (Passes.Count < 2) return text.ToString();
        AppendComparisons(text, 0);
        // 첫 판이 검증 판이면 시간 판끼리의 비교도 남긴다(첫 시간 판 기준). 두 방식을 직접 견주는 행이 여기에 있다.
        if (Passes[0].Verify && Passes.Count > 2)
            AppendComparisons(text, 1);
        return text.ToString();
    }

    private void AppendComparisons(StringBuilder text, int referenceIndex)
    {
        EnemyAbPassResult reference = Passes[referenceIndex];
        for (int p = referenceIndex + 1; p < Passes.Count; p++)
        {
            EnemyAbPassResult other = Passes[p];
            int frames = Math.Min(reference.Frames.Count, other.Frames.Count);
            foreach (EnemyAbSegment segment in Segments(frames))
            {
                int end = segment.First + segment.Count;
                int firstDifference = -1;
                bool enemies = true, specials = true;
                double maxDistanceDifference = 0d;
                for (int i = segment.First; i < end; i++)
                {
                    EnemyAbFrameSample a = reference.Frames[i], b = other.Frames[i];
                    if (firstDifference < 0 && a.PositionHash != b.PositionHash) firstDifference = i;
                    maxDistanceDifference = Math.Max(maxDistanceDifference, Math.Abs(a.MeanDistanceToPlayer - b.MeanDistanceToPlayer));
                    if (a.ActiveEnemies != b.ActiveEnemies) enemies = false;
                    if (a.SpecialHits != b.SpecialHits) specials = false;
                }
                bool kills = false, hits = false, score = false;
                if (segment.Count > 0)
                {
                    EnemyAbFrameSample a = reference.Frames[end - 1], b = other.Frames[end - 1];
                    kills = a.KillsTotal - reference.KillsAtWindowStart == b.KillsTotal - other.KillsAtWindowStart;
                    hits = a.PlayerHitsTotal - reference.PlayerHitsAtWindowStart == b.PlayerHitsTotal - other.PlayerHitsAtWindowStart;
                    score = a.Score == b.Score;
                }
                text.Append(reference.Name).Append(',').Append(reference.Mode).Append(',')
                    .Append(other.Name).Append(',').Append(other.Mode).Append(',')
                    .Append(segment.Name).Append(',').Append(segment.Count).Append(',').Append(firstDifference).Append(',')
                    .Append(firstDifference < 0 && segment.Count > 0 ? 1 : 0).Append(',')
                    .Append(PerfCsv.Number(maxDistanceDifference, "0.#####")).Append(',').Append(enemies ? 1 : 0).Append(',')
                    .Append(specials ? 1 : 0).Append(',').Append(kills ? 1 : 0).Append(',')
                    .Append(hits ? 1 : 0).Append(',').Append(score ? 1 : 0).Append('\n');
            }
        }
    }

    // 결과 파일을 쓴다. 돌려주는 값은 쓴 파일 경로 목록이다.
    public List<string> Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        var utf8 = new UTF8Encoding(false);
        void Save(string name, string content)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, content, utf8);
            written.Add(path);
        }

        var extra = new List<KeyValuePair<string, string>>
        {
            new("measurement_kind", "round (actual round in the game scene)"),
            new("scene", Config.ScenePath),
            new("capture_framerate", PerfCsv.Number(Config.CaptureFramerate)),
            new("target_enemies", PerfCsv.Number(Config.TargetEnemies)),
            new("enemy_cap_in_settings", PerfCsv.Number(EnemyCapInSettings)),
            new("experiment_enemy_cap", PerfCsv.Number(ExperimentEnemyCap)),
            new("experiment_enemy_cap_note", ExperimentEnemyCap > 0
                ? "performance experiment only: more active enemies than the game allows in normal play"
                : string.Empty),
            new("window_frames", PerfCsv.Number(Config.WindowFrames)),
            new("special_frames", string.Join(" ", Config.SpecialFrames ?? Array.Empty<int>())),
            new("unity_random_seed", PerfCsv.Number(Config.Seed)),
            new("succeeded", Succeeded ? "1" : "0"),
            new("error", Error ?? string.Empty),
        };
        string environmentPath = Path.Combine(directory, "round_environment.csv");
        Environment.WriteCsv(environmentPath, extra);
        written.Add(environmentPath);

        Save("round_summary.csv", SummaryCsv());
        Save("round_comparison.csv", ComparisonCsv());
        foreach (EnemyAbPassResult pass in Passes)
            Save("round_frames_" + pass.Name + ".csv", FramesCsv(pass));
        if (Trace != null)
            Save("round_trace.txt", Trace.Serialize());
        return written;
    }
}

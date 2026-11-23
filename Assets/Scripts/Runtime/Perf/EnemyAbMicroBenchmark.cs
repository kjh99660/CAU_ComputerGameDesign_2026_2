using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

// 마이크로벤치마크: 같은 궤적 위에서 "이웃 후보 수집 + 정확한 거리 판정"만 두 방식으로 되풀이해 잰다.
// 게임 루프, 렌더링, 물리, 애니메이션, 적의 상태 변화는 없다. 실제 한 판의 수치가 아니다.
// - 위치는 궤적에서 읽어 프레임마다 그대로 놓는다(두 방식이 완전히 같은 위치를 본다).
// - 거리 판정은 EnemyAgent.CalculateSeparation, EnemyRuntime.ApplySpecial과 같은 식을 쓴다.
// - 판정을 통과한 대상의 집합을 순서와 무관한 합으로 남겨 두 방식의 결과가 같은지 비교한다.
public static class EnemyAbMicroBenchmark
{
    public sealed class Options
    {
        public int Repeats = 21;
        public float CellSize = 4f;
        public float SeparationDistance = 2f;
        public float SpecialRadius = 6f;
        // 궤적의 이 프레임들에서 플레이어 위치를 중심으로 특수 공격 범위 판정을 한 번씩 한다.
        public int[] SpecialFrames = Array.Empty<int>();
    }

    public sealed class ModeResult
    {
        public EnemyCandidateMode Mode;
        public long Queries, Candidates, Accepted;
        public long SpecialQueries, SpecialCandidates, SpecialHits;
        public ulong AcceptedChecksum, SpecialChecksum;
        public bool StableAcrossRepeats = true;
        public readonly List<double> MicrosPerFrame = new();

        public double MedianMicrosPerFrame => PerfCsv.Percentile(MicrosPerFrame, 0.5);
        public double MinMicrosPerFrame => PerfCsv.Percentile(MicrosPerFrame, 0d);
        public double MeanMicrosPerFrame => PerfCsv.Mean(MicrosPerFrame);
    }

    public sealed class Result
    {
        public int EnemyCount, FrameCount, Repeats;
        public string TraceSource;
        public Options Options;
        public ModeResult Grid, BruteForce;
        // 분리 이웃 판정과 특수 공격 판정을 통과한 집합이 두 방식에서 같은가.
        public bool SameSeparationResults, SameSpecialResults;
        public bool Equal => SameSeparationResults && SameSpecialResults;
    }

    public static Result Run(EnemyAbTrace trace, Options options)
    {
        if (trace == null) throw new ArgumentNullException(nameof(trace));
        options ??= new Options();
        int repeats = Mathf.Max(1, options.Repeats);

        var root = new GameObject("EnemyAbMicroBenchmark");
        var agents = new EnemyAgent[trace.EnemyCount];
        var registry = new EnemyRegistry();
        var probe = new EnemyPerfProbe(registry, new EnemySpatialGrid(options.CellSize));
        var buffer = new List<EnemyAgent>(trace.EnemyCount);
        var specialFrames = new HashSet<int>(options.SpecialFrames ?? Array.Empty<int>());
        const int tick = 1;
        try
        {
            for (int i = 0; i < agents.Length; i++)
            {
                var go = new GameObject("MicroEnemy" + i);
                go.transform.SetParent(root.transform, false);
                EnemyAgent agent = go.AddComponent<EnemyAgent>();
                agent.Initialize(null);
                agent.Activate(new Vector3(trace.EnemyX(0, i), 0f, trace.EnemyZ(0, i)), tick);
                registry.Add(agent);
                agents[i] = agent;
            }

            var result = new Result
            {
                EnemyCount = trace.EnemyCount, FrameCount = trace.FrameCount, Repeats = repeats,
                TraceSource = trace.Source, Options = options,
                Grid = new ModeResult { Mode = EnemyCandidateMode.SpatialGrid },
                BruteForce = new ModeResult { Mode = EnemyCandidateMode.BruteForce },
            };

            // 첫 바퀴는 JIT와 캐시를 데우는 용도로 돌리고 기록하지 않는다. 순서 효과를 줄이려고 바퀴마다 순서를 바꾼다.
            for (int repeat = -1; repeat < repeats; repeat++)
            {
                bool gridFirst = repeat % 2 == 0;
                RunOnce(trace, options, gridFirst ? result.Grid : result.BruteForce, agents, probe, buffer, specialFrames, tick, repeat >= 0);
                RunOnce(trace, options, gridFirst ? result.BruteForce : result.Grid, agents, probe, buffer, specialFrames, tick, repeat >= 0);
            }

            result.SameSeparationResults = result.Grid.StableAcrossRepeats && result.BruteForce.StableAcrossRepeats &&
                result.Grid.Accepted == result.BruteForce.Accepted && result.Grid.AcceptedChecksum == result.BruteForce.AcceptedChecksum;
            result.SameSpecialResults = result.Grid.SpecialHits == result.BruteForce.SpecialHits &&
                result.Grid.SpecialChecksum == result.BruteForce.SpecialChecksum;
            return result;
        }
        finally
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void RunOnce(EnemyAbTrace trace, Options options, ModeResult into, EnemyAgent[] agents, EnemyPerfProbe probe,
        List<EnemyAgent> buffer, HashSet<int> specialFrames, int tick, bool record)
    {
        probe.SetMode(into.Mode);
        probe.ResetCounters();
        float separation = options.SeparationDistance;
        float separationSquared = separation * separation;
        float specialRadius = Mathf.Max(0f, options.SpecialRadius);
        long accepted = 0, specialHits = 0, ticks = 0;
        ulong acceptedChecksum = 0, specialChecksum = 0;
        Vector3 sink = Vector3.zero;

        for (int frame = 0; frame < trace.FrameCount; frame++)
        {
            // 위치 놓기는 재지 않는다(두 방식에 똑같이 드는 준비 작업).
            for (int i = 0; i < agents.Length; i++)
                agents[i].transform.position = new Vector3(trace.EnemyX(frame, i), 0f, trace.EnemyZ(frame, i));
            var player = new Vector3(trace.PlayerX(frame), 0f, trace.PlayerZ(frame));
            bool special = specialFrames.Contains(frame);

            long started = Stopwatch.GetTimestamp();
            // 격자 유지 비용도 공간 분할의 비용이다. 전수 방식에서는 Track이 아무 일도 하지 않는다.
            for (int i = 0; i < agents.Length; i++)
                probe.Track(agents[i]);

            for (int i = 0; i < agents.Length; i++)
            {
                EnemyAgent self = agents[i];
                IReadOnlyList<EnemyAgent> candidates = probe.Collect(self.transform.position, separation, buffer);
                Vector3 push = Vector3.zero;
                for (int c = 0; c < candidates.Count; c++)
                {
                    EnemyAgent other = candidates[c];
                    if (other == null || other == self || other.ActiveTick != tick || other.State == EnemyAgentState.Pooled)
                        continue;
                    Vector3 away = self.transform.position - other.transform.position;
                    away.y = 0f;
                    float squared = away.sqrMagnitude;
                    if (squared <= 0.0001f || squared > separationSquared)
                        continue;
                    push += away.normalized * (1f - Mathf.Sqrt(squared) / separation);
                    accepted++;
                    acceptedChecksum += Mix(RuntimeHelpers.GetHashCode(self), RuntimeHelpers.GetHashCode(other), frame);
                }
                sink += push;
            }

            if (special)
            {
                IReadOnlyList<EnemyAgent> candidates = probe.Collect(player, specialRadius, buffer, special: true);
                for (int c = 0; c < candidates.Count; c++)
                {
                    EnemyAgent enemy = candidates[c];
                    if (enemy == null) continue;
                    Vector3 offset = enemy.transform.position - player;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > specialRadius * specialRadius) continue;
                    specialHits++;
                    specialChecksum += Mix(RuntimeHelpers.GetHashCode(enemy), 0, frame);
                }
            }
            ticks += Stopwatch.GetTimestamp() - started;
        }

        EnemyPerfCounters counters = probe.Counters;
        if (into.Queries != 0 && (into.Accepted != accepted || into.AcceptedChecksum != acceptedChecksum ||
                                  into.SpecialHits != specialHits || into.SpecialChecksum != specialChecksum))
            into.StableAcrossRepeats = false;
        into.Queries = counters.AiQueries;
        into.Candidates = counters.AiCandidates;
        into.SpecialQueries = counters.SpecialQueries;
        into.SpecialCandidates = counters.SpecialCandidates;
        into.Accepted = accepted;
        into.AcceptedChecksum = acceptedChecksum;
        into.SpecialHits = specialHits;
        into.SpecialChecksum = specialChecksum;
        if (record)
            into.MicrosPerFrame.Add(EnemyPerfProbe.TicksToMicroseconds(ticks) / trace.FrameCount);
        if (float.IsNaN(sink.x)) throw new InvalidOperationException("Separation produced NaN.");
    }

    // 순서와 무관하게 더할 수 있도록 (자기, 상대, 프레임)을 한 값으로 섞는다.
    private static ulong Mix(int a, int b, int frame)
    {
        unchecked
        {
            ulong value = ((ulong)(uint)a << 32) ^ (uint)b ^ ((ulong)(uint)frame * 0x9E3779B97F4A7C15UL);
            value ^= value >> 33;
            value *= 0xFF51AFD7ED558CCDUL;
            value ^= value >> 33;
            return value;
        }
    }

    public const string CsvHeader =
        "kind,label,environment_class,trace_source,enemies,frames,repeats,mode," +
        "ai_queries,ai_candidates,ai_candidates_per_query,ai_candidates_per_frame,ai_accepted,ai_accepted_checksum," +
        "special_queries,special_candidates,special_hits,special_checksum," +
        "cpu_us_per_frame_median,cpu_us_per_frame_min,cpu_us_per_frame_mean,cpu_ns_per_query_median," +
        "results_equal_between_modes,frame_ms,fps,draw_calls,unsupported_note";

    public static string ToCsv(Result result, PerfEnvironment environment)
    {
        var text = new StringBuilder(CsvHeader).Append('\n');
        foreach (ModeResult mode in new[] { result.Grid, result.BruteForce })
        {
            double perQuery = mode.Queries > 0 ? (double)mode.Candidates / mode.Queries : double.NaN;
            double nsPerQuery = mode.Queries > 0 ? mode.MedianMicrosPerFrame * 1000d * result.FrameCount / mode.Queries : double.NaN;
            text.Append("micro,").Append(PerfCsv.Escape(environment.Label)).Append(',').Append(environment.EnvironmentClass).Append(',')
                .Append(PerfCsv.Escape(result.TraceSource)).Append(',')
                .Append(result.EnemyCount).Append(',').Append(result.FrameCount).Append(',').Append(result.Repeats).Append(',')
                .Append(mode.Mode).Append(',')
                .Append(PerfCsv.Number(mode.Queries)).Append(',').Append(PerfCsv.Number(mode.Candidates)).Append(',')
                .Append(PerfCsv.Number(perQuery)).Append(',')
                .Append(PerfCsv.Number((double)mode.Candidates / result.FrameCount)).Append(',')
                .Append(PerfCsv.Number(mode.Accepted)).Append(',').Append(mode.AcceptedChecksum.ToString("X16")).Append(',')
                .Append(PerfCsv.Number(mode.SpecialQueries)).Append(',').Append(PerfCsv.Number(mode.SpecialCandidates)).Append(',')
                .Append(PerfCsv.Number(mode.SpecialHits)).Append(',').Append(mode.SpecialChecksum.ToString("X16")).Append(',')
                .Append(PerfCsv.Number(mode.MedianMicrosPerFrame)).Append(',').Append(PerfCsv.Number(mode.MinMicrosPerFrame)).Append(',')
                .Append(PerfCsv.Number(mode.MeanMicrosPerFrame)).Append(',').Append(PerfCsv.Number(nsPerQuery, "0.#")).Append(',')
                .Append(result.Equal ? 1 : 0).Append(",,,,")
                .Append(PerfCsv.Escape("microbenchmark: no game loop or rendering; frame time / FPS / draw calls do not apply")).Append('\n');
        }
        return text.ToString();
    }

    public static void WriteCsv(Result result, PerfEnvironment environment, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, ToCsv(result, environment), new UTF8Encoding(false));
    }
}

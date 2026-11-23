using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// 주변 Enemy 후보를 모으는 방법. 성능 비교(A/B)에서 이 한 가지만 바꾼다.
public enum EnemyCandidateMode
{
    // 격자에서 질의 범위와 겹치는 칸의 Enemy만 후보로 삼는다(기본, 출시 구성).
    SpatialGrid,
    // 격자를 쓰지 않고 활성 Enemy 전부를 후보로 삼는다(비교 기준).
    BruteForce
}

// 측정 시작 이후 누적된 수치의 한 시점 값.
public readonly struct EnemyPerfCounters
{
    public readonly long AiQueries, AiCandidates, SpecialQueries, SpecialCandidates;
    public readonly long AiTicks, SpecialTicks;
    public readonly long ShadowQueries, ShadowInRadius, ShadowMissedAi, ShadowMissedSpecial;
    // 놓친 Enemy 가운데 넉백 중(물리로 움직이는 중)이던 수와, 격자에 기록한 뒤 Transform이 옮겨진 거리의 최댓값(m).
    public readonly long ShadowMissedKnockback;
    public readonly float ShadowMissedMaxDrift;

    public EnemyPerfCounters(long aiQueries, long aiCandidates, long specialQueries, long specialCandidates,
        long aiTicks, long specialTicks, long shadowQueries, long shadowInRadius, long shadowMissedAi, long shadowMissedSpecial,
        long shadowMissedKnockback, float shadowMissedMaxDrift)
    {
        ShadowMissedKnockback = shadowMissedKnockback;
        ShadowMissedMaxDrift = shadowMissedMaxDrift;
        AiQueries = aiQueries; AiCandidates = aiCandidates;
        SpecialQueries = specialQueries; SpecialCandidates = specialCandidates;
        AiTicks = aiTicks; SpecialTicks = specialTicks;
        ShadowQueries = shadowQueries; ShadowInRadius = shadowInRadius;
        ShadowMissedAi = shadowMissedAi; ShadowMissedSpecial = shadowMissedSpecial;
    }
}

// EnemyRuntime이 후보를 모을 때 거치는 창구다. 공간 분할을 쓸지 고르고 측정용 수치를 모은다.
// 기본값은 SpatialGrid이고 측정 기능은 모두 꺼져 있어, 평소의 후보 목록·순서·비용은 이전과 같다.
// 후보 수는 "판정 전에 살펴본 Enemy 수"다(격자: 겹치는 칸의 Enemy 수, 전수: 활성 Enemy 수).
public sealed class EnemyPerfProbe
{
    private readonly EnemyRegistry _registry;
    private readonly EnemySpatialGrid _grid;
    private long _aiQueries, _aiCandidates, _specialQueries, _specialCandidates;
    private long _aiTicks, _specialTicks;
    private long _shadowQueries, _shadowInRadius, _shadowMissedAi, _shadowMissedSpecial, _shadowMissedKnockback;
    private float _shadowMissedMaxDrift;
    // 검증을 켠 동안에만 채운다: 격자에 마지막으로 기록할 때의 Transform 위치.
    private Dictionary<EnemyAgent, Vector3> _trackedPositions;

    public EnemyPerfProbe(EnemyRegistry registry, EnemySpatialGrid grid)
    {
        _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        _grid = grid ?? throw new System.ArgumentNullException(nameof(grid));
    }

    public EnemyCandidateMode Mode { get; private set; } = EnemyCandidateMode.SpatialGrid;

    // 켜면 AI 갱신과 특수 공격 판정에 걸린 시간을 Stopwatch로 잰다(프레임당 시각 읽기 두 번).
    public bool MeasureTime { get; set; }

    // 켜면 격자 질의마다 활성 Enemy 전부를 다시 훑어, 반경 안의 Enemy가 후보에서 빠졌는지 센다.
    // 판정 결과가 전수 검사와 같은지 확인하는 용도이며 비용이 크므로 시간 측정과 함께 쓰지 않는다.
    public bool ShadowVerify { get; set; }

    public IReadOnlyList<EnemyAgent> ActiveEnemies => _registry.Active;

    // 성능 실험에서 허용하는 활성 Enemy 수의 최대값. 일반 플레이의 상한은 EnemySettings(100명)가 정한다.
    public const int ExperimentEnemyLimit = 300;

    // 성능 실험용 활성 Enemy 상한. 0이면 꺼져 있고 설정의 상한을 쓴다. 측정기만 켜며 판이 끝나면 끈다.
    public int ExperimentMaximumEnemies { get; private set; }

    public void SetExperimentMaximumEnemies(int count) =>
        ExperimentMaximumEnemies = Mathf.Clamp(count, 0, ExperimentEnemyLimit);

    // 지금 적용되는 활성 Enemy 상한. 실험 값은 설정값보다 클 때만 쓰인다(상한을 낮추지는 않는다).
    public int MaximumEnemies(int configured) => Mathf.Max(configured, ExperimentMaximumEnemies);

    public EnemyPerfCounters Counters => new EnemyPerfCounters(_aiQueries, _aiCandidates, _specialQueries, _specialCandidates,
        _aiTicks, _specialTicks, _shadowQueries, _shadowInRadius, _shadowMissedAi, _shadowMissedSpecial,
        _shadowMissedKnockback, _shadowMissedMaxDrift);

    public static double TicksToMicroseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;

    // 방식을 바꾼다. 격자로 돌아올 때는 지금 위치로 격자를 다시 만든다.
    public void SetMode(EnemyCandidateMode mode)
    {
        if (Mode == mode)
            return;
        Mode = mode;
        _grid.Clear();
        if (mode != EnemyCandidateMode.SpatialGrid)
            return;
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = 0; i < active.Count; i++)
            _grid.AddOrUpdate(active[i]);
    }

    public void ResetCounters()
    {
        _aiQueries = _aiCandidates = _specialQueries = _specialCandidates = 0;
        _aiTicks = _specialTicks = 0;
        _shadowQueries = _shadowInRadius = _shadowMissedAi = _shadowMissedSpecial = _shadowMissedKnockback = 0;
        _shadowMissedMaxDrift = 0f;
    }

    // center에서 radius 안에 있을 수 있는 후보를 돌려준다. 정확한 거리 판정은 호출한 쪽이 한다.
    // 격자 방식은 buffer를 채워 돌려주고, 전수 방식은 활성 목록을 그대로 돌려준다(복사 없음).
    public IReadOnlyList<EnemyAgent> Collect(Vector3 center, float radius, List<EnemyAgent> buffer, bool special = false)
    {
        IReadOnlyList<EnemyAgent> candidates;
        if (Mode == EnemyCandidateMode.BruteForce)
        {
            candidates = _registry.Active;
        }
        else
        {
            _grid.Query(center, radius, buffer);
            candidates = buffer;
            if (ShadowVerify)
                Verify(center, radius, buffer, special);
        }

        if (special)
        {
            _specialQueries++;
            _specialCandidates += candidates.Count;
        }
        else
        {
            _aiQueries++;
            _aiCandidates += candidates.Count;
        }
        return candidates;
    }

    // 새로 나타났거나 움직인 Enemy의 격자 위치를 갱신한다. 전수 방식에서는 격자를 유지하지 않는다.
    public void Track(EnemyAgent enemy)
    {
        if (Mode != EnemyCandidateMode.SpatialGrid)
            return;
        _grid.AddOrUpdate(enemy);
        if (ShadowVerify && enemy != null)
        {
            _trackedPositions ??= new Dictionary<EnemyAgent, Vector3>();
            _trackedPositions[enemy] = enemy.transform.position;
        }
    }

    public void Untrack(EnemyAgent enemy)
    {
        _grid.Remove(enemy);
        _trackedPositions?.Remove(enemy);
    }

    public void Clear() => _grid.Clear();

    public long BeginTiming() => MeasureTime ? Stopwatch.GetTimestamp() : 0L;

    public void EndAiTiming(long started)
    {
        if (MeasureTime)
            _aiTicks += Stopwatch.GetTimestamp() - started;
    }

    public void EndSpecialTiming(long started)
    {
        if (MeasureTime)
            _specialTicks += Stopwatch.GetTimestamp() - started;
    }

    // 격자 후보가 "XZ 거리로 반경 안에 있는 활성 Enemy"를 모두 담고 있는지 전수 비교한다.
    // 빠진 것이 없으면, 같은 거리 판정을 거친 결과는 전수 방식과 같다.
    private void Verify(Vector3 center, float radius, List<EnemyAgent> candidates, bool special)
    {
        float safeRadius = Mathf.Max(0f, radius);
        float radiusSquared = safeRadius * safeRadius;
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        _shadowQueries++;
        for (int i = 0; i < active.Count; i++)
        {
            EnemyAgent enemy = active[i];
            if (enemy == null)
                continue;
            Vector3 offset = enemy.transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude > radiusSquared)
                continue;
            _shadowInRadius++;
            if (candidates.Contains(enemy))
                continue;
            if (special) _shadowMissedSpecial++;
            else _shadowMissedAi++;
            // 놓친 까닭을 가리기 위한 기록: 물리로 움직이는 중이었는지, 격자에 기록한 뒤 Transform이 얼마나 옮겨졌는지.
            if (enemy.State == EnemyAgentState.Knockback) _shadowMissedKnockback++;
            if (_trackedPositions != null && _trackedPositions.TryGetValue(enemy, out Vector3 tracked))
            {
                Vector3 drift = enemy.transform.position - tracked;
                drift.y = 0f;
                _shadowMissedMaxDrift = Mathf.Max(_shadowMissedMaxDrift, drift.magnitude);
            }
        }
    }
}

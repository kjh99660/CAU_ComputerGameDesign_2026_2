using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 후보 수집 창구: 공간 분할과 전수 방식이 같은 판정 결과를 내는지, 후보 수와 방식 전환이 맞는지 검증한다.
public sealed class EnemyPerfProbeTests
{
    private const int Tick = 3;
    private readonly List<GameObject> _objects = new();
    private EnemyRegistry _registry;
    private EnemySpatialGrid _grid;
    private EnemyPerfProbe _probe;
    private readonly List<EnemyAgent> _buffer = new();

    [SetUp]
    public void SetUp()
    {
        _registry = new EnemyRegistry();
        _grid = new EnemySpatialGrid(4f);
        _probe = new EnemyPerfProbe(_registry, _grid);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _objects) Object.DestroyImmediate(go);
        _objects.Clear();
    }

    private EnemyAgent Spawn(Vector3 position)
    {
        var go = new GameObject("ProbeEnemy");
        _objects.Add(go);
        EnemyAgent enemy = go.AddComponent<EnemyAgent>();
        enemy.Initialize(null);
        enemy.Activate(position, Tick);
        _registry.Add(enemy);
        _probe.Track(enemy);
        return enemy;
    }

    // 플랫폼과 무관하게 같은 값을 내는 난수(정수 연산만 쓴다).
    private static float Next(ref uint state, float min, float max)
    {
        state = state * 1664525u + 1013904223u;
        return min + (max - min) * ((state >> 8) / 16777216f);
    }

    private static HashSet<EnemyAgent> Within(IReadOnlyList<EnemyAgent> candidates, Vector3 center, float radius)
    {
        var accepted = new HashSet<EnemyAgent>();
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 offset = candidates[i].transform.position - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radius * radius) accepted.Add(candidates[i]);
        }
        return accepted;
    }

    [Test]
    public void DefaultsToSpatialGridWithMeasurementOff()
    {
        Assert.AreEqual(EnemyCandidateMode.SpatialGrid, _probe.Mode);
        Assert.IsFalse(_probe.MeasureTime);
        Assert.IsFalse(_probe.ShadowVerify);
        Spawn(Vector3.zero);
        // 기본 방식은 이전처럼 넘겨준 buffer를 채워 그대로 돌려준다.
        Assert.AreSame(_buffer, _probe.Collect(Vector3.zero, 2f, _buffer));
        Assert.AreEqual(0, _probe.BeginTiming());
    }

    [Test]
    public void BothModesAcceptExactlyTheSameEnemiesForRandomQueries()
    {
        uint state = 20261123u;
        for (int i = 0; i < 100; i++)
            Spawn(new Vector3(Next(ref state, -30f, 30f), Next(ref state, -1f, 1f), Next(ref state, -30f, 30f)));
        // 칸 경계와 음수 좌표.
        Spawn(new Vector3(-0.0001f, 0f, 4f));
        Spawn(new Vector3(-4f, 0f, -8f));
        Spawn(new Vector3(8f, 0f, 0f));

        for (int query = 0; query < 300; query++)
        {
            var center = new Vector3(Next(ref state, -32f, 32f), 0f, Next(ref state, -32f, 32f));
            float radius = query % 3 == 0 ? 6f : query % 3 == 1 ? 2f : Next(ref state, 0f, 9f);

            _probe.SetMode(EnemyCandidateMode.SpatialGrid);
            IReadOnlyList<EnemyAgent> gridCandidates = _probe.Collect(center, radius, _buffer);
            int gridCount = gridCandidates.Count;
            HashSet<EnemyAgent> viaGrid = Within(gridCandidates, center, radius);

            _probe.SetMode(EnemyCandidateMode.BruteForce);
            IReadOnlyList<EnemyAgent> allCandidates = _probe.Collect(center, radius, _buffer);
            Assert.AreEqual(_registry.Count, allCandidates.Count, "Brute force looks at every active enemy.");
            HashSet<EnemyAgent> viaAll = Within(allCandidates, center, radius);

            Assert.LessOrEqual(gridCount, allCandidates.Count);
            Assert.IsTrue(viaGrid.SetEquals(viaAll), $"Query {query} at {center} r={radius}: grid {viaGrid.Count} vs all {viaAll.Count}.");
        }
    }

    [Test]
    public void CountsQueriesAndCandidatesSeparatelyForAiAndSpecial()
    {
        Spawn(new Vector3(0f, 0f, 0f));
        Spawn(new Vector3(1f, 0f, 1f));
        Spawn(new Vector3(40f, 0f, 40f));

        _probe.Collect(Vector3.zero, 2f, _buffer);
        _probe.Collect(Vector3.zero, 2f, _buffer, special: true);
        EnemyPerfCounters grid = _probe.Counters;
        Assert.AreEqual(1, grid.AiQueries);
        Assert.AreEqual(1, grid.SpecialQueries);
        Assert.AreEqual(2, grid.AiCandidates, "Only the two enemies in nearby cells are candidates.");
        Assert.AreEqual(2, grid.SpecialCandidates);

        _probe.ResetCounters();
        _probe.SetMode(EnemyCandidateMode.BruteForce);
        _probe.Collect(Vector3.zero, 2f, _buffer);
        _probe.Collect(Vector3.zero, 2f, _buffer);
        EnemyPerfCounters all = _probe.Counters;
        Assert.AreEqual(2, all.AiQueries);
        Assert.AreEqual(6, all.AiCandidates);
        Assert.AreEqual(0, all.SpecialQueries);
    }

    [Test]
    public void SwitchingBackToTheGridRebuildsItFromCurrentPositions()
    {
        EnemyAgent mover = Spawn(new Vector3(0f, 0f, 0f));
        Spawn(new Vector3(1f, 0f, 0f));

        _probe.SetMode(EnemyCandidateMode.BruteForce);
        mover.transform.position = new Vector3(50f, 0f, 50f);
        _probe.Track(mover); // 전수 방식에서는 격자를 유지하지 않는다.

        _probe.SetMode(EnemyCandidateMode.SpatialGrid);
        Assert.IsFalse(Within(_probe.Collect(Vector3.zero, 2f, _buffer), Vector3.zero, 2f).Contains(mover));
        var far = new Vector3(50f, 0f, 50f);
        Assert.IsTrue(Within(_probe.Collect(far, 2f, _buffer), far, 2f).Contains(mover));
    }

    [Test]
    public void ShadowVerifyReportsOnlyEnemiesTheGridMissed()
    {
        EnemyAgent a = Spawn(new Vector3(0f, 0f, 0f));
        EnemyAgent b = Spawn(new Vector3(1f, 0f, 0f));
        Spawn(new Vector3(30f, 0f, 30f));
        _probe.ShadowVerify = true;

        _probe.Collect(a.transform.position, 2f, _buffer);
        EnemyPerfCounters clean = _probe.Counters;
        Assert.AreEqual(1, clean.ShadowQueries);
        Assert.AreEqual(2, clean.ShadowInRadius, "The query owner and its neighbour are within the radius.");
        Assert.AreEqual(0, clean.ShadowMissedAi);

        // 격자를 갱신하지 않은 채 Enemy가 멀리 있는 질의 지점 근처로 옮겨지면 격자는 그 Enemy를 놓친다.
        b.transform.position = new Vector3(30.5f, 0f, 30f);
        var center = new Vector3(30f, 0f, 30f);
        _probe.Collect(center, 2f, _buffer, special: true);
        Assert.AreEqual(1, _probe.Counters.ShadowMissedSpecial);
        Assert.AreEqual(0, _probe.Counters.ShadowMissedAi);

        _probe.Track(b);
        _probe.ResetCounters();
        _probe.Collect(center, 2f, _buffer, special: true);
        Assert.AreEqual(0, _probe.Counters.ShadowMissedSpecial);
    }

    [Test]
    public void TimingIsRecordedOnlyWhenEnabled()
    {
        long idle = _probe.BeginTiming();
        _probe.EndAiTiming(idle);
        Assert.AreEqual(0, _probe.Counters.AiTicks);

        _probe.MeasureTime = true;
        long started = _probe.BeginTiming();
        Assert.Greater(started, 0);
        for (int i = 0; i < 10000; i++) _probe.Collect(Vector3.zero, 2f, _buffer);
        _probe.EndAiTiming(started);
        _probe.EndSpecialTiming(_probe.BeginTiming());
        Assert.Greater(_probe.Counters.AiTicks, 0);
        Assert.GreaterOrEqual(_probe.Counters.SpecialTicks, 0);
        Assert.Greater(EnemyPerfProbe.TicksToMicroseconds(_probe.Counters.AiTicks), 0d);
    }
}

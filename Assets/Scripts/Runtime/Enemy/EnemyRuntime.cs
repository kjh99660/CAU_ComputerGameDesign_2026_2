using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Enemy Prefab, Pool, Registry, Grid와 Round 생명주기를 소유하는 Unity Adapter다.
[DisallowMultipleComponent]
public sealed class EnemyRuntime : MonoBehaviour, IRoundInitializable, IRoundResettable
{
    [SerializeField] private EnemyAgent enemyPrefab;
    [SerializeField] private EnemySettings settings;
    [SerializeField] private Transform target;

    private readonly List<EnemyAgent> _candidates = new();
    private EnemyRegistry _registry;
    private EnemySpatialGrid _grid;
    private EnemyPool _pool;
    private PhysicsPauseManager _physics;
    private System.Random _random;
    private Transform _poolRoot;
    private GameState _state = GameState.Initializing;
    private int _tick;
    private float _spawnElapsed;
    private bool _stopRequested;
    private bool _configured;

    public EnemySettings Settings => settings;
    public int ActiveEnemyCount => _registry?.Count ?? 0;

    public void Configure(PhysicsPauseManager physics)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        EnsureInfrastructure();
    }

    public EnemyLayer CreateLayer()
    {
        if (!_configured)
            throw new InvalidOperationException("EnemyRuntime must be configured before its layer is created.");
        return new EnemyLayer(this);
    }

    public IEnumerator InitializeRound(int tick)
    {
        EnsureInfrastructure();
        PrepareRound(tick);
        yield break;
    }

    public void StopProducing() => _stopRequested = true;

    public IEnumerator ResetRound(int tick)
    {
        ReturnAllActive();
        _physics.ClearRound(tick);
        PrepareRound(tick);
        yield break;
    }

    public void ApplyState(GameState state, int tick)
    {
        if (tick < _tick)
            return;

        _state = state;
        _tick = tick;
        if (state == GameState.Ready)
        {
            _stopRequested = false;
            SetAllIdle(tick);
        }
    }

    public void UpdateSpawning(float deltaTime, int tick)
    {
        if (_stopRequested || _state != GameState.Battle || tick != _tick || _registry.Count >= settings.MaximumEnemyCount)
            return;

        _spawnElapsed += deltaTime;
        while (_spawnElapsed >= settings.SpawnInterval && _registry.Count < settings.MaximumEnemyCount)
        {
            _spawnElapsed -= settings.SpawnInterval;
            int count = Mathf.Min(settings.SpawnCountPerWave, settings.MaximumEnemyCount - _registry.Count);
            for (int i = 0; i < count; i++)
                SpawnOne(tick);
        }
    }

    public void UpdateAI(float deltaTime, int tick)
    {
        if (_stopRequested || _state != GameState.Battle || tick != _tick)
            return;

        IReadOnlyList<EnemyAgent> active = _registry.Active;
        Vector3 targetPosition = TargetPosition;
        for (int i = 0; i < active.Count; i++)
        {
            EnemyAgent enemy = active[i];
            if (enemy == null || enemy.ActiveTick != tick)
                continue;

            _grid.Query(enemy.transform.position, settings.SeparationDistance, _candidates);
            enemy.RunAI(targetPosition, _candidates, settings, deltaTime, tick);
            _grid.AddOrUpdate(enemy);
        }
    }

    public void UpdateActions(float deltaTime, int tick)
    {
        if (tick != _tick)
            return;

        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            EnemyAgent enemy = active[i];
            if (enemy == null)
                continue;
            enemy.RunAction(settings, deltaTime, tick);
            if (enemy.WantsPoolReturn)
                Release(enemy);
        }
    }

    public bool TryApplyHit(EnemyAgent enemy, Vector3 direction, float forceMultiplier, int tick)
    {
        if (enemy == null || tick != _tick || (_state != GameState.Battle && _state != GameState.Finishing))
            return false;

        Vector3 horizontal = direction;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude < 0.0001f)
            horizontal = enemy.transform.position - TargetPosition;
        horizontal.Normalize();
        Vector3 impulse = horizontal * (settings.NormalKnockbackForce * Mathf.Max(0f, forceMultiplier));
        impulse.y = settings.KnockbackUpwardForce * Mathf.Max(0f, forceMultiplier);
        return enemy.ApplyHit(impulse, settings, tick);
    }

    private Vector3 TargetPosition => target != null ? target.position : transform.position;

    private void EnsureInfrastructure()
    {
        if (_configured)
            return;
        if (enemyPrefab == null)
            throw new InvalidOperationException("EnemyRuntime requires an EnemyAgent prefab.");
        if (settings == null)
            throw new InvalidOperationException("EnemyRuntime requires EnemySettings.");
        if (_physics == null)
            throw new InvalidOperationException("EnemyRuntime requires PhysicsPauseManager configuration.");

        var rootObject = new GameObject("EnemyPool");
        _poolRoot = rootObject.transform;
        _poolRoot.SetParent(transform, false);
        _registry = new EnemyRegistry();
        _grid = new EnemySpatialGrid(settings.SpatialCellSize);
        _pool = new EnemyPool(enemyPrefab, this, _poolRoot);
        _configured = true;
    }

    private void PrepareRound(int tick)
    {
        _tick = tick;
        _spawnElapsed = 0f;
        _stopRequested = false;
        _random = new System.Random(unchecked(7919 + tick * 104729));
        int initialCount = Mathf.Min(settings.InitialEnemyCount, settings.MaximumEnemyCount);
        for (int i = 0; i < initialCount; i++)
            SpawnOne(tick);
    }

    private void SpawnOne(int tick)
    {
        if (_registry.Count >= settings.MaximumEnemyCount)
            return;

        double angle = _random.NextDouble() * Math.PI * 2d;
        float distance = Mathf.Lerp(settings.MinimumSpawnDistance, settings.MaximumSpawnDistance,
            (float)_random.NextDouble());
        Vector3 center = TargetPosition;
        Vector3 position = center + new Vector3(
            Mathf.Cos((float)angle) * distance,
            settings.SpawnHeight,
            Mathf.Sin((float)angle) * distance);

        EnemyAgent enemy = _pool.Get();
        enemy.Activate(position, tick);
        _registry.Add(enemy);
        _grid.AddOrUpdate(enemy);
        _physics.Register(enemy.Body, tick);
    }

    private void Release(EnemyAgent enemy)
    {
        _physics.Unregister(enemy.Body);
        _grid.Remove(enemy);
        _registry.Remove(enemy);
        _pool.Release(enemy);
    }

    private void ReturnAllActive()
    {
        if (_registry == null)
            return;
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = active.Count - 1; i >= 0; i--)
            Release(active[i]);
        _grid.Clear();
    }

    private void SetAllIdle(int tick)
    {
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = 0; i < active.Count; i++)
            active[i].SetIdle(tick);
    }
}

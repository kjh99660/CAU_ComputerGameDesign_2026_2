using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Enemy Prefab, Pool, Registry, Grid와 Round 생명주기를 소유하는 Unity Adapter다.
// Spawning in Battle follows the spawn phases of EnemySettings by battle time (a flat schedule without phases), and with
// a spawn warning time each new enemy is first announced by a neon ring on the floor where it will appear.
[DisallowMultipleComponent]
public sealed class EnemyRuntime : MonoBehaviour, IRoundInitializable, IRoundResettable
{
    // The round the game starts with (GameStateManager's first Tick). Its seeds are every round's seeds when
    // EnemySettings.SameLayoutEveryRound is on.
    public const int FirstRoundTick = 1;

    [SerializeField] private EnemyAgent enemyPrefab;
    [SerializeField] private EnemySettings settings;
    [SerializeField] private Transform target;
    [Tooltip("Optional. Set it in a level with floors above each other; leave it empty in a flat arena.")]
    [SerializeField] private EnemyNavVolume navigation;

    private readonly EnemyPendingSpawns _pending = new EnemyPendingSpawns(32);
    private EnemySpawnWarnings _warnings;      // made on the first announcement
    private readonly List<EnemyAgent> _candidates = new();
    // Enemies attacking right now, for the limit on simultaneous attackers (EnemySettings.MaximumAttackers).
    private readonly List<EnemyAgent> _attackers = new(8);
    private readonly List<EnemyAgent> _specialCandidates = new();
    private EnemyRegistry _registry;
    private EnemySpatialGrid _grid;
    private EnemyPerfProbe _probe;
    private EnemyPool _pool;
    private PhysicsPauseManager _physics;
    private System.Random _random;
    private System.Random _variationRandom;   // separate stream, so per-enemy variety does not shift spawn positions
    private int _spawnObstacleMask;
    private EnemyNavField _nav;               // null in a flat level
    private bool _navigationMeasured;
    private float _pathAge;
    private Transform _poolRoot;
    private GameState _state = GameState.Initializing;
    private int _tick;
    private int _seedTick;                    // the round whose seeds this round's layout uses
    private float _spawnElapsed;
    private float _battleSeconds;
    private float _roundSpawnY;
    private bool _stopRequested;
    private bool _configured;

    public EnemySettings Settings => settings;

    // Time spent in Battle this round, as the round timer counts it. It holds in Pause and starts over on Reset; the
    // spawn phases are looked up by it.
    public float BattleSeconds => _battleSeconds;
    // The wave rule in effect now: the spawn phase for the battle time, or the flat schedule.
    public EnemySpawnPhase CurrentSpawnPhase => UsesSpawnPhases ? settings.SpawnPhaseAt(_battleSeconds) : settings.FlatSpawnPhase;
    // The most enemies spawning may bring onto the field now, counting the ones a warning ring has announced: the current
    // phase's cap, never above the global maximum. This is the one place where the phase cap meets the global cap
    // (MaximumEnemies, which a performance experiment may raise), so every other cap belongs here too.
    public int CurrentSpawnCap => UsesSpawnPhases ? Mathf.Min(CurrentSpawnPhase.Cap, MaximumEnemies) : MaximumEnemies;
    // A/B measurement rounds (EnemyAbRoundRunner: the perf probe times, verifies or holds an experiment cap) fill the
    // arena by the flat schedule without warning rings, so their fill timing stays comparable with every earlier result
    // and with EnemyAbRoundConfig.EstimateFillFrames.
    private bool MeasurementRound => _probe != null && (_probe.MeasureTime || _probe.ShadowVerify || _probe.ExperimentMaximumEnemies > 0);
    private bool UsesSpawnPhases => settings.HasSpawnPhases && !MeasurementRound;
    private bool UsesSpawnWarnings => settings.SpawnWarningSeconds > 0f && !MeasurementRound;
    // Enemies announced by a warning ring that have not appeared yet. Read by tests and tuning tools.
    public int PendingSpawnCount => _pending.Count;
    public Vector3 PendingSpawnPosition(int index) => _pending[index].Position;
    public float PendingSpawnProgress(int index) => _pending[index].Progress;
    public bool PendingSpawnShowsRing(int index) => _warnings != null && _warnings.IsShown(_pending[index].Marker);
    public int ShownSpawnWarnings => _warnings?.ShownCount ?? 0;
    // The ring pool, for tests and captures. Null until the first warning.
    public EnemySpawnWarnings SpawnWarnings => _warnings;
    // A warning ring went up where an enemy will appear (position, tick).
    public event Action<Vector3, int> SpawnAnnounced;
    // An enemy began an attack: its wind-up (the warning colour) starts now. Position and tick.
    public event Action<Vector3, int> AttackWindupStarted;
    // An enemy's wind-up ended in its strike, whether or not the strike reaches the player. Position and tick.
    public event Action<Vector3, int> AttackSwung;

    // Turns to attack: only a few enemies swing at the player at once, the rest wait (and circle, when lively).
    public int ActiveAttackers => _attackers.Count;
    public bool AttackSlotOpen => settings == null || settings.MaximumAttackers <= 0 || _attackers.Count < settings.MaximumAttackers;

    public bool TryTakeAttackSlot(EnemyAgent enemy)
    {
        if (enemy == null) return false;
        if (_attackers.Contains(enemy)) return true;
        if (!AttackSlotOpen) return false;
        _attackers.Add(enemy);
        return true;
    }

    public void ReleaseAttackSlot(EnemyAgent enemy) => _attackers.Remove(enemy);
    public int ActiveEnemyCount => _registry?.Count ?? 0;
    // 성능 비교용 창구(공간 분할 켜기·끄기, 후보 수, 시간). 구성 전에는 null이다. 기본은 공간 분할이고 측정은 꺼져 있다.
    public EnemyPerfProbe Perf => _probe;
    // 활성 Enemy 상한. 일반 플레이에서는 설정값이고, 성능 실험 중에만 측정기가 더 큰 값을 건다.
    private int MaximumEnemies => _probe != null ? _probe.MaximumEnemies(settings.MaximumEnemyCount) : settings.MaximumEnemyCount;
    public Transform TargetTransform => target;
    // The walkable cells of a level with floors. Null in a flat level and before the first round is prepared.
    public EnemyNavField Navigation => _nav;
    public event Action<int, bool> HitAccepted;
    public event Action<Vector3, Vector3, int> HitPresented;
    public event Action<int> PlayerHit;
    // The enemy an accepted hit landed on, for effects that need the body itself (hit flash, breaking apart).
    // Raised right after HitPresented with the same direction and tick.
    public event Action<EnemyAgent, Vector3, int> EnemyStruck;
    // An enemy whose death has played out, just before it goes back to the pool (so its last pose can be kept).
    // Not raised when a Reset clears the round.
    public event Action<EnemyAgent> EnemyDied;
    // Where the enemy whose strike just reached the player stands. Raised right before PlayerHit, so feedback can
    // push the view away from the blow; whether the round accepts the hit is decided by PlayerHit's listeners.
    public event Action<Vector3, int> PlayerStruckFrom;

    public void SetTarget(Transform value)
    {
        target = value != null ? value : throw new ArgumentNullException(nameof(value));
    }

    // Must be called before the first round is prepared.
    public void SetNavigation(EnemyNavVolume value)
    {
        navigation = value;
        _navigationMeasured = false;
        _nav = null;
    }

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

    // Also cancels the announced spawns: when the timer runs out or a Reset begins, no ring is left on the floor.
    public void StopProducing()
    {
        _stopRequested = true;
        CancelPendingSpawns();
    }

    public IEnumerator ResetRound(int tick)
    {
        CancelPendingSpawns();
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
        SetAnimationPausedForAll(state != GameState.Ready &&
                                 state != GameState.Battle &&
                                 state != GameState.Finishing);
        if (state == GameState.Ready)
        {
            _stopRequested = false;
            SetAllIdle(tick);
        }
        else if (state == GameState.Finishing)
        {
            // AI stops issuing moves here; enemies settle instead of walking on the spot through the slow motion.
            HaltAll(tick);
        }
        // Announced spawns belong to Battle: Pause holds them with their rings, every other state cancels them.
        if (state != GameState.Battle && state != GameState.Paused)
            CancelPendingSpawns();
    }

    public void UpdateSpawning(float deltaTime, int tick) => UpdateSpawning(deltaTime, deltaTime, tick);

    // deltaTime: game time, which paces the waves and the warning rings. battleDelta: the round timer's step (real time
    // while a hit stop slows the game), which moves the battle time the spawn phases are looked up by.
    // Without phases and warnings this is the first prototype's flat schedule, step for step.
    public void UpdateSpawning(float deltaTime, float battleDelta, int tick)
    {
        if (_stopRequested || _state != GameState.Battle || tick != _tick)
            return;

        _battleSeconds += Mathf.Max(0f, battleDelta);
        AdvancePendingSpawns(deltaTime, tick);
        EnemySpawnPhase phase = CurrentSpawnPhase;
        int cap = CurrentSpawnCap;
        if (_registry.Count + _pending.Count >= cap)
            return;

        _spawnElapsed += deltaTime;
        while (_spawnElapsed >= phase.Interval && _registry.Count + _pending.Count < cap)
        {
            _spawnElapsed -= phase.Interval;
            int count = Mathf.Min(phase.PerWave, cap - _registry.Count - _pending.Count);
            for (int i = 0; i < count; i++)
                SpawnOrAnnounce(tick);
        }
    }

    public void UpdateAI(float deltaTime, int tick)
    {
        if (_stopRequested || _state != GameState.Battle || tick != _tick)
            return;

        long started = _probe.BeginTiming();
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        Vector3 targetPosition = TargetPosition;
        if (_nav != null)
        {
            // One path pass serves every enemy, and only when the player has moved to another cell.
            _pathAge += deltaTime;
            if (_pathAge >= settings.PathRefreshSeconds)
            {
                _pathAge = 0f;
                _nav.SetGoal(targetPosition);
            }
        }
        for (int i = 0; i < active.Count; i++)
        {
            EnemyAgent enemy = active[i];
            if (enemy == null || enemy.ActiveTick != tick)
                continue;

            IReadOnlyList<EnemyAgent> neighbours = _probe.Collect(enemy.transform.position, settings.SeparationDistance, _candidates);
            bool attacking = enemy.State == EnemyAgentState.Attack;
            enemy.RunAI(targetPosition, neighbours, settings, deltaTime, tick);
            if (!attacking && enemy.State == EnemyAgentState.Attack)
                AttackWindupStarted?.Invoke(enemy.transform.position, tick);
            enemy.YieldTo(targetPosition, settings, deltaTime, tick);
            _probe.Track(enemy);
        }
        _probe.EndAiTiming(started);
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
            bool windingUp = enemy.IsWindingUp;
            bool struck = enemy.TryAttackHit(TargetPosition, settings, deltaTime, tick);
            if (windingUp && !enemy.IsWindingUp)
                AttackSwung?.Invoke(enemy.transform.position, tick);
            if (struck)
            {
                PlayerStruckFrom?.Invoke(enemy.transform.position, tick);
                PlayerHit?.Invoke(tick);
            }
            enemy.RunAction(settings, deltaTime, tick);
            if (enemy.WantsPoolReturn)
            {
                EnemyDied?.Invoke(enemy);
                Release(enemy);
            }
        }
    }

    public bool TryApplyHit(EnemyAgent enemy, Vector3 direction, float forceMultiplier, int tick, bool chargeGauge = true)
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
        bool accepted = enemy.ApplyHit(impulse, settings, tick);
        if (accepted)
        {
            HitAccepted?.Invoke(tick, chargeGauge);
            HitPresented?.Invoke(enemy.transform.position, horizontal, tick);
            EnemyStruck?.Invoke(enemy, horizontal, tick);
        }
        return accepted;
    }

    public int ApplySpecial(Vector3 center, float radius, float forceMultiplier, int tick, bool chargeGauge)
    {
        if (_state != GameState.Battle || _stopRequested || tick != _tick || _grid == null) return 0;
        long started = _probe.BeginTiming();
        float safeRadius = Mathf.Max(0f, radius);
        IReadOnlyList<EnemyAgent> candidates = _probe.Collect(center, safeRadius, _specialCandidates, special: true);
        int hits = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            EnemyAgent enemy = candidates[i];
            if (enemy == null) continue;
            Vector3 offset = enemy.transform.position - center;
            if (_nav != null && Mathf.Abs(offset.y) > settings.VerticalReach)
                continue;   // another floor
            offset.y = 0f;
            if (offset.sqrMagnitude <= safeRadius * safeRadius && TryApplyHit(enemy, offset, forceMultiplier, tick, chargeGauge))
                hits++;
        }
        _specialCandidates.Clear();
        _probe.EndSpecialTiming(started);
        return hits;
    }

    private Vector3 TargetPosition => target != null
        ? target.position
        : throw new InvalidOperationException("EnemyRuntime requires a Player target.");

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
        if (target == null)
            throw new InvalidOperationException("EnemyRuntime requires a Player target before configuration.");

        var rootObject = new GameObject("EnemyPool");
        _poolRoot = rootObject.transform;
        _poolRoot.SetParent(transform, false);
        _registry = new EnemyRegistry();
        _grid = new EnemySpatialGrid(settings.SpatialCellSize);
        _probe = new EnemyPerfProbe(_registry, _grid);
        _pool = new EnemyPool(enemyPrefab, this, _poolRoot);
        // Spawn checks look for level geometry only: not other enemies, not the player, not ray-ignored helpers.
        _spawnObstacleMask = ~((1 << enemyPrefab.gameObject.layer) | (1 << target.gameObject.layer) | (1 << 2));
        _configured = true;
    }

    private void PrepareRound(int tick)
    {
        _tick = tick;
        _spawnElapsed = 0f;
        _battleSeconds = 0f;
        _stopRequested = false;
        // Each round's own seeds, or the first round's for all of them (EnemySettings.SameLayoutEveryRound).
        _seedTick = settings.SameLayoutEveryRound ? FirstRoundTick : tick;
        _random = new System.Random(unchecked(7919 + _seedTick * 104729));
        _variationRandom = new System.Random(unchecked(104729 + _seedTick * 7919));
        // Movement AI is planar and enemies stay kinematic until hit. Inherit only
        // the player's restored starting height, never a later jump/step elevation.
        _roundSpawnY = TargetPosition.y + settings.SpawnHeight;
        MeasureNavigation();
        if (_nav != null)
        {
            _nav.ClearGoal();
            _nav.SetGoal(TargetPosition);
            _pathAge = 0f;
        }
        int initialCount = Mathf.Min(settings.InitialEnemyCount, settings.MaximumEnemyCount);
        for (int i = 0; i < initialCount; i++)
            SpawnOne(tick);
    }

    private void SpawnOne(int tick)
    {
        if (_registry.Count >= MaximumEnemies)
            return;

        Vector3 center = TargetPosition;
        Vector3 position = _nav != null ? SampleSpawnOnCells(center) : SampleSpawnPosition(center);
        Place(position, (float)_variationRandom.NextDouble(), center, tick);
    }

    private void Place(Vector3 position, float variation, Vector3 faceTo, int tick)
    {
        EnemyAgent enemy = _pool.Get();
        enemy.Activate(position, tick, variation, _seedTick);
        enemy.UseNavigation(_nav);
        enemy.Face(faceTo);
        _registry.Add(enemy);
        _probe.Track(enemy);
        _physics.Register(enemy.Body, tick);
    }

    // A Battle spawn: at once, or announced by a warning ring and placed when its warning time is over. The position and
    // the variety are drawn when the wave is due either way, so both ways draw the same random numbers in the same order.
    private void SpawnOrAnnounce(int tick)
    {
        if (!UsesSpawnWarnings)
        {
            SpawnOne(tick);
            return;
        }
        if (_registry.Count + _pending.Count >= MaximumEnemies)
            return;

        Vector3 center = TargetPosition;
        Vector3 position = _nav != null ? SampleSpawnOnCells(center) : SampleSpawnPosition(center);
        float variation = (float)_variationRandom.NextDouble();
        _warnings ??= new EnemySpawnWarnings(transform, WarningMaterial());
        // The ring lies on the floor the enemy will stand on (spawn points are Spawn Height above it).
        int marker = _warnings.Show(position - Vector3.up * settings.SpawnHeight, settings.SpawnWarningRadius,
            settings.SpawnWarningColor, settings.SpawnWarningHotColor);
        _pending.Add(position, variation, settings.SpawnWarningSeconds, tick, marker);
        SpawnAnnounced?.Invoke(position, tick);
    }

    // Counts the announced spawns down (game time, so Pause holds them) and places the ones whose time is over, at the
    // spot their ring marked, facing the player.
    private void AdvancePendingSpawns(float deltaTime, int tick)
    {
        if (_pending.Count == 0)
            return;
        _pending.Advance(deltaTime);
        for (int i = 0; i < _pending.Count; i++)
        {
            EnemyPendingSpawn pending = _pending[i];
            _warnings?.Update(pending.Marker, pending.Progress, pending.Elapsed);
        }
        while (_pending.TryTakeDue(tick, out EnemyPendingSpawn due))
        {
            _warnings?.Hide(due.Marker);
            if (due.Tick == tick && _registry.Count < MaximumEnemies)
                Place(due.Position, due.Variation, TargetPosition, tick);
        }
    }

    private void CancelPendingSpawns()
    {
        while (_pending.TryTakeFirst(out EnemyPendingSpawn cancelled))
            _warnings?.Hide(cancelled.Marker);
        _warnings?.HideAll();
    }

    private static Material WarningMaterial()
    {
        var vfx = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        return vfx != null ? vfx.groundIndicatorMaterial : null;
    }

    private void OnDestroy()
    {
        _warnings?.Dispose();
        _warnings = null;
    }

    // Draws a point on the spawn ring. Enemies move in a straight line and ignore walls, so a point with level
    // geometry between it and the player (outside the arena, behind a building) is drawn again.
    private Vector3 SampleSpawnPosition(Vector3 center)
    {
        int draws = 1 + Mathf.Max(0, settings.SpawnVisibilityAttempts);
        Vector3 position = center;
        for (int draw = 0; draw < draws; draw++)
        {
            double angle = _random.NextDouble() * Math.PI * 2d;
            float distance = Mathf.Lerp(settings.MinimumSpawnDistance, settings.MaximumSpawnDistance,
                (float)_random.NextDouble());
            position = new Vector3(
                center.x + Mathf.Cos((float)angle) * distance,
                _roundSpawnY,
                center.z + Mathf.Sin((float)angle) * distance);
            if (draw == draws - 1 || HasClearPath(center, position))
                break;
        }
        return position;
    }

    // The level is measured once, when the first round is prepared: by then every collider of the scene exists.
    private void MeasureNavigation()
    {
        if (_navigationMeasured)
            return;
        _navigationMeasured = true;
        if (navigation == null)
            return;
        _nav = navigation.Bake(TargetPosition);
        if (_nav.CellCount == 0)
        {
            Debug.LogError("EnemyNavVolume found nowhere to stand around the player. Enemies fall back to straight-line movement.", navigation);
            _nav = null;
        }
    }

    // Level with floors: the same ring around the player, but only where an enemy can stand (on any floor) and
    // from where the walk to the player is not absurdly long. A spot the player cannot see is preferred, so a
    // wave walks in from around a corner instead of appearing in view.
    private Vector3 SampleSpawnOnCells(Vector3 center)
    {
        const float eyeHeight = 1.5f, chestHeight = 1f;
        int draws = 1 + Mathf.Max(0, settings.SpawnVisibilityAttempts);
        float longestWalk = settings.MaximumSpawnDistance * settings.SpawnDetourLimit;
        int chosen = EnemyNavField.None;
        int shortestWalk = EnemyNavField.None;   // used when every drawn spot is further to walk than wanted
        Vector3 point = center;
        for (int draw = 0; draw < draws; draw++)
        {
            double angle = _random.NextDouble() * Math.PI * 2d;
            float distance = Mathf.Lerp(settings.MinimumSpawnDistance, settings.MaximumSpawnDistance,
                (float)_random.NextDouble());
            float floor = (float)_random.NextDouble();
            point = new Vector3(center.x + Mathf.Cos((float)angle) * distance, center.y,
                center.z + Mathf.Sin((float)angle) * distance);
            int cell = _nav.CellAt(point.x, point.z, floor);
            if (cell == EnemyNavField.None)
                continue;
            float walk = _nav.PathDistance(cell);
            if (walk > longestWalk)
            {
                if (!float.IsPositiveInfinity(walk) &&
                    (shortestWalk == EnemyNavField.None || walk < _nav.PathDistance(shortestWalk)))
                    shortestWalk = cell;
                continue;
            }
            if (chosen == EnemyNavField.None)
                chosen = cell;
            if (Physics.Linecast(center + Vector3.up * eyeHeight, _nav.Position(cell) + Vector3.up * chestHeight,
                    _spawnObstacleMask, QueryTriggerInteraction.Ignore))
            {
                chosen = cell;
                break;
            }
        }

        // The player is somewhere hard to reach (a far roof): keep the spawn distance and accept the long walk.
        if (chosen == EnemyNavField.None)
            chosen = shortestWalk;
        if (chosen == EnemyNavField.None)
        {
            // Nothing drawn was ground at all (the player stands at the rim of the level and most of the ring is
            // outside it). Go round the ring once and take the first ground found.
            const int steps = 24;
            double start = _random.NextDouble() * Math.PI * 2d;
            float middle = (settings.MinimumSpawnDistance + settings.MaximumSpawnDistance) * 0.5f;
            for (int step = 0; step < steps && chosen == EnemyNavField.None; step++)
            {
                float angle = (float)(start + step * Math.PI * 2d / steps);
                point = new Vector3(center.x + Mathf.Cos(angle) * middle, center.y, center.z + Mathf.Sin(angle) * middle);
                int cell = _nav.CellAt(point.x, point.z, 0.999f);
                if (cell != EnemyNavField.None && !float.IsPositiveInfinity(_nav.PathDistance(cell)))
                    chosen = cell;
            }
        }
        if (chosen == EnemyNavField.None)
            chosen = _nav.FindNearestCell(point, 32, float.PositiveInfinity);
        if (chosen == EnemyNavField.None)
            return new Vector3(point.x, _roundSpawnY, point.z);

        // Somewhere inside the cell, so two enemies given the same cell do not start on top of each other.
        Vector3 spot = _nav.Position(chosen);
        float spread = _nav.CellSize * 0.8f;
        spot.x += ((float)_random.NextDouble() - 0.5f) * spread;
        spot.z += ((float)_random.NextDouble() - 0.5f) * spread;
        spot.y = _nav.SampleHeight(chosen, spot) + settings.SpawnHeight;
        return spot;
    }

    private bool HasClearPath(Vector3 center, Vector3 position)
    {
        const float chestHeight = 1f;
        var from = new Vector3(center.x, _roundSpawnY + chestHeight, center.z);
        var to = new Vector3(position.x, _roundSpawnY + chestHeight, position.z);
        return !Physics.Linecast(from, to, _spawnObstacleMask, QueryTriggerInteraction.Ignore);
    }

    private void Release(EnemyAgent enemy)
    {
        _physics.Unregister(enemy.Body);
        _probe.Untrack(enemy);
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
        _attackers.Clear();
        _probe.Clear();
    }

    private void SetAllIdle(int tick)
    {
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = 0; i < active.Count; i++)
            active[i].SetIdle(tick);
    }

    private void HaltAll(int tick)
    {
        if (_registry == null)
            return;
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = 0; i < active.Count; i++)
            if (active[i] != null)
                active[i].HaltLocomotion(tick);
    }

    private void SetAnimationPausedForAll(bool paused)
    {
        if (_registry == null)
            return;
        IReadOnlyList<EnemyAgent> active = _registry.Active;
        for (int i = 0; i < active.Count; i++)
            if (active[i] != null)
                active[i].SetAnimationPaused(paused);
    }
}

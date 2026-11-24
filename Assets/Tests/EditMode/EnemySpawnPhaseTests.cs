using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Spawn phases by battle time, the warning rings that announce Battle spawns, and the option that gives every round the
// first round's layout. Pure rules first, then EnemyRuntime with a bare enemy prefab (no scene).
public sealed class EnemySpawnPhaseTests
{
    private readonly List<Object> _made = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object made in _made)
            if (made != null) Object.DestroyImmediate(made);
        _made.Clear();
    }

    [Test]
    public void ThePhaseInEffectIsTheLatestStartedOneAndChangesExactlyOnItsStartSecond()
    {
        var phases = new[] { Phase(0f, 40), Phase(30f, 70), Phase(70f, 100) };
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(phases, 0f));
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(phases, 29.999f));
        Assert.AreEqual(1, EnemySpawnPhases.IndexAt(phases, 30f), "A phase starts exactly on its start second.");
        Assert.AreEqual(1, EnemySpawnPhases.IndexAt(phases, 69.999f));
        Assert.AreEqual(2, EnemySpawnPhases.IndexAt(phases, 70f));
        Assert.AreEqual(2, EnemySpawnPhases.IndexAt(phases, 500f), "The last phase runs to the end of the round.");

        var unsorted = new[] { Phase(70f, 100), Phase(0f, 40), Phase(30f, 70) };
        Assert.AreEqual(1, EnemySpawnPhases.IndexAt(unsorted, 10f), "The list need not be sorted.");
        Assert.AreEqual(2, EnemySpawnPhases.IndexAt(unsorted, 30f));
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(unsorted, 75f));

        var late = new[] { Phase(5f, 10), Phase(20f, 30) };
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(late, 0f), "Before the earliest start the earliest phase applies.");
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(late, 19.99f));
        Assert.AreEqual(1, EnemySpawnPhases.IndexAt(late, 20f));

        var tie = new[] { Phase(10f, 11), Phase(10f, 22) };
        Assert.AreEqual(0, EnemySpawnPhases.IndexAt(tie, 12f), "Of two phases with the same start the first listed wins.");
        Assert.AreEqual(-1, EnemySpawnPhases.IndexAt(new EnemySpawnPhase[0], 3f));
        Assert.AreEqual(-1, EnemySpawnPhases.IndexAt(null, 3f));
    }

    [Test]
    public void WithoutPhasesTheFlatScheduleRunsTheWholeRound()
    {
        EnemySettings settings = Settings();
        Assert.IsFalse(settings.HasSpawnPhases);
        Assert.AreEqual(0f, settings.SpawnWarningSeconds, "Spawns appear at once unless a warning time is set.");
        Assert.IsFalse(settings.SameLayoutEveryRound);
        foreach (float seconds in new[] { 0f, 30f, 99f })
        {
            EnemySpawnPhase phase = settings.SpawnPhaseAt(seconds);
            Assert.AreEqual(settings.MaximumEnemyCount, phase.Cap);
            Assert.AreEqual(settings.SpawnInterval, phase.Interval);
            Assert.AreEqual(settings.SpawnCountPerWave, phase.PerWave);
        }

        var careless = new EnemySpawnPhase(0f, 0, 0f, 0);
        Assert.AreEqual(1, careless.Cap);
        Assert.AreEqual(0.05f, careless.Interval);
        Assert.AreEqual(1, careless.PerWave);
    }

    [Test]
    public void TheGameStartsSparseAndGetsDenserTowardTheEnd()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        Assert.IsTrue(settings.HasSpawnPhases);
        Assert.Greater(settings.SpawnWarningSeconds, 0f);
        Assert.IsTrue(settings.SameLayoutEveryRound, "Chosen on 2026-11-25: every round starts from the first round's layout (AGENTS.md §17).");
        EnemySpawnPhase previous = settings.SpawnPhaseAt(0f);
        Assert.Less(previous.Cap, settings.MaximumEnemyCount);
        for (float seconds = 1f; seconds <= 100f; seconds += 1f)
        {
            EnemySpawnPhase phase = settings.SpawnPhaseAt(seconds);
            Assert.GreaterOrEqual(phase.Cap, previous.Cap, "The cap never drops during the round.");
            Assert.LessOrEqual(phase.Cap, settings.MaximumEnemyCount);
            Assert.GreaterOrEqual(phase.PerWave / phase.Interval, previous.PerWave / previous.Interval - 0.001f, "Waves never slow down.");
            previous = phase;
        }
        Assert.AreEqual(settings.MaximumEnemyCount, settings.SpawnPhaseAt(99f).Cap, "The full crowd by the end.");
    }

    [Test]
    public void AnnouncedSpawnsCountDownOnlyWithGameTimeAndStaleOnesAreDropped()
    {
        var pending = new EnemyPendingSpawns(2);
        pending.Add(new Vector3(1f, 0f, 2f), 0.25f, 0.6f, 3, 7);
        pending.Advance(0.2f);
        pending.Add(new Vector3(4f, 0f, 5f), 0.75f, 0.6f, 3, 8);
        pending.Add(new Vector3(9f, 0f, 9f), 0.5f, 0.6f, 2, 9);   // from an earlier round; grows the storage
        Assert.AreEqual(3, pending.Count);

        Assert.IsTrue(pending.TryTakeDue(3, out EnemyPendingSpawn stale), "A spawn of another round goes at once.");
        Assert.AreEqual(2, stale.Tick);
        Assert.AreEqual(9, stale.Marker);
        Assert.IsFalse(pending.TryTakeDue(3, out _));

        pending.Advance(0f);   // paused: nothing moves
        Assert.AreEqual(1f / 3f, pending[0].Progress, 0.001f);
        Assert.AreEqual(0f, pending[1].Progress, 0.001f);

        // Frame by frame at 60 fps: the first one is due after its 0.6 s, not a frame later because of rounding.
        for (int frame = 0; frame < 23; frame++)
        {
            Assert.IsFalse(pending.TryTakeDue(3, out _), "frame " + frame);
            pending.Advance(1f / 60f);
        }
        Assert.IsFalse(pending.TryTakeDue(3, out _));
        pending.Advance(1f / 60f);
        Assert.IsTrue(pending.TryTakeDue(3, out EnemyPendingSpawn first));
        Assert.AreEqual(new Vector3(1f, 0f, 2f), first.Position);
        Assert.AreEqual(0.25f, first.Variation);
        Assert.AreEqual(1, pending.Count);
        for (int frame = 0; frame < 11; frame++)
            pending.Advance(1f / 60f);
        Assert.IsFalse(pending.TryTakeDue(3, out _), "35 frames after it was announced");
        pending.Advance(1f / 60f);
        Assert.IsTrue(pending.TryTakeDue(3, out EnemyPendingSpawn second), "36 frames = its 0.6 s");
        Assert.AreEqual(8, second.Marker);
        Assert.AreEqual(1f, second.Progress);

        pending.Add(Vector3.zero, 0.5f, 0.6f, 3, -1);
        Assert.IsTrue(pending.TryTakeFirst(out _), "Cancelling takes them whatever their countdown.");
        Assert.AreEqual(0, pending.Count);
    }

    [Test]
    public void ABattleSpawnFirstShowsARingAndAppearsThereWhenItsWarningIsOver()
    {
        EnemySettings settings = Settings(warning: 0.6f, interval: 1f, perWave: 3);
        EnemyRuntime runtime = Runtime(settings, out Transform target);
        Exhaust(runtime.InitializeRound(3));
        runtime.ApplyState(GameState.Battle, 3);
        var announced = new List<Vector3>();
        runtime.SpawnAnnounced += (position, tick) => announced.Add(position);

        runtime.UpdateSpawning(1f, 3);
        Assert.AreEqual(0, runtime.ActiveEnemyCount, "Nobody appears with the wave itself.");
        Assert.AreEqual(3, runtime.PendingSpawnCount);
        Assert.AreEqual(3, announced.Count);
        Assert.AreEqual(3, runtime.ShownSpawnWarnings, "One ring per announced enemy.");
        for (int i = 0; i < 3; i++)
        {
            Assert.IsTrue(runtime.PendingSpawnShowsRing(i));
            Assert.AreEqual(announced[i], runtime.PendingSpawnPosition(i));
        }
        Material ring = runtime.SpawnWarnings.MaterialOf(0);
        Assert.AreEqual("REBOOT/Ground Indicator", ring.shader.name);
        Assert.AreEqual(settings.SpawnWarningColor, ring.GetColor("_Color"), "Tinted as a threat.");
        Assert.AreEqual(360f, ring.GetFloat("_Arc"));

        runtime.UpdateSpawning(0.3f, 3);
        Assert.AreEqual(0, runtime.ActiveEnemyCount);
        Assert.AreEqual(0.5f, runtime.PendingSpawnProgress(0), 0.001f);
        Assert.AreEqual(0.5f, ring.GetFloat("_Charge"), 0.001f, "The ring fills as the moment comes.");
        runtime.UpdateSpawning(0.3f, 3);
        Assert.AreEqual(3, runtime.ActiveEnemyCount);
        Assert.AreEqual(0, runtime.PendingSpawnCount);
        Assert.AreEqual(0, runtime.ShownSpawnWarnings);
        IReadOnlyList<EnemyAgent> active = runtime.Perf.ActiveEnemies;
        for (int i = 0; i < 3; i++)
            Assert.Less(Vector3.Distance(announced[i], active[i].transform.position), 0.0001f, "It appears where its ring was.");
        Assert.AreSame(target, runtime.TargetTransform);
    }

    [Test]
    public void PauseHoldsAnnouncedSpawnsWhileFinishingAndResetCancelThem()
    {
        EnemySettings settings = Settings(warning: 0.6f, interval: 1f, perWave: 2);
        EnemyRuntime runtime = Runtime(settings, out _);
        Exhaust(runtime.InitializeRound(3));
        runtime.ApplyState(GameState.Battle, 3);
        runtime.UpdateSpawning(1.2f, 3);
        Assert.AreEqual(2, runtime.PendingSpawnCount);
        float progress = runtime.PendingSpawnProgress(0);

        runtime.ApplyState(GameState.Paused, 3);
        runtime.UpdateSpawning(5f, 3);   // nothing runs while paused, even if asked
        Assert.AreEqual(2, runtime.PendingSpawnCount, "Pause holds them.");
        Assert.AreEqual(2, runtime.ShownSpawnWarnings, "Their rings stay up.");
        Assert.AreEqual(progress, runtime.PendingSpawnProgress(0));
        Assert.AreEqual(0, runtime.ActiveEnemyCount);
        Assert.AreEqual(1.2f, runtime.BattleSeconds, 0.0001f, "Battle time holds in Pause.");

        runtime.ApplyState(GameState.Battle, 3);
        runtime.ApplyState(GameState.Finishing, 3);
        Assert.AreEqual(0, runtime.PendingSpawnCount, "The round's end cancels them.");
        Assert.AreEqual(0, runtime.ShownSpawnWarnings, "and hides their rings.");
        runtime.UpdateSpawning(5f, 3);
        Assert.AreEqual(0, runtime.ActiveEnemyCount, "Nothing spawns in Finishing.");

        runtime.StopProducing();
        Exhaust(runtime.ResetRound(4));
        runtime.ApplyState(GameState.Ready, 4);
        runtime.ApplyState(GameState.Battle, 4);
        runtime.UpdateSpawning(1f, 4);
        Assert.AreEqual(2, runtime.PendingSpawnCount);
        runtime.StopProducing();   // Retry: producers stop first
        Assert.AreEqual(0, runtime.PendingSpawnCount);
        Assert.AreEqual(0, runtime.ShownSpawnWarnings);
        Exhaust(runtime.ResetRound(5));
        Assert.AreEqual(0, runtime.PendingSpawnCount);
        Assert.AreEqual(0f, runtime.BattleSeconds, "Battle time starts over with the round.");
        Assert.AreEqual(0, runtime.ActiveEnemyCount);
    }

    [Test]
    public void TheActiveCountFollowsEachPhaseCapAndTheBattleClock()
    {
        EnemySettings settings = Settings();
        SetField(settings, "spawnPhases", new[] { Phase(0f, 6, 0.3f, 2), Phase(2f, 10, 0.4f, 3), Phase(4f, 13, 0.3f, 4) });
        EnemyRuntime runtime = Runtime(settings, out _);
        Exhaust(runtime.InitializeRound(3));
        runtime.ApplyState(GameState.Battle, 3);
        const float frame = 1f / 60f;
        int[] reached = new int[3];
        for (int step = 1; step <= 6 * 60; step++)
        {
            // Every third frame is a hit stop: the game runs at a tenth of real time but battle time keeps real time.
            bool hitStop = step % 3 == 0;
            runtime.UpdateSpawning(hitStop ? frame * 0.1f : frame, frame, 3);
            float seconds = runtime.BattleSeconds;
            Assert.AreEqual(step * frame, seconds, 0.001f, "Battle time follows the round clock, not the slowed game time.");
            int expectedCap = seconds < 2f ? 6 : seconds < 4f ? 10 : 13;
            Assert.AreEqual(expectedCap, runtime.CurrentSpawnCap, $"cap at {seconds:0.000} s");
            Assert.LessOrEqual(runtime.ActiveEnemyCount, runtime.CurrentSpawnCap, $"count at {seconds:0.000} s");
            int phase = expectedCap == 6 ? 0 : expectedCap == 10 ? 1 : 2;
            reached[phase] = Mathf.Max(reached[phase], runtime.ActiveEnemyCount);
        }
        Assert.AreEqual(6, reached[0], "The first phase fills to its cap.");
        Assert.AreEqual(10, reached[1]);
        Assert.AreEqual(13, reached[2]);
    }

    [Test]
    public void BattleTimeTakesTheRoundTimersStepInEveryKindOfFrame()
    {
        const float frame = 1f / 60f, stopScale = 0.05f, longest = 1f / 3f;
        // An ordinary frame: game time, as the round timer takes. A hit stop requested during the frame (a blow landed
        // before the spawner runs) does not change it: the frame began at full speed, so the timer took game time too.
        Assert.AreEqual(frame, EnemySpawnSystem.RoundClockStep(frame, frame, longest));
        // A frame that began inside a hit stop, whose deltaTime Unity scaled: real time, as the timer takes. That holds
        // in its last frame as well, in which the timer has already ended the stop and put the time scale back.
        Assert.AreEqual(frame, EnemySpawnSystem.RoundClockStep(frame * stopScale, frame, longest));
        Assert.AreEqual(frame, EnemySpawnSystem.RoundClockStep(frame * 0.5f, frame, longest));
        // A long frame outside a hit stop: game time stops at the maximum delta, and so does the timer's step...
        Assert.AreEqual(longest, EnemySpawnSystem.RoundClockStep(longest, 0.5f, longest));
        // ...while inside a hit stop the timer takes the whole real step.
        Assert.AreEqual(0.5f, EnemySpawnSystem.RoundClockStep(longest * stopScale, 0.5f, longest));
        Assert.AreEqual(0f, EnemySpawnSystem.RoundClockStep(0f, 0f, longest));

        // Eight hit stops of six frames each, 20 frames apart: battle time ends where the round timer does.
        float battle = 0f, timer = 0f;
        for (int f = 0; f < 8 * 20; f++)
        {
            bool inStop = f % 20 < 6;
            float deltaTime = inStop ? frame * stopScale : frame;
            battle += EnemySpawnSystem.RoundClockStep(deltaTime, frame, longest);
            timer += inStop ? frame : deltaTime;
        }
        Assert.AreEqual(timer, battle, 0.0001f);
        Assert.AreEqual(8 * 20 * frame, battle, 0.0001f);
    }

    [Test]
    public void SameLayoutEveryRoundRepeatsTheFirstRoundsEnemiesAndOffKeepsEachRoundsOwn()
    {
        foreach (bool same in new[] { true, false })
        {
            EnemySettings settings = Settings();
            SetField(settings, "initialEnemyCount", 6);
            SetField(settings, "sameLayoutEveryRound", same);
            EnemyRuntime runtime = Runtime(settings, out _);
            Exhaust(runtime.InitializeRound(EnemyRuntime.FirstRoundTick));
            List<Vector3> first = Positions(runtime);
            List<EnemyTemperament> firstMinds = Minds(runtime);
            for (int tick = 2; tick <= 3; tick++)
            {
                runtime.StopProducing();
                Exhaust(runtime.ResetRound(tick));
                List<Vector3> again = Positions(runtime);
                Assert.AreEqual(first.Count, again.Count);
                bool identical = true;
                for (int i = 0; i < first.Count; i++)
                    identical &= Vector3.Distance(first[i], again[i]) < 0.0001f;
                if (same)
                {
                    Assert.IsTrue(identical, $"Round {tick} must place its enemies where the first round did.");
                    CollectionAssert.AreEqual(firstMinds, Minds(runtime), "...with the same temperaments.");
                }
                else
                    Assert.IsFalse(identical, $"Round {tick} gets its own layout when the option is off.");
            }
        }
    }

    private static List<Vector3> Positions(EnemyRuntime runtime)
    {
        var positions = new List<Vector3>();
        foreach (EnemyAgent enemy in runtime.Perf.ActiveEnemies)
            positions.Add(enemy.transform.position);
        return positions;
    }

    private static List<EnemyTemperament> Minds(EnemyRuntime runtime)
    {
        var minds = new List<EnemyTemperament>();
        foreach (EnemyAgent enemy in runtime.Perf.ActiveEnemies)
            minds.Add(enemy.Temperament);
        return minds;
    }

    private static EnemySpawnPhase Phase(float start, int cap, float interval = 1f, int perWave = 1) =>
        new EnemySpawnPhase(start, cap, interval, perWave);

    private EnemySettings Settings(float warning = 0f, float interval = 1f, int perWave = 5)
    {
        var settings = ScriptableObject.CreateInstance<EnemySettings>();
        _made.Add(settings);
        SetField(settings, "initialEnemyCount", 0);
        SetField(settings, "spawnWarningSeconds", warning);
        SetField(settings, "spawnInterval", interval);
        SetField(settings, "spawnCountPerWave", perWave);
        return settings;
    }

    private EnemyRuntime Runtime(EnemySettings settings, out Transform target)
    {
        var owner = new GameObject("SpawnPhaseOwner");
        var player = new GameObject("SpawnPhaseTarget");
        var prototype = new GameObject("SpawnPhasePrototype");
        _made.Add(owner);
        _made.Add(player);
        _made.Add(prototype);
        player.transform.position = new Vector3(3f, 0f, -2f);
        var runtime = owner.AddComponent<EnemyRuntime>();
        SetField(runtime, "enemyPrefab", prototype.AddComponent<EnemyAgent>());
        SetField(runtime, "settings", settings);
        runtime.SetTarget(player.transform);
        runtime.Configure(new PhysicsPauseManager());
        target = player.transform;
        return runtime;
    }

    private static void SetField(object instance, string field, object value)
    {
        FieldInfo info = instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, "Field not found: " + field);
        info.SetValue(instance, value);
    }

    private static void Exhaust(IEnumerator routine)
    {
        while (routine.MoveNext()) { }
    }
}

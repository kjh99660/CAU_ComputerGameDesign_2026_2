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

    [Test]
    public void APhaseHasNoEventUnlessItAsksForOneAndFiresOnlyAsTheClockPassesItsStart()
    {
        var plain = new EnemySpawnPhase(5f, 10, 1f, 1);
        Assert.IsFalse(plain.HasEvent, "Old phase lists (no burst, no warning) fire nothing.");
        Assert.AreEqual(0, plain.Burst);
        Assert.AreEqual(-1, plain.WarningIndex);
        var mass = new EnemySpawnPhase(5f, 10, 1f, 1, 12, 2);
        Assert.IsTrue(mass.HasEvent);
        Assert.AreEqual(12, mass.Burst);
        Assert.AreEqual(1, mass.WarningIndex, "Warning number 2 is the second line.");
        Assert.IsTrue(mass.StartsBetween(4.98f, 5f), "It starts on its start second.");
        Assert.IsFalse(mass.StartsBetween(5f, 5.02f), "...once: the next frame is already past it.");
        Assert.IsFalse(mass.StartsBetween(3f, 4.9f));
        Assert.IsFalse(new EnemySpawnPhase(0f, 10, 1f, 1, 5, 1).StartsBetween(0f, 0.02f), "A phase at 0 never fires an event.");
    }

    // 2026-11-25 request: a small mass spawn at 25% of the round, the spawn rate doubled from 50%, a large mass spawn at
    // 75%, each with Noa's warning. Kept in step with the round length (CombatSettings.roundDuration).
    [Test]
    public void TheGameMassSpawnsAtAQuarterAndThreeQuartersAndDoublesTheRateAtHalfTime()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        var combat = Resources.Load<CombatSettings>("CombatSettings");
        var dialogue = Resources.Load<DialogueSequenceData>("DialogueSequence");
        float round = combat.roundDuration;
        var events = new List<EnemySpawnPhase>();
        foreach (EnemySpawnPhase phase in settings.SpawnPhases)
            if (phase.HasEvent) events.Add(phase);
        Assert.AreEqual(3, events.Count, "Three spawn events.");
        events.Sort((a, b) => a.startSecond.CompareTo(b.startSecond));
        Assert.AreEqual(0.25f * round, events[0].startSecond, 0.001f, "Small mass spawn at 25%.");
        Assert.AreEqual(0.5f * round, events[1].startSecond, 0.001f, "Spawn rate doubles at 50%.");
        Assert.AreEqual(0.75f * round, events[2].startSecond, 0.001f, "Large mass spawn at 75%.");
        Assert.Greater(events[0].Burst, 0);
        Assert.AreEqual(0, events[1].Burst, "The 50% event only speeds the waves up.");
        Assert.Greater(events[2].Burst, events[0].Burst, "The 75% mass spawn is the larger one.");

        EnemySpawnPhase before = settings.SpawnPhaseAt(0.5f * round - 0.01f);
        EnemySpawnPhase after = settings.SpawnPhaseAt(0.5f * round);
        Assert.AreEqual(2f * before.PerWave / before.Interval, after.PerWave / after.Interval, 0.001f, "Twice the spawn rate from 50% on.");
        EnemySpawnPhase late = settings.SpawnPhaseAt(0.75f * round);
        Assert.AreEqual(after.PerWave / after.Interval, late.PerWave / late.Interval, 0.001f, "...and it stays doubled.");
        Assert.LessOrEqual(settings.SpawnPhaseAt(0.75f * round - 0.01f).Cap + events[2].Burst, settings.MaximumEnemyCount,
            "Room for the whole large mass spawn under the cap of 100.");

        foreach (EnemySpawnPhase phase in events)
        {
            Assert.That(phase.WarningIndex, Is.InRange(0, dialogue.spawnWarnings.Length - 1), "Each event has Noa's warning.");
            DialogueLine line = dialogue.spawnWarnings[phase.WarningIndex];
            Assert.AreEqual("노아", line.speaker);
            Assert.IsFalse(string.IsNullOrWhiteSpace(line.text));
        }
    }

    // 2026-11-25 request: blue one-hit robots first, red two-hit robots mixed in from half time, only red from 75%
    // (starting with the large mass spawn); wind-up 25% shorter and move speed 25% higher.
    [Test]
    public void TheGameSendsBlueRobotsFirstMixesInRedFromHalfTimeAndOnlyRedFromThreeQuarters()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        var combat = Resources.Load<CombatSettings>("CombatSettings");
        float round = combat.roundDuration;
        Assert.AreEqual(0f, settings.SpawnPhaseAt(0f).Tough, "Only the weak kind at the start.");
        Assert.AreEqual(0f, settings.SpawnPhaseAt(0.5f * round - 0.01f).Tough, "...until half time, the 25% mass spawn included.");
        Assert.That(settings.SpawnPhaseAt(0.5f * round).Tough, Is.GreaterThan(0f).And.LessThan(1f), "Both kinds from half time.");
        EnemySpawnPhase last = settings.SpawnPhaseAt(0.75f * round);
        Assert.AreEqual(1f, last.Tough, "Only the tough kind from 75%.");
        Assert.Greater(last.Burst, 0, "...starting with the large mass spawn, all tough.");
        Assert.AreEqual(2, settings.ToughHits, "A red robot takes two hits.");
        Assert.Greater(settings.WeakColor.b, settings.WeakColor.r, "The weak kind is blue.");
        Assert.Greater(settings.ToughColor.r, settings.ToughColor.b, "The tough kind is the red robot.");
        Assert.AreNotEqual(settings.SpawnWarningWeakColor, settings.SpawnWarningColor, "Their warning rings differ too.");
        Assert.AreEqual(0.41f, settings.AttackWindup, 0.001f, "Wind-up 25% shorter than 0.55 s.");
        Assert.AreEqual(4.375f, settings.MoveSpeed, 0.001f, "Move speed 25% above 3.5 m/s.");
    }

    [Test]
    public void EachSpawnTakesItsKindFromItsPhaseAndTheSameRoundDrawsTheSameKinds()
    {
        EnemySettings settings = Settings(interval: 0.5f, perWave: 2);
        var blue = new Color(0.2f, 0.55f, 1f, 1f);
        SetField(settings, "weakColor", blue);
        SetField(settings, "spawnPhases", new[]
        {
            new EnemySpawnPhase(0f, 4, 0.5f, 2, 0, 0, 0f),
            new EnemySpawnPhase(2f, 30, 0.5f, 2, 0, 0, 0.5f),
            new EnemySpawnPhase(5f, 60, 0.5f, 2, 6, 0, 1f),
        });
        const float frame = 1f / 60f;
        var runs = new List<List<bool>>();
        for (int run = 0; run < 2; run++)
        {
            EnemyRuntime runtime = Runtime(settings, out _);
            Exhaust(runtime.InitializeRound(3));
            runtime.ApplyState(GameState.Battle, 3);
            int beforeBurst = 0, afterBurst = 0;
            for (int step = 1; step <= 6 * 60; step++)
            {
                runtime.UpdateSpawning(frame, frame, 3);
                if (step == 114)   // 1.9 s: the first phase, all weak
                    foreach (EnemyAgent enemy in runtime.Perf.ActiveEnemies)
                    {
                        Assert.IsFalse(enemy.Tough, "Only the weak kind in a phase with no tough share.");
                        Assert.AreEqual(1, enemy.HitsLeft);
                        Assert.AreEqual(blue, enemy.SkinColor, "...in the weak kind's skin.");
                    }
                if (step == 294) beforeBurst = runtime.ActiveEnemyCount;   // 4.9 s
                if (step == 306) afterBurst = runtime.ActiveEnemyCount;    // 5.1 s
            }
            IReadOnlyList<EnemyAgent> all = runtime.Perf.ActiveEnemies;
            int toughInMix = 0, weakInMix = 0;
            for (int i = 6; i < beforeBurst; i++)
                if (all[i].Tough) toughInMix++; else weakInMix++;
            Assert.Greater(toughInMix, 0, "A half share brings tough enemies...");
            Assert.Greater(weakInMix, 0, "...and weak ones.");
            Assert.GreaterOrEqual(afterBurst - beforeBurst, 6, "The mass spawn of the last phase.");
            Assert.Greater(all.Count, afterBurst, "Waves keep coming after it.");
            for (int i = afterBurst; i < all.Count; i++)
            {
                Assert.IsTrue(all[i].Tough, "Only the tough kind in a phase with a full tough share.");
                Assert.AreEqual(settings.ToughHits, all[i].HitsLeft);
                Assert.AreEqual(settings.ToughColor, all[i].SkinColor);
            }
            var kinds = new List<bool>();
            foreach (EnemyAgent enemy in all) kinds.Add(enemy.Tough);
            runs.Add(kinds);
        }
        CollectionAssert.AreEqual(runs[0], runs[1], "The same round draws the same kinds.");
    }
}

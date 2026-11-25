using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Spawning in the real scene at a fixed 60 fps step: the active count follows the spawn phases by battle time (the round
// timer's elapsed time, held in Pause and kept through hit stops), every Battle spawn is announced by a neon ring on the
// floor where it appears, Pause holds the announcements and Finishing and Retry clear them, and the same-layout option
// repeats the first round's enemies after a Retry.
public sealed class EnemySpawnPhaseSceneTests
{
    private const int Fps = 60;
    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private int _oldCaptureFramerate;
    private readonly List<Object> _made = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = Fps;
    }

    [TearDown]
    public void TearDown()
    {
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        foreach (Object made in _made)
            if (made != null) Object.Destroy(made);
        _made.Clear();
    }

    [UnityTest]
    public IEnumerator TheActiveCountFollowsThePhaseCapsOverBattleTime()
    {
        yield return Load();
        // A short schedule on a copy of the game's settings, so the test needs seconds instead of a whole round.
        EnemySettings settings = CopyOfGameSettings();
        SetField(settings, "spawnPhases", new[]
        {
            new EnemySpawnPhase(0f, 9, 0.5f, 2), new EnemySpawnPhase(2.5f, 15, 0.4f, 3), new EnemySpawnPhase(5f, 22, 0.3f, 4)
        });
        Use(settings);
        Assert.Greater(settings.SpawnWarningSeconds, 0f, "The game announces its spawns.");
        yield return EnterBattle();

        int[] reached = new int[3];
        for (int frame = 0; frame < 8 * Fps; frame++)
        {
            yield return null;
            float seconds = _enemies.BattleSeconds;
            int phase = seconds < 2.5f ? 0 : seconds < 5f ? 1 : 2;
            int cap = phase == 0 ? 9 : phase == 1 ? 15 : 22;
            Assert.AreEqual(cap, _enemies.CurrentSpawnCap, $"cap at {seconds:0.00} s");
            Assert.LessOrEqual(_enemies.ActiveEnemyCount + _enemies.PendingSpawnCount, cap, $"enemies and announced ones at {seconds:0.00} s");
            reached[phase] = Mathf.Max(reached[phase], _enemies.ActiveEnemyCount);
            // Battle time is the round timer's elapsed time.
            Assert.AreEqual(_combat.Settings.roundDuration - _combat.RoundRemaining, seconds, 1.5f / Fps);
            if (frame == 3 * Fps)
            {
                // Pause holds battle time and the announcements.
                _events.EnqueueRequest(new PauseRequest(_state.Tick));
                yield return WaitFor(GameState.Paused);
                float held = _enemies.BattleSeconds;
                int pending = _enemies.PendingSpawnCount, active = _enemies.ActiveEnemyCount;
                for (int i = 0; i < 40; i++) yield return null;
                Assert.AreEqual(held, _enemies.BattleSeconds);
                Assert.AreEqual(pending, _enemies.PendingSpawnCount);
                Assert.AreEqual(active, _enemies.ActiveEnemyCount);
                _events.EnqueueRequest(new ResumeRequest(_state.Tick));
                yield return WaitFor(GameState.Battle);
            }
        }
        Assert.AreEqual(9, reached[0], "The first phase fills up to its cap...");
        Assert.AreEqual(15, reached[1], "...the second to its higher cap...");
        Assert.AreEqual(22, reached[2], "...and the third to its own.");
    }

    [UnityTest]
    public IEnumerator BattleTimeKeepsStepWithTheRoundTimerThroughHitStops()
    {
        yield return Load();
        yield return EnterBattle();
        // Hit stops as landed blows make them: for a few frames the game runs at a twentieth of real time while the round
        // timer keeps real time. Battle time must keep step with the timer through every frame of every stop, the last
        // one included, in which the timer ends the stop and puts the time scale back before the spawner runs.
        int stopsEnded = 0;
        bool stopping = false;
        for (int frame = 0; frame < 4 * Fps; frame++)
        {
            if (frame % 20 == 0)
                _combat.RequestHitStop(0.1f, 0.05f);
            yield return null;
            bool nowStopping = _combat.HitStopRemaining > 0f;
            if (stopping && !nowStopping) stopsEnded++;
            stopping = nowStopping;
            Assert.AreEqual(_combat.Settings.roundDuration - _combat.RoundRemaining, _enemies.BattleSeconds, 0.002f,
                $"Battle time after {stopsEnded} hit stops (frame {frame}).");
        }
        Assert.GreaterOrEqual(stopsEnded, 10, "The hit stops really happened.");
    }

    [UnityTest]
    public IEnumerator EveryBattleSpawnIsAnnouncedByARingWhereItAppears()
    {
        yield return Load();
        var seen = new HashSet<EnemyAgent>(_enemies.Perf.ActiveEnemies);
        Assert.AreEqual(_enemies.Settings.InitialEnemyCount, seen.Count, "The enemies placed in Ready appear at once.");
        Assert.AreEqual(0, _enemies.ShownSpawnWarnings);
        var announcements = new List<(Vector3 position, int frame)>();
        int frameNow = 0;
        _enemies.SpawnAnnounced += (position, tick) => announcements.Add((position, frameNow));
        yield return EnterBattle();

        int warningFrames = Mathf.RoundToInt(_enemies.Settings.SpawnWarningSeconds * Fps);
        int checkedSpawns = 0;
        for (frameNow = 0; frameNow < 7 * Fps; frameNow++)
        {
            yield return null;
            Assert.AreEqual(_enemies.PendingSpawnCount, _enemies.ShownSpawnWarnings, "One ring per announced enemy.");
            for (int i = 0; i < _enemies.PendingSpawnCount; i++)
                Assert.IsTrue(_enemies.PendingSpawnShowsRing(i));
            foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            {
                if (!seen.Add(enemy)) continue;
                checkedSpawns++;
                int announced = -1;
                foreach ((Vector3 position, int frame) in announcements)
                    if (Vector3.Distance(position, enemy.transform.position) < 0.01f) announced = frame;
                Assert.GreaterOrEqual(announced, 0, $"The enemy at {enemy.transform.position} appeared without a ring.");
                Assert.GreaterOrEqual(frameNow - announced, warningFrames - 1, "It appears only after its warning time.");
                Assert.LessOrEqual(frameNow - announced, warningFrames + 1);
            }
        }
        Assert.Greater(checkedSpawns, 5, "Waves came in this time.");
        EnemySpawnWarnings rings = _enemies.SpawnWarnings;
        Assert.IsNotNull(rings);
        Assert.IsTrue(rings.CanDraw, "The neon floor marking material is there.");
        Material material = rings.MaterialOf(0);
        Assert.AreEqual("REBOOT/Ground Indicator", material.shader.name);
        // The first phase spawns the weak (blue) kind, and a ring shows the colour of the kind it announces.
        Assert.AreEqual(_enemies.Settings.SpawnWarningWeakColor, material.GetColor("_Color"));
        Assert.AreEqual(_enemies.Settings.SpawnWarningWeakHotColor, material.GetColor("_HotColor"));
    }

    [UnityTest]
    public IEnumerator PauseHoldsTheRingsWhileFinishingAndRetryClearThem()
    {
        yield return Load();
        yield return EnterBattle();
        yield return Until(() => _enemies.PendingSpawnCount > 0);
        // Paused: the countdown, the ring's fill and its animation clock all hold.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.Greater(_enemies.PendingSpawnCount, 0);
        float progress = _enemies.PendingSpawnProgress(0);
        Material ring = _enemies.SpawnWarnings.MaterialOf(0);
        float charge = ring.GetFloat("_Charge"), clock = ring.GetFloat("_Clock");
        Assert.GreaterOrEqual(clock, 0f, "The ring runs on its own clock, not on scene time.");
        int active = _enemies.ActiveEnemyCount, shown = _enemies.ShownSpawnWarnings;
        yield return new WaitForSecondsRealtime(0.8f);
        Assert.AreEqual(progress, _enemies.PendingSpawnProgress(0));
        Assert.AreEqual(charge, ring.GetFloat("_Charge"));
        Assert.AreEqual(clock, ring.GetFloat("_Clock"));
        Assert.AreEqual(active, _enemies.ActiveEnemyCount);
        Assert.AreEqual(shown, _enemies.ShownSpawnWarnings);
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        // TIME UP: the announced enemies never come and their rings go.
        yield return Until(() => _enemies.PendingSpawnCount > 0);
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        Assert.AreEqual(0, _enemies.PendingSpawnCount);
        Assert.AreEqual(0, _enemies.ShownSpawnWarnings);
        int atFinish = _enemies.ActiveEnemyCount;
        while (_state.CurrentState == GameState.Finishing)
        {
            Assert.LessOrEqual(_enemies.ActiveEnemyCount, atFinish, "Nothing spawns in Finishing.");
            Assert.AreEqual(0, _enemies.ShownSpawnWarnings);
            yield return null;
        }
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0f, _enemies.BattleSeconds);
        Assert.AreEqual(0, _enemies.PendingSpawnCount);
        Assert.AreEqual(_enemies.Settings.InitialEnemyCount, _enemies.ActiveEnemyCount);

        // Retry from Pause in the middle of a warning.
        yield return EnterBattle();
        yield return Until(() => _enemies.PendingSpawnCount > 0);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, _enemies.PendingSpawnCount);
        Assert.AreEqual(0, _enemies.ShownSpawnWarnings, "No ring is left from the old round.");
        Assert.AreEqual(_enemies.Settings.InitialEnemyCount, _enemies.ActiveEnemyCount);
        Assert.AreEqual(1f, Time.timeScale);
        for (int i = 0; i < 30; i++)
        {
            yield return null;
            Assert.AreEqual(0, _enemies.ShownSpawnWarnings, "Ready spawns nothing more.");
        }
    }

    [UnityTest]
    public IEnumerator ARetryStartsWithTheFirstRoundsEnemiesUnlessTheOptionIsOff()
    {
        yield return Load();
        List<Vector3> first = Positions();
        Assert.AreEqual(EnemyRuntime.FirstRoundTick, _state.Tick);
        // Chosen on 2026-11-25 (AGENTS.md §17): a retried round starts from the first round's initial conditions.
        Assert.IsTrue(_enemies.Settings.SameLayoutEveryRound, "On in the game: every Retry starts with the first round's enemies.");
        for (int round = 0; round < 2; round++)
        {
            yield return Retry();
            Assert.IsTrue(Same(first, Positions()), $"On: round {_state.Tick} starts with the first round's enemies.");
        }

        EnemySettings settings = CopyOfGameSettings();
        SetField(settings, "sameLayoutEveryRound", false);
        Use(settings);
        yield return Retry();
        Assert.IsFalse(Same(first, Positions()), "Off: a round gets its own layout.");
    }

    // Spawn events in the real scene, on a short phase list: each fires once as the battle clock passes its second, its
    // mass spawn is announced at once with warning rings, Noa's warning shows on the HUD, Pause holds the clock, and a
    // Retry brings the events back for the new round.
    [UnityTest]
    public IEnumerator SpawnEventsMassSpawnAndWarnOnceHoldInPauseAndComeBackAfterRetry()
    {
        yield return Load();
        EnemySettings settings = CopyOfGameSettings();
        SetField(settings, "spawnPhases", new[]
        {
            new EnemySpawnPhase(0f, 6, 1f, 1),
            new EnemySpawnPhase(1f, 20, 1f, 1, 8, 1),
            new EnemySpawnPhase(2f, 30, 0.5f, 1, 0, 2),
            new EnemySpawnPhase(3f, 45, 0.5f, 1, 12, 3),
        });
        Use(settings);
        DialogueSequenceData dialogue = Object.FindFirstObjectByType<TextManager>().Sequence;
        HudRadioSubtitle radio = Object.FindFirstObjectByType<HudRadioSubtitle>(FindObjectsInactive.Include);
        Assert.IsNotNull(radio);
        var fired = new List<Vector2Int>();
        System.Action<int, int, int> record = (phase, warning, tick) => fired.Add(new Vector2Int(phase, warning));
        _enemies.SpawnPhaseStarted += record;
        try
        {
            yield return EnterBattle();
            yield return UntilBattleSeconds(0.9f);
            Assert.AreEqual(0, fired.Count, "Nothing before its second.");
            int onField = _enemies.Perf.ActiveEnemies.Count + _enemies.PendingSpawnCount;
            yield return UntilBattleSeconds(1.02f);
            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(new Vector2Int(1, 0), fired[0], "The 1 s phase fires with warning number 1.");
            Assert.GreaterOrEqual(_enemies.Perf.ActiveEnemies.Count + _enemies.PendingSpawnCount, onField + 8,
                "Eight enemies announced at once.");
            Assert.GreaterOrEqual(_enemies.ShownSpawnWarnings, 8, "Each with its warning ring.");
            Assert.IsTrue(radio.IsShowing, "Noa's warning shows on the HUD.");
            Assert.IsTrue(radio.ShowsWarning);
            Assert.AreEqual("WARNING", radio.ChannelShown);
            Assert.AreEqual(dialogue.spawnWarnings[0].speaker, radio.SpeakerShown);
            Assert.AreEqual(dialogue.spawnWarnings[0].text, radio.TextShown);

            // Pause holds the battle clock, so no event fires while paused.
            _events.EnqueueRequest(new PauseRequest(_state.Tick));
            yield return WaitFor(GameState.Paused);
            float held = _enemies.BattleSeconds;
            for (int i = 0; i < 90; i++) yield return null;
            Assert.AreEqual(held, _enemies.BattleSeconds, 1e-5f);
            Assert.AreEqual(1, fired.Count);
            _events.EnqueueRequest(new ResumeRequest(_state.Tick));
            yield return WaitFor(GameState.Battle);

            yield return UntilBattleSeconds(3.05f);
            Assert.AreEqual(3, fired.Count, "The 2 s and 3 s events fired.");
            Assert.AreEqual(new Vector2Int(2, 1), fired[1]);
            Assert.AreEqual(new Vector2Int(3, 2), fired[2]);
            Assert.AreEqual(dialogue.spawnWarnings[2].text, radio.TextShown, "The latest warning shows.");
            yield return UntilBattleSeconds(4f);
            Assert.AreEqual(3, fired.Count, "No event fires twice in a round.");

            // Retry clears the warning and brings the events back for the new round.
            _events.EnqueueRequest(new PauseRequest(_state.Tick));
            yield return WaitFor(GameState.Paused);
            _events.EnqueueRequest(new RestartRequest(_state.Tick));
            yield return WaitFor(GameState.Ready);
            Assert.IsFalse(radio.IsShowing, "Retry clears the warning.");
            fired.Clear();
            yield return EnterBattle();
            yield return UntilBattleSeconds(1.02f);
            Assert.AreEqual(1, fired.Count, "The 1 s event fires again in the new round.");
        }
        finally
        {
            _enemies.SpawnPhaseStarted -= record;
        }
    }

    private IEnumerator UntilBattleSeconds(float seconds)
    {
        for (int frame = 0; frame < 900 && _enemies.BattleSeconds < seconds; frame++)
            yield return null;
        Assert.GreaterOrEqual(_enemies.BattleSeconds, seconds, "The battle clock reached " + seconds + " s.");
    }

    // ---------------------------------------------------------------- helpers
    private IEnumerator Retry()
    {
        yield return EnterBattle();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
    }

    private List<Vector3> Positions()
    {
        var positions = new List<Vector3>();
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            positions.Add(enemy.transform.position);
        return positions;
    }

    private static bool Same(List<Vector3> a, List<Vector3> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (Vector3.Distance(a[i], b[i]) > 0.001f) return false;
        return true;
    }

    private EnemySettings CopyOfGameSettings()
    {
        EnemySettings copy = Object.Instantiate(_enemies.Settings);
        _made.Add(copy);
        return copy;
    }

    private void Use(EnemySettings settings) => SetField(_enemies, "settings", settings);

    private static void SetField(object instance, string field, object value)
    {
        FieldInfo info = instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, "Field not found: " + field);
        info.SetValue(instance, value);
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_enemies);
        yield return WaitFor(GameState.Ready);
    }

    private IEnumerator EnterBattle()
    {
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }

    private static IEnumerator Until(System.Func<bool> done)
    {
        for (int frame = 0; frame < 20 * Fps && !done(); frame++)
            yield return null;
        Assert.IsTrue(done(), "Timed out.");
    }
}

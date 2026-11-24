using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Plays the city stage (Main_NeonCity.unity) at a fixed 60 fps step: streets, a second storey reached by three
// stairs and a bridge, third-storey roofs. Checks that enemies spawn on real ground on every floor, climb to the
// player, come down after the player, and that the round still resets in place.
public sealed class NeonCitySceneTests
{
    private const string ScenePath = "Assets/Scenes/Main_NeonCity.unity";
    private const int Fps = 60;
    private const int Level = 1 << 0;
    private static readonly Vector3 EastTerrace = new(10f, 6.9f, 3f);
    private static readonly Vector3 AlleyUnderTheTerrace = new(0.5f, 0.3f, 3f);

    private readonly HashSet<Key> _keys = new();
    private Keyboard _keyboard;
    private int _oldCaptureFramerate;
    private InputSettings.BackgroundBehavior _oldBackground;
#if UNITY_EDITOR
    private InputSettings.EditorInputBehaviorInPlayMode _oldEditorBehavior;
#endif
    private GameStateManager _state;
    private EventManager _events;
    private PlayerRuntime _player;
    private EnemyRuntime _enemies;
    private CharacterController _controller;

    [UnitySetUp]
    public IEnumerator LoadStage()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = Fps;
        InputSettings settings = InputSystem.settings;
        _oldBackground = settings.backgroundBehavior;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        _oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        Assert.Ignore("The city stage is not in the build yet; it is loaded through the editor.");
#endif
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _controller = _player.GetComponent<CharacterController>();
        yield return WaitFor(GameState.Ready);
    }

    [TearDown]
    public void Restore()
    {
        if (_keyboard != null)
            InputSystem.RemoveDevice(_keyboard);
        _keyboard = null;
        _keys.Clear();
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        InputSettings settings = InputSystem.settings;
        settings.backgroundBehavior = _oldBackground;
#if UNITY_EDITOR
        settings.editorInputBehaviorInPlayMode = _oldEditorBehavior;
#endif
    }

    [UnityTest]
    public IEnumerator StageStartsWithGroundToWalkOnAndItsOwnLook()
    {
        // Ground: measured once, several floors, and the player and the first enemies stand on it.
        EnemyNavField ground = _enemies.Navigation;
        Assert.IsNotNull(ground, "The city needs its EnemyNavVolume.");
        Assert.Greater(ground.CellCount, 8000);
        var volume = Object.FindFirstObjectByType<EnemyNavVolume>();
        Assert.Less(volume.LastBakeMilliseconds, 500d, "Measuring the level is part of loading; it must stay short.");
        Assert.AreEqual(2, ground.FloorsAt(0.5f, 6.2f), "The bridge and the alley under it.");

        Vector3 start = _player.transform.position;
        Assert.IsTrue(Physics.Raycast(start + Vector3.up, Vector3.down, out RaycastHit street, 3f, Level, QueryTriggerInteraction.Ignore));
        Assert.AreEqual(street.point.y, start.y, 0.05f, "The player starts standing on the street.");
        Assert.AreNotEqual(EnemyNavField.None, ground.FindNearestCell(start, 2, 1f));

        List<EnemyAgent> first = Active();
        Assert.AreEqual(_enemies.Settings.InitialEnemyCount, first.Count);
        foreach (EnemyAgent enemy in first)
        {
            Assert.AreEqual(EnemyAgentState.Idle, enemy.State);
            AssertStandsOnGround(enemy, "initial enemy");
        }

        // Look: the stage's own surfaces, lights, reflections, haze and grade are in the scene.
        Shader city = Shader.Find("REBOOT/City Lit");
        Assert.IsNotNull(city);
        Assert.IsTrue(city.isSupported, "The city shader must compile on this machine.");
        int surfaces = 0;
        foreach (Renderer renderer in GameObject.Find("Stage_NeonCity").GetComponentsInChildren<Renderer>())
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader == city)
                surfaces++;
        Assert.Greater(surfaces, 1000);
        Assert.IsTrue(RenderSettings.fog);
        Assert.AreEqual("REBOOT/Night Sky", RenderSettings.skybox.shader.name);
        Assert.Greater(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length, 60);
        ReflectionProbe[] probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
        Assert.AreEqual(6, probes.Length);
        foreach (ReflectionProbe probe in probes)
            Assert.IsTrue(probe.texture != null || probe.mode == ReflectionProbeMode.Realtime, probe.name + " has nothing to reflect.");
        var grade = Object.FindFirstObjectByType<Volume>();
        Assert.AreEqual("VP_NeonCity", grade.sharedProfile.name);
        yield break;
    }

    [UnityTest]
    public IEnumerator EnemiesSpawnOnRealGroundOnEveryFloor()
    {
        yield return EnterBattle();
        EnemySettings settings = _enemies.Settings;
        var before = new HashSet<EnemyAgent>(Active());
        int street = 0, upper = 0, spawned = 0;

        // Player in the street, then up on the terrace with everyone before cleared away.
        for (int round = 0; round < 2; round++)
        {
            if (round == 1)
            {
                Teleport(EastTerrace);
                foreach (EnemyAgent enemy in Active())
                    _enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick, false);
            }
            for (int frame = 0; frame < 12 * Fps; frame++)
            {
                yield return null;
                Vector3 player = _player.transform.position;
                List<EnemyAgent> now = Active();
                foreach (EnemyAgent enemy in now)
                {
                    // New this frame: enemies come from a pool, so the same object spawns again and again.
                    if (before.Contains(enemy))
                        continue;
                    spawned++;
                    Vector3 position = enemy.transform.position;
                    AssertStandsOnGround(enemy, "spawn " + spawned);
                    Vector3 flat = position - player;
                    flat.y = 0f;
                    // One frame of walking may have passed since it appeared.
                    Assert.That(flat.magnitude, Is.InRange(settings.MinimumSpawnDistance - 0.6f, settings.MaximumSpawnDistance + 0.6f),
                        $"Spawn {spawned} at {position} is off the ring around the player at {player}.");
                    Assert.Less(_enemies.Navigation.PathDistance(enemy.NavCell), 200f, "Every spawn can walk to the player.");
                    if (position.y > 2.5f) upper++; else street++;
                }
                before.Clear();
                before.UnionWith(now);
            }
        }
        Assert.Greater(spawned, 80);
        Assert.Greater(street, 20, "Enemies spawn in the streets.");
        Assert.Greater(upper, 3, "Enemies spawn on the upper floors too.");
    }

    [UnityTest]
    public IEnumerator EnemiesClimbToThePlayerAndJumpDownAfterThePlayer()
    {
        yield return EnterBattle();
        yield return Frames(6 * Fps);   // let a crowd form in the street

        // Up: the terrace is 6.7 m above the street and only stairs lead to it.
        Teleport(EastTerrace);
        var lastHeight = new Dictionary<EnemyAgent, float>();
        float highestStep = 0f;
        int arrived = 0, frames = 0;
        for (; frames < 30 * Fps && arrived < 5; frames++)
        {
            yield return null;
            arrived = 0;
            Vector3 player = _player.transform.position;
            foreach (EnemyAgent enemy in Active())
            {
                if (enemy.State == EnemyAgentState.Knockback || enemy.State == EnemyAgentState.Dying)
                    continue;
                Vector3 position = enemy.transform.position;
                if (lastHeight.TryGetValue(enemy, out float before) && position.y > before)
                    highestStep = Mathf.Max(highestStep, position.y - before);
                lastHeight[enemy] = position.y;
                if (Mathf.Abs(position.y - player.y) < 1f && Flat(position - player).magnitude < 3f)
                    arrived++;
            }
        }
        Assert.GreaterOrEqual(arrived, 5, "Enemies did not reach the player on the terrace within 30 s.");
        Assert.Less(highestStep, 0.12f, "Enemies walk up the stairs; nobody appears on an upper floor.");

        // Down: the player jumps into the alley. Enemies on the terrace and the stairs follow over the edge.
        int hitsBefore = Hits();
        Teleport(AlleyUnderTheTerrace);
        var fell = new HashSet<EnemyAgent>();
        lastHeight.Clear();
        arrived = 0;
        for (frames = 0; frames < 8 * Fps && (arrived < 8 || fell.Count < 5); frames++)
        {
            yield return null;
            arrived = 0;
            Vector3 player = _player.transform.position;
            foreach (EnemyAgent enemy in Active())
            {
                if (enemy.State == EnemyAgentState.Knockback || enemy.State == EnemyAgentState.Dying)
                    continue;
                Vector3 position = enemy.transform.position;
                if (lastHeight.TryGetValue(enemy, out float before) && before - position.y > 0.12f)
                    fell.Add(enemy);
                lastHeight[enemy] = position.y;
                if (position.y < 1f && Flat(position - player).magnitude < 4f)
                    arrived++;
            }
        }
        Assert.GreaterOrEqual(fell.Count, 5, "Enemies above the player should step off an open edge after the player.");
        Assert.GreaterOrEqual(arrived, 8, "Enemies did not close in again within 8 s of the jump.");
        Assert.GreaterOrEqual(Hits(), hitsBefore, "The round keeps counting.");
    }

    [UnityTest]
    public IEnumerator PlayerWalksUpTheNewStairsAndAcrossTheBridge()
    {
        yield return EnterBattle();
        _enemies.StopProducing();
        _keyboard = InputSystem.AddDevice<Keyboard>("CityTestKeyboard");
        yield return Frames(3);

        // The camera looks north at the start, so W walks north (+z), A west, D east.
        yield return Walk(new Vector3(3.75f, 0.3f, -13f), Key.W, 2.2f);
        Vector3 top = _player.transform.position;
        Assert.AreEqual(6.75f, top.y, 0.15f, "Up the alley stair to the second storey.");
        Assert.That(top.z, Is.InRange(0f, 2.8f), "Stopped by the railing at the end of the landing.");

        yield return Walk(new Vector3(-16.25f, 0.3f, -6.5f), Key.W, 2.4f);
        Assert.AreEqual(6.75f, _player.transform.position.y, 0.15f, "Up the stair on the west road.");

        yield return Walk(new Vector3(8f, 6.9f, 6.2f), Key.A, 1.6f);
        Vector3 across = _player.transform.position;
        Assert.Less(across.x, -5f, "Across the bridge to the west terrace.");
        Assert.AreEqual(6.75f, across.y, 0.15f);

        yield return Walk(new Vector3(3.75f, 3.6f, -5.5f), Key.A, 0.5f);
        Assert.Less(_player.transform.position.y, 0.3f, "Off the side of the half-way landing, down to the street.");
        Assert.IsTrue(_player.IsGrounded);
    }

    [UnityTest]
    public IEnumerator RetryResetsTheRoundInPlace()
    {
        Vector3 start = _player.transform.position;
        Scene scene = SceneManager.GetActiveScene();
        yield return EnterBattle();
        Teleport(EastTerrace);
        yield return Frames(4 * Fps);
        Assert.Greater(Active().Count, _enemies.Settings.InitialEnemyCount);

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);

        Assert.IsTrue(scene == SceneManager.GetActiveScene() && _enemies != null && _player != null, "Retry must not reload the scene.");
        Assert.AreEqual(1f, Time.timeScale);
        Assert.Less(Vector3.Distance(start, _player.transform.position), 0.05f, "The player is back at the start.");
        List<EnemyAgent> active = Active();
        Assert.AreEqual(_enemies.Settings.InitialEnemyCount, active.Count);
        foreach (EnemyAgent enemy in active)
        {
            Assert.AreEqual(EnemyAgentState.Idle, enemy.State);
            AssertStandsOnGround(enemy, "enemy after retry");
        }
        Assert.AreNotEqual(EnemyNavField.None, _enemies.Navigation.Goal, "The path points at the player's start again.");
    }

    // ---------------------------------------------------------------- helpers
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
        float timeout = Time.realtimeSinceStartup + 10f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private IEnumerator Walk(Vector3 from, Key key, float seconds)
    {
        Teleport(from);
        yield return Frames(30);
        Press(key, true);
        yield return Frames(Mathf.RoundToInt(seconds * Fps));
        Press(key, false);
        yield return Frames(45);
    }

    private void Press(Key key, bool down)
    {
        if (down) _keys.Add(key); else _keys.Remove(key);
        var keys = new Key[_keys.Count];
        _keys.CopyTo(keys);
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
    }

    private void Teleport(Vector3 position)
    {
        _controller.enabled = false;
        _player.transform.position = position;
        _controller.enabled = true;
        Physics.SyncTransforms();
    }

    private List<EnemyAgent> Active()
    {
        var active = new List<EnemyAgent>();
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            if (enemy != null && enemy.State != EnemyAgentState.Pooled)
                active.Add(enemy);
        return active;
    }

    private int Hits()
    {
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        return combat != null && combat.Stats != null ? combat.Stats.HitCount : 0;
    }

    private static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

    private void AssertStandsOnGround(EnemyAgent enemy, string who)
    {
        Vector3 position = enemy.transform.position;
        Assert.AreNotEqual(EnemyNavField.None, enemy.NavCell, $"{who} at {position} is not on walkable ground.");
        Assert.AreEqual(_enemies.Navigation.SampleHeight(enemy.NavCell, position), position.y, 0.05f, $"{who} at {position} is not standing on its ground.");
        Assert.IsFalse(Physics.CheckCapsule(position + Vector3.up * 0.9f, position + Vector3.up * 1.5f, 0.2f, Level, QueryTriggerInteraction.Ignore),
            $"{who} at {position} is inside level geometry.");
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Plays the real scene with simulated keyboard and mouse at a fixed 60 fps step and checks what a player feels:
// how fast the character answers, whether early presses are kept, whether the crowd walls the player in, where the
// camera goes next to a wall, and whether enemies really move at their configured speed.
public sealed class GameFeelSceneTests
{
    private const int Fps = 60;
    private readonly HashSet<Key> _keys = new();
    private Keyboard _keyboard;
    private Mouse _mouse;
    private int _oldCaptureFramerate;
    private InputSettings.BackgroundBehavior _oldBackground;
#if UNITY_EDITOR
    private InputSettings.EditorInputBehaviorInPlayMode _oldEditorBehavior;
#endif
    private GameStateManager _state;
    private EventManager _events;
    private PlayerRuntime _player;
    private EnemyRuntime _enemies;
    private Animator _animator;
    private CharacterController _controller;
    private Camera _camera;

    [UnitySetUp]
    public IEnumerator EnterBattle()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = Fps;
        // Tests run without window focus; simulated devices must still reach the game.
        InputSettings settings = InputSystem.settings;
        _oldBackground = settings.backgroundBehavior;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        _oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _animator = _player.GetComponent<Animator>();
        _controller = _player.GetComponent<CharacterController>();
        _camera = Camera.main;
        yield return WaitFor(GameState.Ready);
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        _keyboard = InputSystem.AddDevice<Keyboard>("GameFeelTestKeyboard");
        _mouse = InputSystem.AddDevice<Mouse>("GameFeelTestMouse");
        _keys.Clear();
        yield return Frames(3);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
        if (_mouse != null) InputSystem.RemoveDevice(_mouse);
        _keyboard = null;
        _mouse = null;
        InputSettings settings = InputSystem.settings;
        settings.backgroundBehavior = _oldBackground;
#if UNITY_EDITOR
        settings.editorInputBehaviorInPlayMode = _oldEditorBehavior;
#endif
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerStartsStopsAndLandsWithinFrames()
    {
        yield return ClearEnemies();
        Vector3 ground = _player.transform.position;

        Hold(Key.W, true);
        yield return Frames(10);
        Assert.GreaterOrEqual(_player.PlanarSpeed, 9f, "Ten frames after the key the character must be at run speed.");
        Vector3 atRelease = _player.transform.position;
        Assert.Greater(Planar(atRelease - ground).magnitude, 0.7f);
        Hold(Key.W, false);
        yield return Frames(8);
        Assert.AreEqual(0f, _player.PlanarSpeed, 0.01f, "Eight frames after release the character must have stopped.");
        Assert.Less(Planar(_player.transform.position - atRelease).magnitude, 0.8f, "It must not slide on after release.");

        // A short tap still moves a useful distance.
        Vector3 beforeTap = _player.transform.position;
        Hold(Key.D, true);
        yield return Frames(6);
        Hold(Key.D, false);
        yield return Frames(20);
        Assert.Greater(Planar(_player.transform.position - beforeTap).magnitude, 0.5f, "A 0.1 s tap must move at least half a metre.");

        // Lifted off the floor, the character comes back down by itself.
        float floorHeight = _player.transform.position.y;
        Teleport(_player.transform.position + Vector3.up * 2f);
        yield return Frames(45);
        Assert.AreEqual(floorHeight, _player.transform.position.y, 0.05f, "The character must fall back to the floor.");
        Assert.IsTrue(_player.IsGrounded);
    }

    [UnityTest]
    public IEnumerator EarlyAttackPressesAreKeptForTheCombo()
    {
        yield return ClearEnemies();
        Vector3 start = _player.transform.position;
        // Second click 0.15 s, 0.3 s and 0.45 s after the first: all before the combo window opens.
        foreach (int gap in new[] { 9, 18, 27 })
        {
            Teleport(start);
            yield return WaitUntilIdle();
            yield return Click();
            bool firstAttack = false;
            for (int i = 0; i < gap - 2; i++)
            {
                yield return null;
                firstAttack |= Entering("Attack1");
                Assert.IsFalse(Entering("Attack2"), "The second attack must not start before the second click.");
            }
            Assert.IsTrue(firstAttack, "The first click must start Attack1.");
            yield return Click();
            bool chained = false;
            for (int i = 0; i < 60 && !chained; i++)
            {
                yield return null;
                chained = Entering("Attack2");
            }
            Assert.IsTrue(chained, "A second click " + (gap * 1000 / Fps) + " ms after the first must be kept and chained into Attack2.");
        }
    }

    [UnityTest]
    public IEnumerator PlayerRunsAndDodgesThroughTheCrowd()
    {
        // Stand still until enemies have closed in around the player.
        int near = 0;
        for (int frame = 0; frame < 20 * Fps && near < 6; frame++)
        {
            yield return null;
            if (frame % 15 == 0) near = CountEnemiesWithin(3f);
        }
        Assert.GreaterOrEqual(near, 6, "The crowd must have reached the player for this test to mean anything.");

        Vector3 start = _player.transform.position;
        Hold(Key.W, true);
        yield return Frames(45);
        Hold(Key.W, false);
        Assert.Greater(Planar(_player.transform.position - start).magnitude, 5f, "Surrounded, the player must still be able to run out (it used to move 0.9 m in this time).");
        yield return Frames(30);

        Vector3 beforeDodge = _player.transform.position;
        Hold(Key.S, true);
        Hold(Key.LeftShift, true);
        yield return Frames(3);
        Hold(Key.LeftShift, false);
        yield return Frames(60);
        Hold(Key.S, false);
        Assert.Greater(Planar(_player.transform.position - beforeDodge).magnitude, 4f, "A dodge into the crowd must cover its distance (it used to go nowhere).");
    }

    [UnityTest]
    public IEnumerator AttackLungeStopsInFrontOfTheEnemyAndHitsIt()
    {
        // The player walks through the crowd now, but an attack must not carry the player past the enemy it is aimed at.
        _enemies.StopProducing();   // enemies stay where they are
        yield return Frames(2);
        EnemyAgent target = null;
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (target == null) target = enemy;
            else _enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick);
        Assert.IsNotNull(target);
        yield return Frames(150);

        Vector3 enemyPosition = target.transform.position;
        Vector3 toEnemy = Planar(enemyPosition - _player.transform.position).normalized;
        Teleport(new Vector3(enemyPosition.x, _player.transform.position.y, enemyPosition.z) - toEnemy * 2.6f);
        _player.transform.rotation = Quaternion.LookRotation(toEnemy);
        yield return Frames(20);
        int killsBefore = Object.FindFirstObjectByType<CombatRoundRuntime>().Stats.KillCount;

        yield return Click();
        float closest = float.MaxValue;
        for (int i = 0; i < 40; i++)
        {
            yield return null;
            closest = Mathf.Min(closest, Planar(enemyPosition - _player.transform.position).magnitude);
        }
        Assert.Greater(closest, 0.9f, "The 3 m lunge must stop in front of an enemy standing 2.6 m away, not pass through it.");
        Assert.Less(closest, 2.2f, "The lunge must still close the distance.");
        Assert.AreEqual(killsBefore + 1, Object.FindFirstObjectByType<CombatRoundRuntime>().Stats.KillCount, "The enemy it stopped in front of must be hit.");
    }

    // The camera used to keep its minimum distance even with a wall closer than that: it ended up inside the wall.
    [UnityTest]
    public IEnumerator WithAWallRightBehindThePlayerTheCameraStaysInFrontOfItAndThePlayerIsNotDrawn()
    {
        yield return ClearEnemies();
        Vector3 origin = _player.transform.position;
        Assert.IsTrue(Physics.Raycast(origin + Vector3.up, Vector3.left, out RaycastHit wall, 100f, 1 << 0, QueryTriggerInteraction.Ignore), "The arena must have a west wall.");
        Assert.IsFalse(_player.HiddenForCamera, "In the open the player is drawn.");
        // The player's back against the wall.
        Teleport(new Vector3(wall.point.x + _controller.radius + 0.05f, origin.y, wall.point.z));
        // 450 px of mouse travel = 90 degrees: the camera now sits toward the wall, behind the player.
        InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(450f, 0f) });
        yield return Frames(30);

        Vector3 head = _player.transform.position + Vector3.up * 1.5f;
        float distance = Vector3.Distance(head, _camera.transform.position);
        Assert.Less(distance, 0.7f, "The wall is closer than the camera used to come.");
        Assert.IsFalse(Physics.Linecast(head, _camera.transform.position, 1 << 0, QueryTriggerInteraction.Ignore), "The camera must stay on the player's side of the wall.");
        Assert.IsTrue(_player.HiddenForCamera, "This close the camera would look into the player's own model.");
        foreach (Renderer renderer in _player.GetComponentsInChildren<Renderer>(true))
            Assert.IsTrue(renderer.forceRenderingOff, renderer.name + " is still drawn.");

        // Stepping away from the wall: the camera backs out and the player is drawn again.
        Teleport(origin);
        yield return Frames(90);
        Assert.IsFalse(_player.HiddenForCamera);
        foreach (Renderer renderer in _player.GetComponentsInChildren<Renderer>(true))
            Assert.IsFalse(renderer.forceRenderingOff, renderer.name + " stayed hidden.");
        Assert.Greater(Vector3.Distance(_player.transform.position + Vector3.up * 1.5f, _camera.transform.position), 2f);
    }

    [UnityTest]
    public IEnumerator CameraPullsInNextToAWall()
    {
        yield return ClearEnemies();
        Vector3 origin = _player.transform.position;
        Assert.IsTrue(Physics.Raycast(origin + Vector3.up, Vector3.left, out RaycastHit wall, 100f, 1 << 0, QueryTriggerInteraction.Ignore), "The arena must have a west wall.");
        Teleport(new Vector3(wall.point.x + 1f, origin.y, wall.point.z));
        // 450 px of mouse travel = 90 degrees: the camera now sits toward the wall, behind the player.
        var look = new MouseState { delta = new Vector2(450f, 0f) };
        InputSystem.QueueStateEvent(_mouse, look);
        yield return Frames(30);   // the teleport above is not a real move; let the followed point arrive

        Vector3 head = _player.transform.position + Vector3.up * 1.5f;
        float distance = Vector3.Distance(head, _camera.transform.position);
        Assert.Less(distance, 2f, "With a wall one metre behind the player, the camera must pull in.");
        Assert.IsFalse(Physics.Linecast(head, _camera.transform.position, 1 << 0, QueryTriggerInteraction.Ignore), "No level geometry may stand between the player and the camera.");

        // Away from the wall it eases back out to its normal distance.
        Teleport(origin);
        yield return Frames(90);
        Assert.Greater(Vector3.Distance(_player.transform.position + Vector3.up * 1.5f, _camera.transform.position), 3.5f);
    }

    [UnityTest]
    public IEnumerator EnemiesAdvanceEveryFrameAtTheirConfiguredSpeed()
    {
        yield return Frames(120);   // let them get up to speed
        EnemySettings settings = _enemies.Settings;
        var tracked = new List<EnemyAgent>();
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (enemy.State == EnemyAgentState.Chase && Planar(enemy.transform.position - _player.transform.position).magnitude > 12f)
                tracked.Add(enemy);
        Assert.GreaterOrEqual(tracked.Count, 3, "Enemies must be on their way.");

        var last = new Vector3[tracked.Count];
        var travelled = new float[tracked.Count];
        var stalled = new int[tracked.Count];
        for (int i = 0; i < tracked.Count; i++) last[i] = tracked[i].transform.position;
        const int frames = 30;
        for (int frame = 0; frame < frames; frame++)
        {
            yield return null;
            for (int i = 0; i < tracked.Count; i++)
            {
                float step = Planar(tracked[i].transform.position - last[i]).magnitude;
                travelled[i] += step;
                if (step < 0.01f) stalled[i]++;
                last[i] = tracked[i].transform.position;
            }
        }

        float slowest = settings.MoveSpeed * (1f - settings.SpeedVariance) * 0.9f;
        float fastest = settings.MoveSpeed * (1f + settings.SpeedVariance) * 1.05f;
        for (int i = 0; i < tracked.Count; i++)
        {
            if (tracked[i].State != EnemyAgentState.Chase) continue;   // reached the player or was hit meanwhile
            float speed = travelled[i] * Fps / frames;
            Assert.AreEqual(0, stalled[i], "A chasing enemy must move every rendered frame (it used to move in two frames out of six).");
            Assert.That(speed, Is.InRange(slowest, fastest), "A chasing enemy must travel at the configured speed (it used to manage about a third of it).");
            Assert.IsTrue(tracked[i].ShowsLocomotion);
        }
    }

    [UnityTest]
    public IEnumerator EnemiesSpawnWithAClearPathAndNeverRunOnTheSpot()
    {
        // Twelve seconds of standing still: waves keep spawning and the first ones settle around the player.
        int checkedSpawns = 0, blocked = 0, standing = 0;
        var seen = new HashSet<EnemyAgent>();
        var lastPosition = new Dictionary<EnemyAgent, Vector3>();
        for (int frame = 0; frame < 12 * Fps; frame++)
        {
            yield return null;
            Vector3 chest = _player.transform.position + Vector3.up;
            foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            {
                Vector3 position = enemy.transform.position;
                if (seen.Add(enemy))
                {
                    checkedSpawns++;
                    if (Physics.Linecast(chest, position + Vector3.up, 1 << 0, QueryTriggerInteraction.Ignore)) blocked++;
                }
                else if (lastPosition.TryGetValue(enemy, out Vector3 previous) &&
                         (enemy.State == EnemyAgentState.Idle || enemy.State == EnemyAgentState.Chase) &&
                         Planar(position - previous).magnitude * Fps < 0.05f)
                {
                    standing++;
                    Assert.IsFalse(enemy.ShowsLocomotion && enemy.PlanarVelocity.sqrMagnitude < 0.0001f,
                        "An enemy that stands still must not play a walk or run cycle (state " + enemy.State + ").");
                }
                lastPosition[enemy] = position;
            }
        }
        Assert.Greater(checkedSpawns, 30);
        Assert.LessOrEqual(blocked, checkedSpawns / 20, "Enemies must spawn where a straight line to the player is free of walls.");
        Assert.Greater(standing, 100, "Some enemies must have arrived and be waiting for this test to mean anything.");
    }

    // ---------------------------------------------------------------- helpers
    private static Vector3 Planar(Vector3 value) => new Vector3(value.x, 0f, value.z);

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator WaitFor(GameState target)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (_state.CurrentState != target && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.AreEqual(target, _state.CurrentState);
    }

    private IEnumerator WaitUntilIdle()
    {
        for (int i = 0; i < 300; i++)
        {
            yield return null;
            if (i > 5 && _animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && !_animator.IsInTransition(0)) yield break;
        }
        Assert.Fail("The player did not return to Idle.");
    }

    // The state the player is in or blending into.
    private bool Entering(string stateName)
    {
        if (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsName(stateName)) return true;
        return _animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
    }

    private void Hold(Key key, bool down)
    {
        if (down) _keys.Add(key); else _keys.Remove(key);
        var pressed = new Key[_keys.Count];
        _keys.CopyTo(pressed);
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(pressed));
    }

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left, true));
        yield return Frames(2);
        InputSystem.QueueStateEvent(_mouse, new MouseState());
    }

    private void Teleport(Vector3 position)
    {
        _controller.enabled = false;
        _player.transform.position = position;
        _controller.enabled = true;
        Physics.SyncTransforms();
    }

    // Stops spawning and AI, then removes the enemies already there, so the player can be measured alone.
    private IEnumerator ClearEnemies()
    {
        _enemies.StopProducing();
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            _enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick);
        yield return Frames(150);
        Assert.AreEqual(0, Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None).Length);
    }

    private int CountEnemiesWithin(float radius)
    {
        int count = 0;
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (Planar(enemy.transform.position - _player.transform.position).magnitude <= radius) count++;
        return count;
    }
}

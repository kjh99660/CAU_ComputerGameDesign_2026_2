using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// How long each attack keeps the player from running away. The real scene is played with simulated keyboard and
// mouse at a fixed 60 fps step: attack, hold W from the first frame of the attack and count the frames until the
// player is running. Also checks that pressing attack while holding W still chains the combo.
// The limits follow cancelTime in Assets/Data/Attacks (Attack1 0.44 s, Attack2 0.53 s, ChargeAttack 0.34 s). Moving
// may start before the combo windows of Attack1 and Attack2 open (0.5 s and 0.61 s): a pending attack press holds
// the move back and waits for the next hit, so mashing while holding W still chains the combo.
public sealed class AttackRecoverySceneTests
{
    private const int Fps = 60;
    // When each attack's hit ends (HitEnd event time divided by the state speed): running must not cut the hit.
    private const float Attack1HitEnd = 0.2f / 0.7f;
    private const float Attack2HitEnd = 0.25f / 0.7f;
    private const float ChargeHitEnd = 0.1f / 1.5f;
    // Three frames: the state is noticed one frame late and the blend into the run takes a frame or two.
    private const float Slack = 3f / Fps;
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

    [UnitySetUp]
    public IEnumerator EnterBattle()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = Fps;
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
        yield return WaitFor(GameState.Ready);
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        _keyboard = InputSystem.AddDevice<Keyboard>("RecoveryTestKeyboard");
        _mouse = InputSystem.AddDevice<Mouse>("RecoveryTestMouse");
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
    public IEnumerator EachAttackLetsThePlayerRunAwaySoonAfterItsHit()
    {
        yield return ClearEnemies();
        var results = new List<string>();
        Vector3 start = _player.transform.position;

        // 1st hit alone.
        Teleport(start);
        yield return WaitUntilIdle();
        yield return Click();
        yield return WaitUntilEntering("Attack1");
        yield return MeasureRunAway("Attack1", Attack1HitEnd, 0.44f + Slack, results);

        // 2nd hit: the second click is pressed early and kept for the combo.
        Teleport(start);
        yield return WaitUntilIdle();
        yield return Click();
        yield return WaitUntilEntering("Attack1");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEntering("Attack2");
        yield return MeasureRunAway("Attack2", Attack2HitEnd, 0.53f + Slack, results);

        // 3rd press: a quick click after the 2nd hit releases the charge attack at once.
        Teleport(start);
        yield return WaitUntilIdle();
        yield return Click();
        yield return WaitUntilEntering("Attack1");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEntering("Attack2");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEntering("Charge_Release");
        yield return MeasureRunAway("Charge_Release", ChargeHitEnd, 0.34f + Slack, results);

        Debug.Log("[Recovery] " + string.Join(" | ", results));
    }

    [UnityTest]
    public IEnumerator HoldingAMoveKeyWhilePressingAttackKeepsTheCombo()
    {
        yield return ClearEnemies();
        yield return WaitUntilIdle();
        Hold(Key.W, true);
        yield return Frames(10);
        yield return Click();
        yield return WaitUntilEntering("Attack1");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEnteringWithoutRunning("Attack2");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEnteringWithoutRunning("Charge_Release");
        // With W still held, the player runs off once the last attack allows it.
        int frames = 0;
        while (frames < 120 && !Logical().IsTag("RunMotion"))
        {
            yield return null;
            frames++;
        }
        Hold(Key.W, false);
        Assert.Less(frames, 120, "The player runs after the combo.");
    }

    // "Click, click, press and hold" makes the charge attack, and the hold often begins while the second hit has
    // barely started. A press is remembered for 0.4 s, the second hit's combo window opens 0.61 s in, and a held
    // button is never pressed again: the player stood there holding the button and nothing came. A press now stays
    // for as long as its button is held and the window is still ahead.
    [UnityTest]
    public IEnumerator AnEarlyThirdPressThatIsHeldStillChargesTheAttack()
    {
        yield return ClearEnemies();
        yield return WaitUntilIdle();

        // One press is still one step: holding the first click does not run on into the second hit.
        InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left, true));
        yield return WaitUntilEntering("Attack1");
        for (int i = 0; i < 100; i++)
        {
            Assert.IsFalse(Logical().IsName("Attack2"), "Holding the first click must not chain the combo.");
            yield return null;
        }
        InputSystem.QueueStateEvent(_mouse, new MouseState());
        yield return WaitUntilIdle();

        yield return Click();
        yield return WaitUntilEntering("Attack1");
        yield return Frames(15);
        yield return Click();
        yield return WaitUntilEntering("Attack2");
        yield return Frames(3);   // 0.05 s into the second hit: more than half a second before its window
        InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left, true));
        yield return WaitUntilEntering("Charge_Hold");
        yield return Frames(20);
        Assert.IsTrue(Logical().IsName("Charge_Hold"), "The charge is held for as long as the button is.");
        InputSystem.QueueStateEvent(_mouse, new MouseState());
        yield return WaitUntilEntering("Charge_Release");
    }

    // From the frame the attack starts, W is held; counts the frames until the player is running.
    private IEnumerator MeasureRunAway(string attack, float hitEnds, float latest, List<string> results)
    {
        AnimatorStateInfo info = Logical();
        float stateLength = info.length;
        Hold(Key.W, true);
        int frames = 0;
        while (frames < 300 && !(Logical().IsTag("RunMotion")))
        {
            yield return null;
            frames++;
        }
        Hold(Key.W, false);
        Assert.Less(frames, 300, attack + ": the player never ran.");
        float seconds = frames / (float)Fps;
        results.Add(attack + " stateLength=" + stateLength.ToString("F3") + "s runAfter=" + seconds.ToString("F3") + "s");
        Debug.Log("[Recovery] " + results[results.Count - 1]);
        Assert.GreaterOrEqual(seconds, hitEnds, attack + ": running must not cut the hit short.");
        Assert.LessOrEqual(seconds, latest, attack + ": the player must be able to run soon after the hit.");
        yield return Frames(20);
    }

    private IEnumerator WaitUntilEnteringWithoutRunning(string stateName)
    {
        for (int i = 0; i < 120; i++)
        {
            AnimatorStateInfo state = Logical();
            if (state.IsName(stateName)) yield break;
            Assert.IsFalse(state.IsTag("RunMotion"), "Pressing attack while holding W must keep the combo, not run off.");
            yield return null;
        }
        Assert.Fail("Never entered " + stateName);
    }

    private AnimatorStateInfo Logical() =>
        _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0) : _animator.GetCurrentAnimatorStateInfo(0);

    private IEnumerator WaitUntilEntering(string stateName)
    {
        for (int i = 0; i < 300; i++)
        {
            if (Logical().IsName(stateName)) yield break;
            yield return null;
        }
        Assert.Fail("Never entered " + stateName);
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

    private IEnumerator ClearEnemies()
    {
        _enemies.StopProducing();
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            _enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick);
        yield return Frames(150);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator WaitFor(GameState target)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != target && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(target, _state.CurrentState);
    }
}

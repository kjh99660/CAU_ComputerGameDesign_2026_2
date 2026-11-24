using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Hit feel in the real scene: the hit stop (and that time always comes back), the camera shake and kick, the white
// flash on a struck enemy, and what a destroyed enemy leaves behind (a body eaten away, or parts that fly apart and
// land on the floor). Each must hold still in Pause and be gone after Retry.
public sealed class HitFeelSceneTests
{
    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private PlayerRuntime _player;
    private Animator _animator;
    private CombatVfxSettings _settings;
    private int _oldCaptureFramerate;
    private float _oldBreakChance;

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
        _settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        _oldBreakChance = _settings.lightKillBreakChance;
    }

    [TearDown]
    public void TearDown()
    {
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        _settings.lightKillBreakChance = _oldBreakChance;
    }

    [UnityTest]
    public IEnumerator AHitHoldsTheRoundBrieflyAndTimeAlwaysComesBack()
    {
        yield return Load();
        yield return EnterBattle();
        Assert.IsTrue(_enemies.TryApplyHit(Enemy(), Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(_settings.hitStopTimeScale, Time.timeScale, 0.0001f, "A landed blow holds the round.");
        Assert.AreEqual(_settings.hitStopLight, _combat.HitStopRemaining, 0.0001f, "The first hit is light.");
        yield return UntilReal(() => _combat.HitStopRemaining <= 0f, 1f);
        Assert.AreEqual(0f, _combat.HitStopRemaining);
        Assert.AreEqual(1f, Time.timeScale, "The hit stop ends by itself.");

        // The round clock keeps real time through a hit stop, so hit stops do not lengthen the round. Frames are
        // captured at 60 fps here, so a 0.25 s stop lasts 15 frames and takes 0.25 s off the clock.
        _combat.RequestHitStop(0.25f, 0.05f);
        float clock = _combat.RoundRemaining;
        int frames = 0;
        while (_combat.HitStopRemaining > 0f && frames < 60)
        {
            yield return null;
            frames++;
        }
        Assert.AreEqual(15, frames, 1, "A hit stop lasts its time in captured frames too.");
        Assert.AreEqual(frames / 60f, clock - _combat.RoundRemaining, 1.5f / 60f, "Hit stops do not lengthen the round.");

        // A charged blow holds longer.
        _animator.Play("Charge_Release", 0, 0f);
        yield return null;
        Assert.IsTrue(_enemies.TryApplyHit(Enemy(), Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(_settings.hitStopHeavy, _combat.HitStopRemaining, 0.0001f);

        // Pause in the middle of a hit stop: time is normal again (Pause does not rely on the time scale).
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.AreEqual(1f, Time.timeScale);
        Assert.AreEqual(0f, _combat.HitStopRemaining);
        _combat.RequestHitStop(0.2f, 0.05f);
        Assert.AreEqual(1f, Time.timeScale, "No hit stop outside Battle.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        // The round ends in the middle of a hit stop: Finishing keeps its own slow motion.
        _combat.RequestHitStop(0.2f, 0.05f);
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        Assert.AreEqual(0.5f, Time.timeScale, 0.0001f);
        _combat.RequestHitStop(0.2f, 0.05f);
        Assert.AreEqual(0.5f, Time.timeScale, 0.0001f, "No hit stop in Finishing.");
    }

    [UnityTest]
    public IEnumerator RetryInTheMiddleOfAHitStopPutsTimeBack()
    {
        yield return Load();
        yield return EnterBattle();
        _combat.RequestHitStop(0.25f, 0.05f);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        _combat.RequestHitStop(0.25f, 0.05f);
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(1f, Time.timeScale);
        Assert.AreEqual(0f, _combat.HitStopRemaining);
    }

    [UnityTest]
    public IEnumerator HeavyBlowsShakeTheCameraWhichComesBackToItsPlace()
    {
        yield return Load();
        yield return EnterBattle();
        // No enemy attacks during this test: a hit on the player would shake the view as well.
        _enemies.StopProducing();
        var feedback = Object.FindFirstObjectByType<CombatCameraFeedback>();

        // A light hit does not shake (it kicks a little); a charged one does.
        Assert.IsTrue(_enemies.TryApplyHit(Enemy(), Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(0f, feedback.Trauma, "Light hits do not shake the view.");
        yield return Frames(40);
        _animator.Play("Charge_Release", 0, 0f);
        yield return null;
        Assert.IsTrue(_enemies.TryApplyHit(Enemy(), Vector3.forward, 1f, _state.Tick));
        Assert.Greater(feedback.Trauma, 0.3f);
        float largest = 0f;
        for (int i = 0; i < 6; i++)
        {
            yield return null;
            largest = Mathf.Max(largest, feedback.ShakeOffset.magnitude);
        }
        Assert.IsTrue(feedback.IsShaking);
        Assert.Greater(largest, 0.005f, "The view moves.");

        // Paused: the shake holds exactly where it was.
        feedback.AddTrauma(0.5f);
        yield return null;
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Vector3 pausedOffset = feedback.ShakeOffset;
        float pausedTrauma = feedback.Trauma;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.AreEqual(pausedOffset, feedback.ShakeOffset);
        Assert.AreEqual(pausedTrauma, feedback.Trauma, "Paused: the shake does not run down either.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        yield return UntilReal(() => !feedback.IsShaking, 4f);
        Assert.IsFalse(feedback.IsShaking, "The shake dies out.");
        Assert.AreEqual(0f, feedback.Trauma);
        Assert.AreEqual(Vector3.zero, feedback.ShakeOffset, "The camera is back where the player's camera put it.");

        // Retry leaves no shake behind.
        feedback.AddTrauma(1f);
        yield return null;
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0f, feedback.Trauma);
        Assert.IsFalse(feedback.IsShaking);
    }

    [UnityTest]
    public IEnumerator AStruckEnemyFlashesAndKeepsItsTint()
    {
        yield return Load();
        yield return EnterBattle();
        var impact = Object.FindFirstObjectByType<CombatImpactFeedback>();
        EnemyAgent enemy = Enemy();
        Renderer skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
        var block = new MaterialPropertyBlock();

        Assert.IsTrue(_enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(1, impact.ActiveFlashes);
        skin.GetPropertyBlock(block);
        Assert.AreEqual(1f, block.GetFloat("_HitFlash"), 0.0001f);
        // Writing the flash must not wipe the tint the enemy keeps in the same property block.
        Assert.Greater(block.GetColor("_BaseColor").maxColorComponent, 0f, "The flash keeps the enemy's tint.");
        yield return UntilReal(() => impact.ActiveFlashes == 0, 3f);
        Assert.AreEqual(0, impact.ActiveFlashes);
        skin.GetPropertyBlock(block);
        Assert.AreEqual(0f, block.GetFloat("_HitFlash"), 0.0001f);
    }

    [UnityTest]
    public IEnumerator ANormalKillLeavesABodyThatIsEatenAway()
    {
        yield return Load();
        yield return EnterBattle();
        var remains = Object.FindFirstObjectByType<EnemyRemainsRuntime>();
        Assert.Greater(remains.BodyPoolSize, 0, "Bodies are pooled at start-up.");
        _settings.lightKillBreakChance = 0f;
        _enemies.StopProducing();
        EnemyAgent enemy = Enemy();
        Assert.IsTrue(_enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(0, remains.ActivePieces, "A light kill does not break apart.");
        yield return UntilReal(() => remains.ActiveBodies > 0, 8f);
        Assert.AreEqual(1, remains.ActiveBodies, "When its death has played out the enemy leaves a body.");
        yield return UntilReal(() => remains.ActiveBodies == 0, 8f);
        Assert.AreEqual(0, remains.ActiveBodies, "The body is eaten away.");
    }

    [UnityTest]
    public IEnumerator AHeavyBlowBreaksTheEnemyIntoPartsThatLandAndVanish()
    {
        yield return Load();
        yield return EnterBattle();
        var remains = Object.FindFirstObjectByType<EnemyRemainsRuntime>();
        Assert.Greater(remains.PartsPerEnemy, 5, "The robot breaks into head, chest, arms and legs.");
        _enemies.StopProducing();
        EnemyAgent enemy = Enemy();
        Renderer skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
        float floor = enemy.transform.position.y;
        _animator.Play("Charge_Release", 0, 0f);
        yield return null;
        Assert.IsTrue(_enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(remains.PartsPerEnemy, remains.ActivePieces);
        Assert.AreEqual(1, remains.HiddenEnemies);
        Assert.IsTrue(skin.forceRenderingOff, "The broken enemy is hidden while its parts fly.");

        // Paused: the parts hang where they are.
        yield return Frames(6);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        int first = FirstActivePiece(remains);
        Assert.IsTrue(remains.TryGetPiece(first, out Vector3 held, out _));
        yield return new WaitForSecondsRealtime(0.1f);
        remains.TryGetPiece(first, out Vector3 still, out _);
        Assert.AreEqual(held, still, "Pause holds the parts.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        // They come down and rest on the floor, then are eaten away; the enemy is visible again once pooled.
        bool rested = false;
        float until = Time.realtimeSinceStartup + 4f;
        while (!rested && Time.realtimeSinceStartup < until)
        {
            yield return null;
            rested = remains.TryGetPiece(first, out Vector3 at, out bool resting) && resting && Mathf.Abs(at.y - floor) < 1.2f;
        }
        Assert.IsTrue(rested, "A part lands on the floor.");
        yield return UntilReal(() => remains.ActivePieces == 0, 12f);
        Assert.AreEqual(0, remains.ActivePieces, "The parts are eaten away.");
        yield return UntilReal(() => remains.HiddenEnemies == 0, 8f);
        Assert.AreEqual(0, remains.HiddenEnemies);
        Assert.IsFalse(skin.forceRenderingOff, "Pooled again, the enemy can be seen.");
    }

    [UnityTest]
    public IEnumerator BrokenPartsComeApartOnlyALittleAndSomeKeepAFaintGlowOrCrackle()
    {
        yield return Load();
        yield return EnterBattle();
        var remains = Object.FindFirstObjectByType<EnemyRemainsRuntime>();
        _enemies.StopProducing();
        _combat.HitStopsEnabled = false;
        _animator.Play("Charge_Release", 0, 0f);
        yield return null;
        for (int i = 0; i < 3; i++)
            Assert.IsTrue(_enemies.TryApplyHit(Enemy(), Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(3 * remains.PartsPerEnemy, remains.ActivePieces, "Three heavy blows, three broken enemies.");
        var start = new Vector3[remains.PiecePoolSize];
        var used = new bool[remains.PiecePoolSize];
        for (int i = 0; i < remains.PiecePoolSize; i++)
        {
            if (!remains.TryGetPiece(i, out start[i], out _)) continue;
            used[i] = true;
            Assert.IsTrue(remains.TryGetPieceLight(i, out _, out float light, out _));
            Assert.AreEqual(1f, light, 0.0001f, "The parts fly off as bright as the enemy was.");
        }

        // A second of game time: they have landed close to where their robot stood and dimmed.
        yield return Frames(60);
        float farthest = 0f, total = 0f, ceiling = _settings.remainsAfterglow * RemainsLight.CrackleBoost + 0.0001f;
        int parts = 0, glowing = 0;
        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i]) continue;
            Assert.IsTrue(remains.TryGetPiece(i, out Vector3 at, out _));
            Vector3 moved = at - start[i];
            moved.y = 0f;
            farthest = Mathf.Max(farthest, moved.magnitude);
            total += moved.magnitude;
            parts++;
            Assert.IsTrue(remains.TryGetPieceLight(i, out RemainsLight.Kind kind, out float light, out _));
            Assert.LessOrEqual(light, ceiling, "What is kept is only a faint glow.");
            if (kind == RemainsLight.Kind.Dark) Assert.AreEqual(0f, light, 0.0001f);
            if (light > 0.01f) glowing++;
        }
        Assert.Greater(farthest, 0.3f, "The robot does come apart.");
        Assert.Less(farthest, 3f, "No part flies across the arena.");
        Assert.Less(total / parts, 1.6f, "The parts stay close to where it stood.");
        Assert.Greater(glowing, 0, "Some parts keep a faint glow.");

        // Some crackle with sparks now and then.
        bool sparked = false;
        for (int frame = 0; frame < 90 && !sparked; frame++)
        {
            yield return null;
            for (int i = 0; i < used.Length && !sparked; i++)
                sparked = used[i] && remains.TryGetPieceLight(i, out _, out _, out bool sparking) && sparking;
        }
        Assert.IsTrue(sparked, "Some parts crackle.");

        // Paused, the lights hold.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        var held = new float[used.Length];
        for (int i = 0; i < used.Length; i++)
            if (used[i]) remains.TryGetPieceLight(i, out _, out held[i], out _);
        yield return new WaitForSecondsRealtime(0.1f);
        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i]) continue;
            remains.TryGetPieceLight(i, out _, out float now, out _);
            Assert.AreEqual(held[i], now, "Pause holds the lights.");
        }
    }

    [UnityTest]
    public IEnumerator RetryClearsBodiesAndPartsAndShowsBrokenEnemiesAgain()
    {
        yield return Load();
        yield return EnterBattle();
        var remains = Object.FindFirstObjectByType<EnemyRemainsRuntime>();
        _enemies.StopProducing();
        EnemyAgent broken = Enemy();
        Renderer skin = broken.GetComponentInChildren<SkinnedMeshRenderer>();
        _animator.Play("Charge_Release", 0, 0f);
        yield return null;
        Assert.IsTrue(_enemies.TryApplyHit(broken, Vector3.forward, 1f, _state.Tick));
        Assert.Greater(remains.ActivePieces, 0);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, remains.ActivePieces);
        Assert.AreEqual(0, remains.ActiveBodies);
        Assert.AreEqual(0, remains.HiddenEnemies);
        Assert.IsFalse(skin.forceRenderingOff, "An enemy hidden by a break is visible again after Retry.");
    }

    // Waits on real time, so it holds whether test frames run faster or slower than the captured 60 fps.
    private static IEnumerator UntilReal(System.Func<bool> done, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < until) yield return null;
    }

    private static int FirstActivePiece(EnemyRemainsRuntime remains)
    {
        for (int i = 0; i < remains.PiecePoolSize; i++)
            if (remains.TryGetPiece(i, out _, out _)) return i;
        return -1;
    }

    private EnemyAgent Enemy()
    {
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID))
            if (enemy.State != EnemyAgentState.Dying && enemy.State != EnemyAgentState.Knockback && enemy.State != EnemyAgentState.Pooled)
                return enemy;
        Assert.Fail("No standing enemy left.");
        return null;
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
        _animator = _player.GetComponent<Animator>();
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

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }
}

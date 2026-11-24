using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The two robot kinds in the real scene (2026-11-25 request): the round starts with blue robots that one hit destroys;
// a red robot takes two hits, scoring the first as a hit only, and the special destroys it at once.
public sealed class EnemyKindsSceneTests
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private readonly List<Object> _made = new List<Object>();
    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private PlayerRuntime _player;
    private int _oldCaptureFramerate;

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
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
    public IEnumerator TheRoundStartsWithBlueRobotsThatOneHitDestroys()
    {
        yield return Load();
        EnemySettings settings = _enemies.Settings;
        IReadOnlyList<EnemyAgent> ready = _enemies.Perf.ActiveEnemies;
        Assert.Greater(ready.Count, 0, "Ready shows the first robots.");
        foreach (EnemyAgent enemy in ready)
        {
            Assert.IsFalse(enemy.Tough, "The first robots are the weak kind.");
            AssertColor(settings.WeakColor, ShownColor(enemy), "...in the blue skin.");
        }
        Assert.Greater(settings.WeakColor.b, settings.WeakColor.r);

        yield return EnterBattle();
        EnemyAgent target = _enemies.Perf.ActiveEnemies[0];
        int score = _combat.Stats.Score;
        Assert.IsTrue(_enemies.TryApplyHit(target, Vector3.forward, 1f, _state.Tick));
        Assert.IsTrue(target.Defeated, "One hit destroys a blue robot.");
        Assert.AreEqual(1, _combat.Stats.KillCount);
        Assert.AreEqual(score + _combat.Settings.hitScore + _combat.Settings.killScore, _combat.Stats.Score);
    }

    [UnityTest]
    public IEnumerator ARedRobotTakesTwoHitsAndTheSpecialDestroysItAtOnce()
    {
        yield return Load();
        EnemySettings settings = Object.Instantiate(_enemies.Settings);
        _made.Add(settings);
        SetField(settings, "spawnPhases", new[] { new EnemySpawnPhase(0f, 12, 1f, 2, 0, 0, 1f) });
        SetField(_enemies, "settings", settings);
        // The next round is prepared with these settings: every robot is the tough kind from the start.
        yield return EnterBattle();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
        {
            Assert.IsTrue(enemy.Tough);
            Assert.AreEqual(settings.ToughHits, enemy.HitsLeft);
            AssertColor(settings.ToughColor, ShownColor(enemy), "The tough kind keeps the red skin.");
        }

        yield return EnterBattle();
        CombatSettings combat = _combat.Settings;
        EnemyAgent target = _enemies.Perf.ActiveEnemies[0];
        int score = _combat.Stats.Score;
        Assert.IsTrue(_enemies.TryApplyHit(target, Vector3.forward, 1f, _state.Tick), "The first hit lands.");
        Assert.IsFalse(target.Defeated, "A red robot survives one hit.");
        Assert.IsTrue(target.Damaged);
        Assert.IsTrue(target.Staggered);
        Assert.AreEqual(0, _combat.Stats.KillCount, "No kill yet.");
        Assert.AreEqual(score + combat.hitScore, _combat.Stats.Score, "The hit scores as a hit.");
        Assert.IsFalse(_enemies.TryApplyHit(target, Vector3.forward, 1f, _state.Tick), "The same swing cannot count twice.");

        // Its lights flicker while it stands damaged.
        bool sawOff = false, sawOn = false;
        for (int frame = 0; frame < 60 && !(sawOff && sawOn); frame++)
        {
            yield return null;
            if (target.LightsOn) sawOn = true; else sawOff = true;
        }
        Assert.IsTrue(sawOff && sawOn, "A damaged robot's lights flicker.");
        Assert.IsFalse(target.Defeated);

        Assert.IsTrue(_enemies.TryApplyHit(target, Vector3.forward, 1f, _state.Tick), "The second hit lands.");
        Assert.IsTrue(target.Defeated, "...and destroys it.");
        Assert.AreEqual(1, _combat.Stats.KillCount);
        Assert.AreEqual(2, _combat.Stats.MaxCombo, "Both hits count toward the combo.");
        Assert.AreEqual(score + combat.hitScore + RoundCombatStats.ComboKillScore(combat.hitScore + combat.killScore, 2,
            combat.comboScoreStep, combat.comboScoreMaxBonus), _combat.Stats.Score, "The kill scores with the combo it made.");

        // The special destroys every red robot in reach at once.
        int kills = _combat.Stats.KillCount;
        int struck = _enemies.ApplySpecial(_player.transform.position, 80f, 1f, _state.Tick, false);
        Assert.Greater(struck, 0, "The special reaches the others.");
        Assert.AreEqual(kills + struck, _combat.Stats.KillCount, "Each of them is a kill.");
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            Assert.IsTrue(enemy.Defeated, "No red robot survives the special.");
    }

    private static Color ShownColor(EnemyAgent enemy)
    {
        var block = new MaterialPropertyBlock();
        enemy.GetComponentInChildren<SkinnedMeshRenderer>(true).GetPropertyBlock(block);
        return block.GetColor(BaseColorId);
    }

    private static void AssertColor(Color expected, Color actual, string message)
    {
        Assert.AreEqual(expected.r, actual.r, 0.01f, message);
        Assert.AreEqual(expected.g, actual.g, 0.01f, message);
        Assert.AreEqual(expected.b, actual.b, 0.01f, message);
    }

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
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
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
}

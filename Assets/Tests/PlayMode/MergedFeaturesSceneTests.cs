using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Features built side by side meet in the real scene: the performance overlay must not change how enemies are spawned,
// and the game's chosen settings (dialogue, combo bonus, same layout every round, the REBOOT IN timer label) are the
// ones the scene actually uses.
public sealed class MergedFeaturesSceneTests
{
    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
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
    }

    [UnityTest]
    public IEnumerator TheOverlayLeavesSpawnPhasesAndSpawnWarningsAlone()
    {
        yield return Load();
        yield return EnterBattle();
        var perf = Object.FindFirstObjectByType<PerfRuntime>();
        Assert.IsNotNull(perf, "The performance tools are part of the game.");
        Assert.IsTrue(_enemies.Settings.HasSpawnPhases, "The game spawns in phases.");
        int cap = _enemies.CurrentSpawnCap;
        int phaseCap = _enemies.CurrentSpawnPhase.Cap;

        perf.SetOverlayVisible(true);
        bool sawRing = false;
        for (int frame = 0; frame < 240 && !sawRing; frame++)
        {
            yield return null;
            sawRing = _enemies.ShownSpawnWarnings > 0;
        }
        Assert.IsTrue(perf.OverlayVisible);
        Assert.AreEqual(phaseCap, _enemies.CurrentSpawnPhase.Cap, "Showing the overlay does not change the spawn phase.");
        Assert.AreEqual(cap, _enemies.CurrentSpawnCap, "...or the cap.");
        Assert.IsTrue(sawRing, "Spawn warnings still appear while the overlay is shown.");
        perf.SetOverlayVisible(false);
    }

    [UnityTest]
    public IEnumerator TheSceneUsesTheChosenSettings()
    {
        yield return Load();
        var dialogue = Resources.Load<DialogueSequenceData>("DialogueSequence");
        StringAssert.Contains("비상 재부팅", dialogue.intro[0].text, "The recommended dialogue (05 §3) is in use.");
        Assert.Greater(dialogue.outroManyHits.Length, 0, "The many-hits outro (05 §4.4) is on.");
        Assert.Greater(dialogue.outroPrecise.Length, 0, "The precise outro (05 §4.4) is on.");
        Assert.AreEqual(2, dialogue.battleRadio.Length, "Noa's radio lines at 50 s and 10 s.");
        Assert.AreEqual(0.05f, _combat.Settings.comboScoreStep, 0.0001f, "Combo bonus on: +5% per combo step (provisional).");
        Assert.AreEqual(0.5f, _combat.Settings.comboScoreMaxBonus, 0.0001f, "...up to +50% (provisional).");
        Assert.IsTrue(_enemies.Settings.SameLayoutEveryRound, "Every round starts from the same enemy layout.");
        TMPro.TMP_Text label = null;
        foreach (TMPro.TMP_Text text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (text.name == "TimerLabel" && text.transform.parent != null && text.GetComponentInParent<BattleView>(true) != null)
                label = text;
        Assert.IsNotNull(label);
        Assert.AreEqual("REBOOT IN", label.text, "The battle timer reads REBOOT IN (05 §4.2).");
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
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

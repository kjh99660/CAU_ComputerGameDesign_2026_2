using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Sound effects in the real scene. Combat events sound once each and only inside the round, Pause holds the round's
// sounds while buttons still click, Retry cuts them, and the countdown, TIME UP and the result each sound in time.
public sealed class SfxSceneTests
{
    private GameStateManager _state;
    private EventManager _events;
    private SfxRuntime _sfx;
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
    public IEnumerator CombatEventsEachPlayTheirSound()
    {
        yield return Load();
        yield return EnterBattle();
        var vfx = Object.FindFirstObjectByType<CombatVfxRuntime>();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.AreEqual(Resources.Load<SfxSettings>("SfxSettings"), _sfx.Settings);
        Assert.Greater(_sfx.VoiceCount, 0);
        // No enemy attacks during this test: their hits on the player would play sounds of their own.
        enemies.StopProducing();

        int swings = _sfx.PlayCount(SfxCue.Swing), heavy = _sfx.PlayCount(SfxCue.SwingHeavy);
        vfx.PlaySlash(player.transform, 1f, _state.Tick);
        Assert.AreEqual(swings + 1, _sfx.PlayCount(SfxCue.Swing));
        vfx.PlaySlash(player.transform, 1.6f, _state.Tick);
        Assert.AreEqual(heavy + 1, _sfx.PlayCount(SfxCue.SwingHeavy), "A charged swing sounds heavier.");
        Assert.AreEqual(swings + 1, _sfx.PlayCount(SfxCue.Swing));

        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        Vector3 where = enemy.transform.position;
        int hits = _sfx.PlayCount(SfxCue.Hit), metal = _sfx.PlayCount(SfxCue.HitMetal);
        int downs = _sfx.PlayCount(SfxCue.EnemyDown), subs = _sfx.PlayCount(SfxCue.HitSub);
        Assert.IsTrue(enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(hits + 1, _sfx.PlayCount(SfxCue.Hit), "A landed blow is a dull thud...");
        Assert.AreEqual(metal + 1, _sfx.PlayCount(SfxCue.HitMetal), "...with the armour ringing...");
        Assert.AreEqual(downs + 1, _sfx.PlayCount(SfxCue.EnemyDown), "...and the robot breaking.");
        Assert.AreEqual(subs, _sfx.PlayCount(SfxCue.HitSub), "A light blow has no deep thump.");
        Assert.Greater(_sfx.LastSource.spatialBlend, 0f, "Enemy sounds lean towards the enemy.");
        Assert.Less(Vector3.Distance(where, _sfx.LastSource.transform.position), 0.01f);

        yield return new WaitForSecondsRealtime(0.15f);

        int hurts = _sfx.PlayCount(SfxCue.PlayerHurt), glitches = _sfx.PlayCount(SfxCue.PlayerGlitch);
        Assert.IsTrue(combat.Stats.RecordPlayerHit(_state.Tick, 100f));
        Assert.AreEqual(hurts + 1, _sfx.PlayCount(SfxCue.PlayerHurt));
        Assert.AreEqual(glitches + 1, _sfx.PlayCount(SfxCue.PlayerGlitch), "The player's hit breaks up like the screen.");
        Assert.IsFalse(combat.Stats.RecordPlayerHit(_state.Tick, 100.1f), "Still invulnerable.");
        Assert.AreEqual(hurts + 1, _sfx.PlayCount(SfxCue.PlayerHurt), "A hit the round refused makes no sound.");

        int full = _sfx.PlayCount(SfxCue.GaugeFull);
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.AreEqual(full + 1, _sfx.PlayCount(SfxCue.GaugeFull), "The gauge chimes once when it fills.");
        int special = _sfx.PlayCount(SfxCue.Special);
        subs = _sfx.PlayCount(SfxCue.HitSub);
        Assert.IsTrue(combat.TryUseSpecial(_state.Tick, player.transform.position));
        Assert.AreEqual(special + 1, _sfx.PlayCount(SfxCue.Special));
        Assert.AreEqual(subs + 1, _sfx.PlayCount(SfxCue.HitSub), "The special has the deep thump once, not once per enemy.");

        // A charged blow adds the deep thump. (Playing the charged attack really swings it, so this comes last: the
        // enemies it cuts down would fill the gauge.)
        yield return new WaitForSecondsRealtime(0.15f);
        subs = _sfx.PlayCount(SfxCue.HitSub);
        player.GetComponent<Animator>().Play("Charge_Release", 0, 0f);
        yield return null;
        EnemyAgent standing = null;
        foreach (EnemyAgent candidate in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID))
            if (candidate.State != EnemyAgentState.Knockback && candidate.State != EnemyAgentState.Dying) standing = candidate;
        Assert.IsNotNull(standing);
        Assert.IsTrue(enemies.TryApplyHit(standing, Vector3.forward, 1f, _state.Tick));
        Assert.GreaterOrEqual(_sfx.PlayCount(SfxCue.HitSub), subs + 1);
        player.GetComponent<Animator>().Play("Idle", 0, 0f);

        // The dodge is heard by watching the player's animator.
        int dodges = _sfx.PlayCount(SfxCue.Dodge);
        player.GetComponent<Animator>().Play("Dodge_F", 0, 0f);
        yield return null;
        yield return null;
        Assert.AreEqual(dodges + 1, _sfx.PlayCount(SfxCue.Dodge));
    }

    [UnityTest]
    public IEnumerator PauseHoldsRoundSoundsWhileButtonsStillClick()
    {
        yield return Load();
        yield return EnterBattle();
        var vfx = Object.FindFirstObjectByType<CombatVfxRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        vfx.PlaySlash(player.transform, 1.6f, _state.Tick);
        Assert.AreEqual(1, _sfx.ActiveVoices(SfxCue.SwingHeavy));
        AudioSource swing = _sfx.LastSource;

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.AreEqual(1, _sfx.PausedVoices, "Pause holds the swing where it was.");
        Assert.IsFalse(swing.isPlaying);
        yield return new WaitForSecondsRealtime(0.6f);
        Assert.AreEqual(1, _sfx.ActiveVoices(SfxCue.SwingHeavy), "A held sound does not run out while paused.");

        int confirms = _sfx.PlayCount(SfxCue.UiConfirm);
        Find<Button>(Object.FindFirstObjectByType<PauseView>().transform, "ResumeButton").onClick.Invoke();
        Assert.AreEqual(confirms + 1, _sfx.PlayCount(SfxCue.UiConfirm), "Buttons still sound while paused.");
        yield return WaitFor(GameState.Battle);
        Assert.AreEqual(0, _sfx.PausedVoices);
        Assert.AreEqual(1, _sfx.ActiveVoices(SfxCue.SwingHeavy), "The swing goes on from where it stopped.");
    }

    [UnityTest]
    public IEnumerator RetryCutsTheRoundsSoundsAndNothingSoundsOutsideTheRound()
    {
        yield return Load();
        var vfx = Object.FindFirstObjectByType<CombatVfxRuntime>();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var enemy = Object.FindFirstObjectByType<EnemyAgent>();

        // Ready: this round's combat events make no sound before the battle starts.
        int before = CombatSounds();
        vfx.PlaySlash(player.transform, 1f, _state.Tick);
        enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick);
        combat.Stats.RecordPlayerHit(_state.Tick, 0f);
        Assert.AreEqual(before, CombatSounds());

        int confirms = _sfx.PlayCount(SfxCue.UiConfirm), clicks = _sfx.PlayCount(SfxCue.UiClick);
        Find<Button>(Object.FindFirstObjectByType<StartView>().transform, "StartButton").onClick.Invoke();
        Assert.AreEqual(confirms + 1, _sfx.PlayCount(SfxCue.UiConfirm));
        yield return WaitFor(GameState.IntroDialogue);
        Find<Button>(Object.FindFirstObjectByType<DialogueView>().transform, "SkipButton").onClick.Invoke();
        Assert.AreEqual(clicks + 1, _sfx.PlayCount(SfxCue.UiClick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        Assert.IsTrue(enemies.TryApplyHit(Object.FindFirstObjectByType<EnemyAgent>(), Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(1, _sfx.ActiveVoices(SfxCue.Hit));
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.Greater(_sfx.PausedVoices, 0);
        Find<Button>(Object.FindFirstObjectByType<PauseView>().transform, "RetryButton").onClick.Invoke();
        var confirmation = Object.FindFirstObjectByType<RetryConfirmationView>(FindObjectsInactive.Include);
        Find<Button>(confirmation.transform, "ConfirmButton").onClick.Invoke();
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, _sfx.PausedVoices, "Retry leaves no held sound behind.");
        Assert.AreEqual(0, _sfx.ActiveVoices(SfxCue.Hit));
        Assert.AreEqual(0, _sfx.ActiveVoices(SfxCue.EnemyDown));
    }

    [UnityTest]
    public IEnumerator CountdownTicksThreeTimesAndTheRoundEndsWithTimeUpAndTheResult()
    {
        yield return Load();
        int ticks = _sfx.PlayCount(SfxCue.CountdownTick), go = _sfx.PlayCount(SfxCue.CountdownGo);
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        yield return WaitFor(GameState.Battle);
        Assert.AreEqual(ticks + 3, _sfx.PlayCount(SfxCue.CountdownTick), "3, 2, 1");
        Assert.AreEqual(go + 1, _sfx.PlayCount(SfxCue.CountdownGo), "START");

        int timeUp = _sfx.PlayCount(SfxCue.TimeUp), result = _sfx.PlayCount(SfxCue.Result);
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        Assert.AreEqual(timeUp + 1, _sfx.PlayCount(SfxCue.TimeUp));
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        Assert.AreEqual(result + 1, _sfx.PlayCount(SfxCue.Result));
        Assert.AreEqual(timeUp + 1, _sfx.PlayCount(SfxCue.TimeUp));
    }

    private int CombatSounds()
    {
        int sum = 0;
        for (int cue = 0; cue < SfxCues.Count; cue++)
            if (SfxCues.IsGameplay((SfxCue)cue)) sum += _sfx.PlayCount((SfxCue)cue);
        return sum;
    }

    private IEnumerator Load()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _sfx = Object.FindFirstObjectByType<SfxRuntime>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_events);
        Assert.IsNotNull(_sfx, "The bootstrap adds the sound effects.");
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

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Combat feedback in the real scene: the player's outline, the enemies' lights, the blur that follows a hit, the
// camera zoom pulse, the special attack's particle burst and the enemy's attack looks. Each one must also stop with
// Pause and be gone after Retry, like everything else in the round.
public sealed class CombatFeedbackSceneTests
{
    private GameStateManager _state;
    private EventManager _events;

    private int _oldCaptureFramerate;

    // A fixed 60 fps step, so the timing of the effects does not depend on how fast the test machine draws.
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
    public IEnumerator PlayerIsOutlinedAndEnemiesCarryTheirLights()
    {
        yield return Load();
        var outline = Object.FindFirstObjectByType<PlayerOutline>();
        Assert.IsNotNull(outline, "The player gets an outline when the round is wired up.");
        Assert.IsTrue(outline.IsShown);
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        int outlined = 0;
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
            Material[] materials = renderer.sharedMaterials;
            Assert.GreaterOrEqual(materials.Length, 3, renderer.name + " keeps its own material and gets two more.");
            Material mark = materials[materials.Length - 2], band = materials[materials.Length - 1];
            Assert.AreEqual("Hidden/Reboot/CharacterOutline", mark.shader.name);
            Assert.AreEqual("Hidden/Reboot/CharacterOutline", band.shader.name);
            Assert.Less(mark.renderQueue, band.renderQueue, "Every silhouette mark is drawn before any band.");
            Assert.AreEqual(0f, mark.GetFloat("_OutlinePixels"));
            Assert.Greater(band.GetFloat("_OutlinePixels"), 0f);
            outlined++;
        }
        Assert.AreEqual(outline.RendererCount, outlined);
        Assert.GreaterOrEqual(outlined, 2, "Body and sword.");

        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        var skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.AreEqual("REBOOT/Enemy Lit", skin.sharedMaterial.shader.name);
        Assert.IsNotNull(skin.sharedMaterial.GetTexture("_GlowMap"));
    }

    [UnityTest]
    public IEnumerator HitBlurOutlastsTheGlitchFreezesInPauseAndIsGoneAfterRetry()
    {
        yield return Load();
        yield return EnterBattle();
        var battle = Object.FindFirstObjectByType<BattleView>();
        Assert.AreEqual(0f, ScreenGlitchState.Blur);

        battle.PlayPlayerHitFeedback();
        yield return null;
        yield return null;
        Assert.Greater(ScreenGlitchState.Blur, 0.2f, "A hit blurs the 3D view.");
        float deadline = Time.time + 1f;
        while (ScreenGlitchState.Amount > 0f && Time.time < deadline) yield return null;
        Assert.AreEqual(0f, ScreenGlitchState.Amount, "The glitch is over.");
        Assert.Greater(ScreenGlitchState.Blur, 0.05f, "The blur is still clearing after the glitch has ended.");

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedBlur = ScreenGlitchState.Blur, pausedTime = battle.HitBlurRemaining;
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.AreEqual(pausedBlur, ScreenGlitchState.Blur, "Paused: the blur stays as it was.");
        Assert.AreEqual(pausedTime, battle.HitBlurRemaining);

        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        deadline = Time.time + 3f;
        while (ScreenGlitchState.Blur > 0f && Time.time < deadline) yield return null;
        Assert.AreEqual(0f, ScreenGlitchState.Blur, "It clears by itself.");

        battle.PlayPlayerHitFeedback();
        yield return null;
        yield return null;
        Assert.Greater(ScreenGlitchState.Blur, 0f);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0f, ScreenGlitchState.Blur, "Retry leaves no blur behind.");
        Assert.AreEqual(0f, battle.HitBlurRemaining);
    }

    [UnityTest]
    public IEnumerator HitsAndTheSpecialPulseTheCameraWhichReturnsToRest()
    {
        yield return Load();
        yield return EnterBattle();
        var feedback = Object.FindFirstObjectByType<CombatCameraFeedback>();
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        Camera camera = Camera.main;
        Assert.IsNotNull(feedback);
        float rest = camera.fieldOfView;

        // An accepted hit: the view pushes in, then comes back to exactly where it was.
        Assert.IsTrue(enemies.TryApplyHit(Object.FindFirstObjectByType<EnemyAgent>(), Vector3.forward, 1f, _state.Tick));
        Assert.IsTrue(feedback.IsPulsing);
        float narrowest = rest;
        float deadline = Time.time + 1f;
        while (feedback.IsPulsing && Time.time < deadline)
        {
            yield return null;
            narrowest = Mathf.Min(narrowest, camera.fieldOfView);
        }
        Assert.Less(narrowest, rest - settings.hitZoomDegrees * 0.5f, "The hit pulse narrows the view.");
        Assert.IsFalse(feedback.IsPulsing);
        Assert.AreEqual(rest, camera.fieldOfView, 0.0001f, "After the pulse the field of view is back at rest.");

        // The special pulses harder, freezes with Pause and is undone by Retry.
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.IsTrue(combat.TryUseSpecial(_state.Tick, player.transform.position));
        yield return null;
        yield return null;
        Assert.IsTrue(feedback.IsPulsing);
        Assert.Greater(feedback.ZoomDegrees, 0f);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedView = camera.fieldOfView;
        Assert.Less(pausedView, rest, "Paused in the middle of the pulse.");
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.AreEqual(pausedView, camera.fieldOfView, "Paused: the view does not move.");
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(rest, camera.fieldOfView, 0.0001f, "Retry puts the field of view back.");
        Assert.IsFalse(feedback.IsPulsing);
    }

    [UnityTest]
    public IEnumerator SpecialPlaysItsPooledBurstAndRetryClearsIt()
    {
        yield return Load();
        yield return EnterBattle();
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.IsNotNull(settings.specialBurstPrefab, "Special burst effect");
        Assert.IsNotNull(settings.specialRadialPrefab, "Special ring effect");
        var vfx = Object.FindFirstObjectByType<CombatVfxRuntime>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        int expected = 1 + settings.specialRadialCount;
        Assert.AreEqual(0, vfx.ActiveSpecialEffects);

        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.IsTrue(combat.TryUseSpecial(_state.Tick, player.transform.position));
        Assert.AreEqual(expected, vfx.ActiveSpecialEffects, "One burst on the player and a ring of copies around it.");
        int instances = vfx.PooledSpecialEffects;
        Assert.AreEqual(expected, instances);

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.AreEqual(expected, vfx.ActiveSpecialEffects, "Paused effects stay where they are.");
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(0, vfx.ActiveSpecialEffects, "Retry leaves no effect behind.");

        // A second special reuses the same instances instead of creating new ones.
        yield return EnterBattle();
        for (int i = 0; i < 13; i++) combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.IsTrue(combat.TryUseSpecial(_state.Tick, player.transform.position));
        Assert.AreEqual(expected, vfx.ActiveSpecialEffects);
        Assert.AreEqual(instances, vfx.PooledSpecialEffects, "Effects are pooled.");
    }

    [UnityTest]
    public IEnumerator EnemyAttacksUseSeveralLooksWithoutRepeatingOne()
    {
        yield return Load();
        yield return EnterBattle();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        EnemySettings settings = enemies.Settings;
        var enemy = Object.FindFirstObjectByType<EnemyAgent>();
        Assert.AreEqual(3, enemy.AttackLookCount, "The controller offers three attack looks.");

        // Drive one enemy through a run of attacks against a point in reach, all inside this frame.
        Vector3 target = enemy.transform.position + enemy.transform.forward * (settings.AttackRange * 0.5f);
        var none = System.Array.Empty<EnemyAgent>();
        var seen = new HashSet<int>();
        int previous = -1;
        for (int attack = 0; attack < 12; attack++)
        {
            for (int step = 0; step < 600 && enemy.State != EnemyAgentState.Attack; step++)
                enemy.RunAI(target, none, settings, 0.05f, _state.Tick);
            Assert.AreEqual(EnemyAgentState.Attack, enemy.State, "attack " + attack);
            Assert.That(enemy.AttackLook, Is.InRange(0, 2));
            Assert.AreNotEqual(previous, enemy.AttackLook, "The same look is not shown twice in a row.");
            previous = enemy.AttackLook;
            seen.Add(previous);
            enemy.RunAction(settings, settings.AttackDuration + 0.01f, _state.Tick);
            Assert.AreNotEqual(EnemyAgentState.Attack, enemy.State, "Every look ends after the same attack duration.");
        }
        Assert.GreaterOrEqual(seen.Count, 2, "More than one look is used.");
    }

    private IEnumerator Load()
    {
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        Assert.IsNotNull(_state);
        Assert.IsNotNull(_events);
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

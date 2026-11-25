using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// The lively crowd and the player's hurt in the real scene: only a few enemies attack at once while the others keep
// moving around the player, a hit on the player flashes its outline and blinks it until it can be hit again, and the
// charge reach and the special's ring use the neon floor markings.
public sealed class EnemyLifeSceneTests
{
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
    }

    [UnityTest]
    public IEnumerator TheCrowdTakesTurnsAndKeepsMovingAroundThePlayer()
    {
        yield return Load();
        yield return EnterBattle();
        EnemySettings settings = _enemies.Settings;
        Assert.Greater(settings.Liveliness, 0f, "The game's enemies are lively.");
        Assert.Greater(settings.MaximumAttackers, 0);
        float sizeUp = settings.HoverRingMaximum + settings.SizeUpDistance;
        int mostAttackers = 0, nearSamples = 0, movingNearSamples = 0;
        for (int frame = 0; frame < 900; frame++)
        {
            yield return null;
            mostAttackers = Mathf.Max(mostAttackers, _enemies.ActiveAttackers);
            if (frame < 420 || frame % 10 != 0) continue;
            foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            {
                if (enemy.State != EnemyAgentState.Chase && enemy.State != EnemyAgentState.Idle) continue;
                Vector3 gap = enemy.transform.position - _player.transform.position;
                gap.y = 0f;
                if (gap.magnitude > sizeUp) continue;
                nearSamples++;
                if (enemy.PlanarVelocity.magnitude > 0.3f) movingNearSamples++;
            }
        }
        Assert.LessOrEqual(mostAttackers, settings.MaximumAttackers, "Never more than the allowed attackers at once.");
        Assert.Greater(nearSamples, 20, "Enemies came close.");
        Assert.Greater(movingNearSamples, nearSamples / 3, "Close to the player, the waiting enemies keep moving.");
    }

    [UnityTest]
    public IEnumerator AHitOnThePlayerFlashesItsOutlineThenBlinksUntilItCanBeHitAgain()
    {
        yield return Load();
        yield return EnterBattle();
        _enemies.StopProducing();
        var outline = _player.GetComponent<PlayerOutline>();
        var impact = Object.FindFirstObjectByType<CombatImpactFeedback>();
        CombatVfxSettings vfx = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.IsTrue(Near(outline.RestColor, outline.CurrentColor));

        Assert.IsTrue(_combat.Stats.RecordPlayerHit(_state.Tick, 100f));
        Assert.IsTrue(Near(vfx.hurtOutlineColor, outline.CurrentColor), "The outline flashes the hurt colour.");
        bool sawRest = false, sawHurt = false;
        float until = Time.realtimeSinceStartup + 4f;
        while (impact.HurtRemaining > 0f && Time.realtimeSinceStartup < until)
        {
            yield return null;
            if (impact.HurtRemaining < _combat.Settings.invulnerabilitySeconds - vfx.hurtSolidSeconds - 0.05f)
            {
                if (Near(outline.CurrentColor, outline.RestColor)) sawRest = true;
                else sawHurt = true;
            }
            if (sawRest && sawHurt) break;
        }
        Assert.IsTrue(sawRest && sawHurt, "Then it blinks.");

        // Paused, the blink holds; afterwards it ends on the usual colour.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float held = impact.HurtRemaining;
        Color shown = outline.CurrentColor;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.AreEqual(held, impact.HurtRemaining);
        Assert.IsTrue(Near(shown, outline.CurrentColor));
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        until = Time.realtimeSinceStartup + 4f;
        while (impact.HurtRemaining > 0f && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(Near(outline.RestColor, outline.CurrentColor), "When the player can be hit again the outline is white.");

        // Retry in the middle of a blink leaves the usual colour.
        Assert.IsTrue(_combat.Stats.RecordPlayerHit(_state.Tick, 200f));
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.IsTrue(Near(outline.RestColor, outline.CurrentColor));
    }

    [UnityTest]
    public IEnumerator TheChargeReachAndTheSpecialRingUseTheNeonFloorMarkings()
    {
        yield return Load();
        yield return EnterBattle();
        CombatVfxSettings vfx = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.IsNotNull(vfx.groundIndicatorMaterial);
        Assert.AreEqual("REBOOT/Ground Indicator", vfx.groundIndicatorMaterial.shader.name);
        // The charge reach (made by the player's ChargeRangePreview, hidden until the player charges).
        MeshRenderer reach = null;
        foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            if (renderer.name == "ChargeRangePreview (auto)" && renderer.gameObject.scene.IsValid()) reach = renderer;
        Assert.IsNotNull(reach);
        Assert.AreEqual("REBOOT/Ground Indicator", reach.sharedMaterial.shader.name, "The charge reach uses the neon marking.");
        var ring = Object.FindFirstObjectByType<SpecialFeedback>();
        Assert.IsTrue(ring.UsesNeon, "The special's ring uses the neon marking.");
        for (int i = 0; i < 200 && _combat.Stats.Gauge < _combat.Settings.maximumGauge; i++) _combat.Stats.RecordEnemyKill(_state.Tick);
        Assert.IsTrue(_combat.TryUseSpecial(_state.Tick, _player.transform.position));
        Assert.IsTrue(ring.IsShowing);
    }

    // Colours read back from a material can differ from what was set in the last few bits.
    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;

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

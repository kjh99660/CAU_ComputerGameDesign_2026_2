using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// 스킬(E): 게이지 절반을 쓰고, 0.21초 준비 뒤 적을 뚫고 9m를 곧게 돌진해, 지나간 길(폭 3m)만 일반 타격으로 한 번 벤다.
// 도착한 자리 주변은 베지 않는다. 스킬로 맞힌 타격은 게이지를 채우지 않는다. 돌진한 길에는 홀로그램 잔상이 남는다.
// 궁극기(Q): 게이지를 전부 쓰고 주변 반지름 8m를 종류와 관계없이 부순다. 그 처치도 게이지를 채우지 않는다.
// 실제 씬(평지 아레나)을 가짜 키보드로 60fps 고정 걸음으로 플레이한다. 적은 생성과 AI를 멈춘 뒤 정해진 자리에 다시
// 세운다. 값은 모두 임시값이다(Assets/Data/Skill/Skill1.asset, CombatSettings의 skill·special 값).
public sealed class SkillSceneTests
{
    private const int Fps = 60;
    private const float DashMetres = 9f;
    private const float WindupSeconds = 0.21f;
    private const float LaneWidth = 3f;
    private const float LanePadding = 0.5f;
    private const float SpecialMetres = 8f;
    private readonly List<EnemyAgent> _robots = new();
    private readonly List<GameObject> _props = new();
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
    private CombatRoundRuntime _combat;
    private SkillFeedback _feedback;
    private Animator _animator;
    private Camera _camera;
    private CombatSettings _pinnedSettings;
    private float _oldHitGauge, _oldKillGauge;

    private RoundCombatStats Stats => _combat.Stats;

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
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        _feedback = Object.FindFirstObjectByType<SkillFeedback>();
        _animator = _player.GetComponent<Animator>();
        _camera = Camera.main;
        Assert.IsNotNull(_feedback, "The skill's markings are part of the scene.");
        // These tests are about the skill and the special, not the tuning: a kill charges 3 + 5 here, so the gauge
        // arithmetic below stays the same whatever the game's settings asset holds (SkillGaugeTests checks those values).
        _pinnedSettings = _combat.Settings;
        _oldHitGauge = _pinnedSettings.hitGauge;
        _oldKillGauge = _pinnedSettings.killGauge;
        _pinnedSettings.hitGauge = 3f;
        _pinnedSettings.killGauge = 5f;
        yield return WaitFor(GameState.Ready);
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        _keyboard = InputSystem.AddDevice<Keyboard>("SkillTestKeyboard");
        _mouse = InputSystem.AddDevice<Mouse>("SkillTestMouse");
        _robots.Clear();
        yield return Frames(3);
    }

    [UnityTearDown]
    public IEnumerator Restore()
    {
        if (_pinnedSettings != null)
        {
            _pinnedSettings.hitGauge = _oldHitGauge;
            _pinnedSettings.killGauge = _oldKillGauge;
            _pinnedSettings = null;
        }
        foreach (GameObject prop in _props)
            if (prop != null) Object.Destroy(prop);
        _props.Clear();
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
    public IEnumerator TheSkillSpendsHalfTheGaugeDashesNineMetresThroughEnemiesAndCutsOnlyAlongItsPath()
    {
        yield return Arrange(8);
        Vector3 forward = Flat(_camera.transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        yield return MoveToClearLane(forward);
        Vector3 start = _player.transform.position;
        Vector3 arrival = start + forward * DashMetres;
        // The blow covers the path, 3 m wide and half a metre past both ends. It reaches a robot's body, which is
        // 1.4 m wide, so a robot is hit when its middle is up to about 2.2 m from the line.
        EnemyAgent onThePath = Place(0, start + forward * 3f);
        EnemyAgent besideThePath = Place(1, start + forward * 1.6f + right * 1.8f);
        EnemyAgent atTheEnd = Place(2, start + forward * (DashMetres + 0.5f) - right * 0.3f);
        EnemyAgent tough = Place(3, start + forward * 4.6f - right * 1.2f, 2);
        EnemyAgent besideTheEnd = Place(4, arrival + right * 4f);          // a circle round the end would reach it
        EnemyAgent farBeside = Place(5, start + forward * 3f - right * 3.8f);
        EnemyAgent ahead = Place(6, start + forward * (DashMetres + 3f));
        EnemyAgent behind = Place(7, start - forward * 2.6f);
        yield return Frames(3);
        Charge(7);
        Assert.AreEqual(56f, Stats.Gauge);
        int kills = Stats.KillCount;
        int combo = Stats.ComboAt(_combat.CombatTime);
        var sfx = Object.FindFirstObjectByType<SfxRuntime>();
        int dashSounds = sfx.PlayCount(SfxCue.SkillDash), swingSounds = sfx.PlayCount(SfxCue.SwingHeavy);
        SkinnedMeshRenderer[] body = _player.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        int shown = CountEnabled(body);
        Assert.Greater(shown, 0);
        Assert.IsTrue(_feedback.DrawsGhosts, "The copies the dash leaves need their shader and the player's meshes.");

        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        Assert.AreEqual(6f, Stats.Gauge, "Half the gauge is spent when the key is pressed.");
        Assert.AreEqual(1, Stats.SkillUseCount);
        Assert.AreEqual(0, Stats.SpecialUseCount, "E is the skill, not the special.");
        Assert.IsTrue(_feedback.AimShowing, "The floor shows the stretch the dash will cut while the skill winds up.");
        Assert.AreEqual(LaneWidth, _feedback.AimWidth, 1e-3f, "The lane is as wide as the blow.");
        Assert.Less(Flat(_feedback.AimFrom - start, false).magnitude, 0.05f);
        Assert.Less(Flat(_feedback.AimTo - (arrival + forward * LanePadding), false).magnitude, 0.05f);
        float fill = _feedback.AimFill;
        yield return Frames(6);
        Assert.Greater(_feedback.AimFill, fill, "The lane fills as the wind-up runs out.");
        Assert.IsFalse(_player.IsInvulnerable, "The wind-up can be hit.");
        Assert.Less(Flat(_player.transform.position - start, false).magnitude, 0.05f, "The wind-up stands still.");
        Assert.AreEqual(0, _feedback.GhostsSpawned);

        int windupFrames = 7;   // frames already spent in the wind-up above (the press, the checks and the six frames)
        for (; windupFrames < 120 && !Logical().IsName("Skill1_Dash"); windupFrames++) yield return null;
        Assert.AreEqual(WindupSeconds * Fps, windupFrames, 2.5f, "The wind-up lasts 0.21 s (about 13 frames).");
        bool invulnerable = false, hidden = false;
        for (int i = 0; i < 60 && !Logical().IsName("Skill1_Main"); i++)
        {
            invulnerable |= _player.IsInvulnerable;
            hidden |= CountEnabled(body) < shown;
            yield return null;
        }
        Assert.IsTrue(Logical().IsName("Skill1_Main"));
        Assert.IsTrue(invulnerable, "The dash cannot be hit.");
        Assert.IsTrue(hidden, "The player vanishes for the dash.");
        Assert.IsFalse(_player.IsInvulnerable, "Only the dash is safe.");
        Assert.AreEqual(shown, CountEnabled(body), "The player is back.");
        Vector3 moved = Flat(_player.transform.position - start, false);
        Assert.AreEqual(DashMetres, moved.magnitude, 0.05f, "The dash goes its whole length through the robot in the way.");
        Assert.Greater(Vector3.Dot(moved.normalized, forward), 0.999f, "With no move key held the dash follows the camera.");
        Assert.IsFalse(_feedback.AimShowing);
        Assert.AreEqual(1, _feedback.DashesPlayed);
        Assert.IsTrue(_feedback.StreakShowing, "The dash leaves its cut.");
        Assert.Less(Flat(_feedback.StreakFrom - start, false).magnitude, 0.1f);
        Assert.Less(Flat(_feedback.StreakTo - _player.transform.position, false).magnitude, 0.1f);
        Assert.AreEqual(dashSounds + 1, sfx.PlayCount(SfxCue.SkillDash));
        Assert.AreEqual(swingSounds + 1, sfx.PlayCount(SfxCue.SwingHeavy), "The blow at the end of the dash is heard.");
        // The copies: the first where the dash began, the others along the way, none past where it ended.
        int ghosts = _feedback.GhostsSpawned;
        float spacing = _feedback.GhostSpacing;
        Assert.AreEqual(Mathf.FloorToInt(DashMetres / spacing + 0.001f) + 1, ghosts,
            "A copy where the dash began and one every 1.2 m of it, although the dash covers about a metre a frame.");
        Assert.AreEqual(ghosts, _feedback.GhostsAlive);
        Assert.Less(Flat(_feedback.GhostPosition(0) - start, false).magnitude, 0.05f);
        for (int i = 1; i < ghosts; i++)
        {
            Vector3 offset = Flat(_feedback.GhostPosition(i) - start, false);
            float along = Vector3.Dot(offset, forward);
            float previous = Vector3.Dot(Flat(_feedback.GhostPosition(i - 1) - start, false), forward);
            Assert.AreEqual(spacing, along - previous, 0.05f, "The copies are evenly spaced.");
            Assert.LessOrEqual(along, DashMetres + 0.05f);
            Assert.Less((offset - forward * along).magnitude, 0.05f, "The copies stand on the path.");
        }
        // The robots are where they were put (a prop of the arena could have pushed one aside).
        Assert.Less(Lateral(onThePath, start, forward), 0.3f);
        Assert.Less(Lateral(besideThePath, start, forward), 2f);
        Assert.Less(Lateral(tough, start, forward), 1.4f);
        Assert.Greater(Lateral(besideTheEnd, start, forward), 3.6f);
        Assert.Greater(Lateral(farBeside, start, forward), 3.4f);

        for (int i = 0; i < 120 && Stats.KillCount == kills; i++) yield return null;
        yield return Frames(6);   // the blow stays out for 0.05 s
        Assert.IsTrue(onThePath.Defeated, "The robot the dash went through.");
        Assert.IsTrue(besideThePath.Defeated, "1.8 m beside the line is inside the 3 m wide blow.");
        Assert.IsTrue(atTheEnd.Defeated, "The blow reaches half a metre past where the dash ended.");
        Assert.IsFalse(tough.Defeated, "The skill is a normal hit: a tough robot takes two.");
        Assert.AreEqual(1, tough.HitsLeft);
        Assert.IsFalse(besideTheEnd.Defeated, "Nothing is cut round where the dash ended: 4 m beside the end is safe.");
        Assert.IsFalse(farBeside.Defeated, "3.8 m beside the line is out of reach.");
        Assert.IsFalse(ahead.Defeated, "3 m past the end is out of reach.");
        Assert.IsFalse(behind.Defeated, "Behind where the dash began is out of reach.");
        Assert.AreEqual(1, besideTheEnd.HitsLeft);
        Assert.AreEqual(kills + 3, Stats.KillCount);
        Assert.AreEqual(combo + 4, Stats.ComboAt(_combat.CombatTime), "The skill's blows go on with the combo like normal hits.");
        Assert.AreEqual(6f, Stats.Gauge, 1e-3f, "A blow of the skill charges nothing, whether it destroys the robot or not.");

        // The copies break up and are gone well within a second.
        yield return Frames(60);
        Assert.AreEqual(0, _feedback.GhostsAlive);
        Assert.IsFalse(_feedback.StreakShowing);

        // A sword attack afterwards charges the gauge as always: 8 for the robot it destroys.
        yield return WaitUntilIdle();
        Place(6, start - forward * 12f);   // out of the sword's way
        Place(4, _player.transform.position + _player.transform.forward * 1.6f);
        yield return Frames(3);
        float before = Stats.Gauge;
        int killsBefore = Stats.KillCount;
        yield return Click();
        for (int i = 0; i < 120 && !besideTheEnd.Defeated; i++) yield return null;
        Assert.IsTrue(besideTheEnd.Defeated, "The sword attack reaches the robot standing in front.");
        Assert.AreEqual(killsBefore + 1, Stats.KillCount);
        Assert.AreEqual(before + _pinnedSettings.hitGauge + _pinnedSettings.killGauge, Stats.Gauge, 1e-3f);
        Assert.AreEqual(1, Stats.SkillUseCount);
    }

    [UnityTest]
    public IEnumerator TheSpecialOnQReachesEightMetresDestroysEitherKindAndItsKillsChargeNothing()
    {
        // The special goes through the enemies' own search, which a stopped spawner switches off: the robots keep
        // their AI here and are put in place in the frame before the key is pressed.
        for (int i = 0; i < 1200 && _enemies.ActiveEnemyCount < 5; i++)
        {
            _enemies.UpdateSpawning(0.25f, 0.25f, _state.Tick);
            if (i % 4 == 3) yield return null;
        }
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            if (enemy.ActiveTick == _state.Tick && !enemy.Defeated && enemy.State != EnemyAgentState.Pooled &&
                enemy.State != EnemyAgentState.Dying && _robots.Count < 5)
                _robots.Add(enemy);
        Assert.AreEqual(5, _robots.Count);
        Charge(13);
        Assert.AreEqual(100f, Stats.Gauge);
        Assert.AreEqual(SpecialMetres, _combat.Settings.specialRadius, 1e-3f);
        int kills = Stats.KillCount;
        Vector3 centre = _player.transform.position;
        Vector3 forward = Flat(_camera.transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        EnemyAgent near = Place(0, centre + right * 2.5f);
        EnemyAgent atSeven = Place(1, centre - right * 7f);
        EnemyAgent toughAtSeven = Place(2, centre - forward * 7f, 2);
        EnemyAgent outOfReach = Place(3, centre + right * 10.5f);
        EnemyAgent farOutOfReach = Place(4, centre - right * 14f);
        yield return Frames(2);   // the enemies' search learns where they stand now (they barely move in two frames)

        yield return Press(Key.Q);
        Assert.AreEqual(1, Stats.SpecialUseCount, "Q is the special.");
        Assert.AreEqual(0, Stats.SkillUseCount);
        Assert.IsTrue(near.Defeated);
        Assert.IsTrue(atSeven.Defeated, "7 m is inside the 8 m the special reaches.");
        Assert.IsTrue(toughAtSeven.Defeated, "The special destroys a tough robot at once.");
        Assert.IsFalse(outOfReach.Defeated, "10.5 m is out of reach.");
        Assert.IsFalse(farOutOfReach.Defeated);
        Assert.AreEqual(kills + 3, Stats.KillCount);
        Assert.AreEqual(0f, Stats.Gauge, "The special spends the whole gauge and its kills charge nothing.");
        yield return Press(Key.Q);
        Assert.AreEqual(1, Stats.SpecialUseCount, "An empty gauge: no second special.");
    }

    [UnityTest]
    public IEnumerator WithoutHalfTheGaugeTheSkillDoesNotStartAndTheKeyShakes()
    {
        yield return Arrange(0);
        var battle = Object.FindFirstObjectByType<BattleView>();
        HudKeyBadge key = Find<HudKeyBadge>(battle.transform, "SkillKeyBadge");
        Assert.AreEqual("E", Find<TMPro.TMP_Text>(key.transform, "Key").text, "The skill's key on the HUD.");
        RectTransform mark = Find<RectTransform>(battle.transform, BattleView.SkillMarkName);
        Assert.IsNotNull(mark, "The gauge shows where the skill's share ends.");
        Assert.IsTrue(mark.gameObject.activeInHierarchy);
        Assert.AreEqual(0f, mark.anchoredPosition.x, 0.5f, "Half the gauge is the bottom of the ring.");
        Assert.Less(mark.anchoredPosition.y, -60f);
        Assert.AreEqual(HudKeyBadge.State.Dim, key.Current, "E is dim on an empty gauge.");

        Charge(6);
        Assert.AreEqual(48f, Stats.Gauge);
        Assert.AreEqual(HudKeyBadge.State.Dim, key.Current, "48 of 50.");
        Vector3 start = _player.transform.position;
        yield return Press(Key.E);
        bool shook = false;
        for (int i = 0; i < 30; i++)
        {
            shook |= key.IsDenying;
            Assert.IsFalse(Logical().IsTag("Action"), "The skill must not start.");
            yield return null;
        }
        Assert.IsTrue(shook, "The E key shakes when the gauge is short.");
        Assert.IsFalse(key.IsDenying, "And comes to rest.");
        Assert.AreEqual(48f, Stats.Gauge, "A refused press spends nothing.");
        Assert.AreEqual(0, Stats.SkillUseCount);
        Assert.Less(Flat(_player.transform.position - start, false).magnitude, 0.05f);
        Assert.IsFalse(_feedback.AimShowing);

        Charge(1);
        Assert.AreEqual(56f, Stats.Gauge);
        Assert.AreEqual(HudKeyBadge.State.Ready, key.Current, "E is bright once half the gauge is there.");
        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        Assert.AreEqual(6f, Stats.Gauge);
        Assert.AreEqual(HudKeyBadge.State.Dim, key.Current, "And dim again after the use.");
    }

    [UnityTest]
    public IEnumerator AWindupCutShortByADodgeGivesTheGaugeBack()
    {
        yield return Arrange(0);
        Charge(7);
        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        Assert.AreEqual(6f, Stats.Gauge);
        yield return Frames(4);
        yield return Press(Key.LeftShift);
        for (int i = 0; i < 60 && !Logical().IsName("Dodge_B") && !Logical().IsName("Dodge_F"); i++)
            yield return null;
        Assert.IsTrue(Logical().IsName("Dodge_B") || Logical().IsName("Dodge_F"), "The dodge cuts the wind-up short.");
        yield return Frames(3);
        Assert.AreEqual(56f, Stats.Gauge, "The skill did nothing, so its share of the gauge comes back.");
        Assert.AreEqual(0, Stats.SkillUseCount);
        Assert.IsFalse(_feedback.AimShowing, "No lane is left on the floor.");
        Assert.AreEqual(0, _feedback.DashesPlayed);
        Assert.AreEqual(0, _feedback.GhostsSpawned);

        // The same press works again afterwards, and this time it is spent for good.
        yield return WaitUntilIdle();
        yield return Press(Key.E);
        Vector3 from = _player.transform.position;
        yield return WaitUntilEntering("Skill1_Main");
        Assert.AreEqual(DashMetres, Flat(_player.transform.position - from, false).magnitude, 0.05f);
        Assert.AreEqual(6f, Stats.Gauge);
        Assert.AreEqual(1, Stats.SkillUseCount);
    }

    [UnityTest]
    public IEnumerator AWallStopsTheDashAndOnlyTheStretchReallyGoneIsCut()
    {
        yield return Arrange(2);
        Vector3 forward = Flat(_camera.transform.forward);
        yield return MoveToClearLane(forward);
        Vector3 start = _player.transform.position;
        // A wall whose near face is 3 m ahead: the player (0.5 m wide to each side) stops about 2.4 m ahead.
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _props.Add(wall);
        wall.transform.SetPositionAndRotation(start + forward * 3.25f + Vector3.up * 1.5f, Quaternion.LookRotation(forward));
        wall.transform.localScale = new Vector3(10f, 3f, 0.5f);
        Physics.SyncTransforms();
        // On the line of the dash: one robot before the wall, one 5 m ahead, which a dash of the whole 9 m would have cut.
        EnemyAgent beforeTheWall = Place(0, start + forward * 1.4f);
        EnemyAgent pastTheWall = Place(1, start + forward * 5f);
        yield return Frames(3);
        Charge(7);
        int kills = Stats.KillCount;

        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Main");
        Vector3 moved = Flat(_player.transform.position - start, false);
        Assert.Less(Vector3.Dot(moved, forward), 3f, "The dash does not go through a wall.");
        Assert.Greater(Vector3.Dot(moved, forward), 2f, "It goes as far as the wall.");
        Assert.Less(Flat(_feedback.StreakTo - _player.transform.position, false).magnitude, 0.1f, "The cut ends where the dash ended.");
        Assert.AreEqual(6f, Stats.Gauge, "The gauge is spent all the same.");
        for (int i = 1; i < _feedback.GhostsSpawned; i++)
            Assert.Less(Vector3.Dot(Flat(_feedback.GhostPosition(i) - start, false), forward), 3f, "No copy stands past the wall.");
        for (int i = 0; i < 120 && Stats.KillCount == kills; i++) yield return null;
        yield return Frames(6);
        Assert.IsTrue(beforeTheWall.Defeated, "On the stretch the dash really went.");
        Assert.IsFalse(pastTheWall.Defeated, "Past the wall: the dash never got there.");
    }

    [UnityTest]
    public IEnumerator PressingAgainBeforeTheBlowLandsDoesNothingAndAfterItStartsTheSecondUse()
    {
        yield return Arrange(1);
        Vector3 forward = Flat(_camera.transform.forward);
        yield return MoveToClearLane(forward);
        // A tough robot on the path: it survives each blow, so its hits left count the blows that landed.
        EnemyAgent robot = Place(0, _player.transform.position + forward * 3f, 2);
        yield return Frames(3);
        Charge(13);
        Assert.AreEqual(100f, Stats.Gauge);

        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        yield return Press(Key.E);   // during the wind-up
        yield return WaitUntilEntering("Skill1_Dash");
        yield return Press(Key.E);   // during the dash
        yield return WaitUntilEntering("Skill1_Main");
        yield return Press(Key.E);   // in the main part, before its blow lands (0.1 s in)
        for (int i = 0; i < 120 && robot.HitsLeft == 2; i++) yield return null;
        Assert.AreEqual(1, robot.HitsLeft, "The first use lands its blow.");
        Assert.AreEqual(1, Stats.SkillUseCount, "Presses before the blow has landed are not taken: the gauge is spent once.");
        Assert.AreEqual(1, _feedback.DashesPlayed);
        Assert.AreEqual(50f, Stats.Gauge, 1e-3f);

        // Once the blow has landed, the same key starts the second use that a full gauge pays for.
        yield return Frames(20);
        Assert.IsTrue(Logical().IsName("Skill1_Main"));
        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        Assert.AreEqual(2, Stats.SkillUseCount);
        Assert.AreEqual(0f, Stats.Gauge, 1e-3f);
    }

    [UnityTest]
    public IEnumerator ASkillPressedJustBeforeTheTimeIsUpStillLandsInFinishing()
    {
        yield return Arrange(1);
        Vector3 forward = Flat(_camera.transform.forward);
        yield return MoveToClearLane(forward);
        EnemyAgent robot = Place(0, _player.transform.position + forward * 3f);
        yield return Frames(3);
        Charge(7);
        int kills = Stats.KillCount, score = Stats.Score;

        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        Assert.IsFalse(robot.Defeated, "The time ran out while the skill was still winding up.");
        for (int i = 0; i < 600 && !robot.Defeated && _state.CurrentState == GameState.Finishing; i++) yield return null;
        Assert.IsTrue(robot.Defeated, "A skill that began before the time was up plays out and lands in Finishing.");
        Assert.AreEqual(GameState.Finishing, _state.CurrentState);
        Assert.AreEqual(kills + 1, Stats.KillCount, "And its kill counts.");
        Assert.Greater(Stats.Score, score);
        Assert.AreEqual(6f, Stats.Gauge, 1e-3f);
        Assert.AreEqual(1, Stats.SkillUseCount);

        // No new skill starts once the time is up, whatever the gauge holds.
        Charge(6);
        float gauge = Stats.Gauge;
        Assert.GreaterOrEqual(gauge, 50f);
        yield return Press(Key.E);
        yield return Frames(20);
        Assert.AreEqual(1, Stats.SkillUseCount);
        Assert.AreEqual(gauge, Stats.Gauge, 1e-3f);
    }

    [UnityTest]
    public IEnumerator EveryRobotOnThePathIsHitEvenInACrowdOfSeventy()
    {
        const int crowd = 70;
        yield return Arrange(crowd);
        Vector3 forward = Flat(_camera.transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        yield return MoveToClearLane(forward);
        Vector3 start = _player.transform.position;
        // Ten rows of seven across the lane, from half a metre ahead to half a metre short of the end.
        int placed = 0;
        for (int row = 0; row < 10; row++)
            for (int column = 0; column < 7; column++)
                Place(placed++, start + forward * (0.5f + row * (DashMetres - 1f) / 9f) + right * ((column - 3) * 0.4f));
        Assert.AreEqual(crowd, placed);
        yield return Frames(3);
        Charge(7);
        int kills = Stats.KillCount;

        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Main");
        for (int i = 0; i < 120 && Stats.KillCount == kills; i++) yield return null;
        yield return Frames(6);   // the blow stays out for 0.05 s
        int standing = 0;
        foreach (EnemyAgent robot in _robots)
            if (!robot.Defeated) standing++;
        Assert.AreEqual(0, standing, "Robots on the path that the blow missed.");
        Assert.AreEqual(kills + crowd, Stats.KillCount);
        Assert.AreEqual(6f, Stats.Gauge, 1e-3f, "Seventy kills by the skill charge nothing.");
    }

    [UnityTest]
    public IEnumerator PauseHoldsTheSkillAndRetryClearsItWithoutGivingGaugeToTheNewRound()
    {
        yield return Arrange(0);
        Charge(13);
        Assert.AreEqual(100f, Stats.Gauge);
        SkinnedMeshRenderer[] body = _player.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        int shown = CountEnabled(body);
        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Windup");
        Assert.AreEqual(50f, Stats.Gauge);
        Assert.IsTrue(_feedback.AimShowing);

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float fill = _feedback.AimFill;
        yield return Frames(30);
        Assert.IsTrue(Logical().IsName("Skill1_Windup"), "The wind-up waits while the game is paused.");
        Assert.IsTrue(_feedback.AimShowing, "The lane holds while paused.");
        Assert.AreEqual(fill, _feedback.AimFill, 1e-4f, "And does not go on filling.");
        Assert.AreEqual(50f, Stats.Gauge);

        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        yield return Frames(3);
        Assert.IsFalse(_feedback.AimShowing, "Retry clears the markings.");
        Assert.IsFalse(_feedback.StreakShowing);
        Assert.AreEqual(0, _feedback.GhostsAlive);
        Assert.AreEqual(0f, Stats.Gauge, "The skill of the round before gives nothing back to the new round.");
        Assert.AreEqual(0, Stats.SkillUseCount);
        Assert.IsFalse(_player.IsInvulnerable);
        Assert.AreEqual(shown, CountEnabled(body), "The player is visible in the new round.");
        Assert.AreEqual(1f, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator RetryInTheMiddleOfTheDashBringsThePlayerBackVisibleAndVulnerable()
    {
        yield return Arrange(0);
        Charge(7);
        SkinnedMeshRenderer[] body = _player.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        int shown = CountEnabled(body);
        var controller = _player.GetComponent<CharacterController>();
        int excluded = controller.excludeLayers.value;
        yield return Press(Key.E);
        yield return WaitUntilEntering("Skill1_Dash");
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        int copies = _feedback.GhostsAlive;
        yield return Frames(20);
        Assert.IsTrue(Logical().IsName("Skill1_Dash"), "The pause caught the dash before it ended.");
        Assert.Less(CountEnabled(body), shown, "The player stays hidden while the game is paused in the middle of the dash.");
        Assert.IsTrue(_player.IsInvulnerable);
        Assert.Greater(copies, 0, "The copies left so far");
        Assert.AreEqual(copies, _feedback.GhostsAlive, "stay as they are while the game is paused.");

        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        yield return Frames(3);
        Assert.AreEqual(shown, CountEnabled(body), "Retry brings the player back.");
        Assert.IsFalse(_player.IsInvulnerable, "And the dash's safety does not carry into the new round.");
        Assert.AreEqual(excluded, controller.excludeLayers.value);
        Assert.IsFalse(_feedback.AimShowing);
        Assert.IsFalse(_feedback.StreakShowing);
        Assert.AreEqual(0, _feedback.GhostsAlive, "No copy of the round before is left.");
        Assert.AreEqual(0f, Stats.Gauge);
        Assert.AreEqual(0, Stats.SkillUseCount);
    }

    // ── helpers ──

    // Stops spawning and the enemies' AI, keeps "needed" robots to place by hand and removes the others.
    private IEnumerator Arrange(int needed)
    {
        // More robots than the round has brought in yet: the spawner is run ahead (its phases follow its own clock).
        for (int i = 0; i < 1200 && _enemies.ActiveEnemyCount < needed; i++)
        {
            _enemies.UpdateSpawning(0.25f, 0.25f, _state.Tick);
            if (i % 4 == 3) yield return null;
        }
        _enemies.StopProducing();
        _robots.Clear();
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
        {
            if (enemy.ActiveTick != _state.Tick || enemy.Defeated || enemy.State == EnemyAgentState.Pooled ||
                enemy.State == EnemyAgentState.Dying)
                continue;
            if (_robots.Count < needed)
            {
                _robots.Add(enemy);
                // Out of the way until it is placed.
                enemy.Activate(_player.transform.position + Vector3.right * (30f + 2f * _robots.Count), _state.Tick);
            }
            else
                _enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick, false, 9, true);
        }
        Assert.AreEqual(needed, _robots.Count, "Not enough robots in the arena.");
        yield return Frames(150);
        yield return WaitUntilIdle();
    }

    // The arena has benches and columns: stands the player where a dash of the whole length along "forward" meets
    // nothing (5 m back from the start of the round, a stretch other tests of this file dash through too).
    private IEnumerator MoveToClearLane(Vector3 forward)
    {
        var controller = _player.GetComponent<CharacterController>();
        Vector3 spot = _player.transform.position - forward * 5f;
        controller.enabled = false;
        _player.transform.position = spot;
        controller.enabled = true;
        Physics.SyncTransforms();
        yield return Frames(20);   // the camera catches up
        Vector3 feet = _player.transform.position;
        int walls = ~((1 << _player.gameObject.layer) | (1 << 8) | (1 << 2));
        bool blocked = Physics.CapsuleCast(feet + Vector3.up * (controller.radius + 0.1f),
            feet + Vector3.up * (controller.height - controller.radius), controller.radius, forward, out RaycastHit hit,
            DashMetres + 1f, walls, QueryTriggerInteraction.Ignore);
        Assert.IsFalse(blocked, "The arena changed: something stands in the test's dash lane" + (blocked ? " (" + hit.collider.name + ")." : "."));
    }

    // How far a robot stands from the line of a dash that began at "from" and went along "forward".
    private static float Lateral(EnemyAgent enemy, Vector3 from, Vector3 forward)
    {
        Vector3 offset = Flat(enemy.transform.position - from, false);
        return (offset - forward * Vector3.Dot(offset, forward)).magnitude;
    }

    private EnemyAgent Place(int index, Vector3 position, int hits = 1)
    {
        EnemyAgent enemy = _robots[index];
        enemy.Activate(position, _state.Tick, 0.5f, -1, hits);
        Physics.SyncTransforms();
        return enemy;
    }

    // Fills the gauge the way kills do: 8 for each (the score and the combo move with it).
    private void Charge(int kills)
    {
        for (int i = 0; i < kills; i++)
            Assert.IsTrue(Stats.RecordEnemyKill(_state.Tick, true, _combat.CombatTime));
    }

    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
        yield return Frames(2);
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
    }

    private IEnumerator Click()
    {
        InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left, true));
        yield return Frames(2);
        InputSystem.QueueStateEvent(_mouse, new MouseState());
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
        for (int i = 0; i < 400; i++)
        {
            yield return null;
            if (i > 5 && _animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && !_animator.IsInTransition(0)) yield break;
        }
        Assert.Fail("The player did not return to Idle.");
    }

    private static int CountEnabled(SkinnedMeshRenderer[] renderers)
    {
        int count = 0;
        foreach (SkinnedMeshRenderer renderer in renderers)
            if (renderer != null && renderer.enabled) count++;
        return count;
    }

    private static Vector3 Flat(Vector3 vector, bool normalize = true)
    {
        vector.y = 0f;
        return normalize ? vector.normalized : vector;
    }

    private static T Find<T>(Transform root, string objectName) where T : Component
    {
        foreach (T candidate in root.GetComponentsInChildren<T>(true))
            if (candidate.name == objectName) return candidate;
        return null;
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private IEnumerator WaitFor(GameState target)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != target && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(target, _state.CurrentState);
    }
}

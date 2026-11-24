using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

// The 2026-11-25 polish pass in the real scene: the pause menu's options (mouse sensitivity, volume) and controls list,
// the retry confirmation covering the menu instead of showing it through, Space/Enter on the start and result screens,
// and the robots going dark when the outro starts.
public sealed class UxPolishSceneTests
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private PlayerRuntime _player;
    private CoreBootstrap _bootstrap;
    private UIRoot _ui;
    private Mouse _mouse;
    private InputSettings.BackgroundBehavior _oldBackground;
#if UNITY_EDITOR
    private InputSettings.EditorInputBehaviorInPlayMode _oldEditorBehavior;
#endif
    private bool _hadLook, _hadVolume, _hadBest;
    private float _oldLook, _oldVolume, _oldListenerVolume;
    private int _oldBest;
    // The game's settings asset, changed by one test. Put back in TearDown: a failure inside a nested coroutine of a
    // Unity test skips the test's own finally.
    private CombatSettings _combatAsset;
    private float _oldRoundDuration;

    [SetUp]
    public void SetUp()
    {
        // Tests run without window focus; the simulated mouse must still reach the game.
        InputSettings settings = InputSystem.settings;
        _oldBackground = settings.backgroundBehavior;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        _oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        // The player's own saved options and best score on this machine are put back afterwards.
        _hadLook = PlayerPrefs.HasKey(GameOptions.LookScaleKey);
        _oldLook = PlayerPrefs.GetFloat(GameOptions.LookScaleKey, 1f);
        _hadVolume = PlayerPrefs.HasKey(GameOptions.VolumeKey);
        _oldVolume = PlayerPrefs.GetFloat(GameOptions.VolumeKey, 1f);
        _hadBest = PlayerPrefs.HasKey(PlayerPrefsBestScoreStore.Key);
        _oldBest = PlayerPrefs.GetInt(PlayerPrefsBestScoreStore.Key, 0);
        _oldListenerVolume = AudioListener.volume;
        PlayerPrefs.DeleteKey(GameOptions.LookScaleKey);
        PlayerPrefs.DeleteKey(GameOptions.VolumeKey);
        _combatAsset = Resources.Load<CombatSettings>("CombatSettings");
        _oldRoundDuration = _combatAsset.roundDuration;
    }

    [TearDown]
    public void TearDown()
    {
        _combatAsset.roundDuration = _oldRoundDuration;
        if (_mouse != null) InputSystem.RemoveDevice(_mouse);
        _mouse = null;
        InputSettings settings = InputSystem.settings;
        settings.backgroundBehavior = _oldBackground;
#if UNITY_EDITOR
        settings.editorInputBehaviorInPlayMode = _oldEditorBehavior;
#endif
        Restore(GameOptions.LookScaleKey, _hadLook, _oldLook);
        Restore(GameOptions.VolumeKey, _hadVolume, _oldVolume);
        if (_hadBest) PlayerPrefs.SetInt(PlayerPrefsBestScoreStore.Key, _oldBest);
        else PlayerPrefs.DeleteKey(PlayerPrefsBestScoreStore.Key);
        PlayerPrefs.Save();
        AudioListener.volume = _oldListenerVolume;
        Time.timeScale = 1f;
    }

    private static void Restore(string key, bool had, float value)
    {
        if (had) PlayerPrefs.SetFloat(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    [UnityTest]
    public IEnumerator ThePauseMenuOffersOptionsAndControlsAndTheRetryConfirmationCoversIt()
    {
        yield return Load();
        yield return EnterBattle();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);

        PauseView pause = _ui.PauseScreen;
        Transform root = pause.transform;
        // AGENTS.md: the pause menu always has Resume, Retry and Quit.
        foreach (string button in new[] { "ResumeButton", "RetryButton", "QuitButton" })
            Assert.IsTrue(Find<Button>(root, button).gameObject.activeInHierarchy, button);
        Assert.IsTrue(pause.HasOptions, "The options panel is wired.");
        Assert.IsTrue(Find<RectTransform>(root, "OptionsPanel").gameObject.activeInHierarchy);
        Assert.AreEqual("100%", Find<TMP_Text>(root, "LookValue").text);
        Assert.AreEqual("100%", Find<TMP_Text>(root, "VolumeValue").text);
        Assert.IsFalse(Find<Button>(root, "VolumeUpButton").interactable, "100% is the loudest.");
        Assert.IsTrue(Find<Button>(root, "VolumeDownButton").interactable);
        // A clicked button must not stay selected: Enter is also the UI's Submit key and would press it again later
        // (twice on START, a dialogue line skipped, RETRY before the result has counted up).
        foreach (Selectable selectable in _ui.GetComponentsInChildren<Selectable>(true))
            Assert.AreEqual(Navigation.Mode.None, selectable.navigation.mode, selectable.name + " can be selected.");
        Assert.IsNull(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject);
        string keys = Find<TMP_Text>(root, "ControlKeys").text, actions = Find<TMP_Text>(root, "ControlActions").text;
        Assert.AreEqual(keys.Split('\n').Length, actions.Split('\n').Length, "Every key has its action.");
        foreach (string key in new[] { "WASD", "MOUSE", "LMB", "SHIFT", "Q", "E", "ESC" })
            StringAssert.Contains(key, keys);
        Assert.IsTrue(pause.IsMenuVisible);

        // The confirmation is see-through: the menu behind it is hidden while it is open, the dimming stays.
        Find<Button>(root, "RetryButton").onClick.Invoke();
        yield return null;
        RetryConfirmationView confirmation = _ui.RetryConfirmationScreen;
        Assert.IsTrue(confirmation.gameObject.activeSelf);
        Assert.IsFalse(pause.IsMenuVisible);
        Assert.IsTrue(pause.gameObject.activeSelf, "The pause screen itself stays, for its dimming.");
        Assert.IsTrue(Find<Image>(root, "DimmedOverlay").gameObject.activeInHierarchy);
        foreach (string hidden in new[] { "ResumeButton", "RetryButton", "QuitButton", "OptionsPanel", "ControlsPanel", "MenuPanel" })
            Assert.IsFalse(Find<Transform>(root, hidden).gameObject.activeInHierarchy, hidden + " would show through the confirmation.");

        Find<Button>(confirmation.transform, "CancelButton").onClick.Invoke();
        yield return null;
        Assert.AreEqual(GameState.Paused, _state.CurrentState);
        Assert.IsFalse(confirmation.gameObject.activeSelf);
        Assert.IsTrue(pause.IsMenuVisible, "Cancel brings the menu back.");
        Assert.IsTrue(Find<Button>(root, "ResumeButton").gameObject.activeInHierarchy);

        // Confirmed: the menu does not flash back before the reset, and the next pause shows it whole again.
        Find<Button>(root, "RetryButton").onClick.Invoke();
        yield return null;
        Find<Button>(confirmation.transform, "ConfirmButton").onClick.Invoke();
        Assert.IsFalse(pause.IsMenuVisible, "No flash of the menu between the confirmation and the reset.");
        yield return WaitFor(GameState.Ready);
        yield return EnterBattle();
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.IsTrue(pause.IsMenuVisible);
        Assert.IsTrue(Find<Button>(root, "ResumeButton").gameObject.activeInHierarchy);
        Assert.IsTrue(Find<RectTransform>(root, "ControlsPanel").gameObject.activeInHierarchy);
    }

    [UnityTest]
    public IEnumerator OptionStepsChangeTheCameraSensitivityAndTheVolumeAndAreKept()
    {
        yield return Load();
        yield return EnterBattle();
        _mouse = InputSystem.AddDevice<Mouse>("UxPolishTestMouse");
        yield return Frames(3);
        Assert.AreEqual(100, _bootstrap.Options.LookPercent);
        Assert.AreEqual(1f, _player.LookSensitivityScale);
        Assert.AreEqual(1f, AudioListener.volume);
        float before = 0f;
        yield return Turn(100f, degrees => before = degrees);
        Assert.Greater(before, 1f, "The simulated mouse must turn the camera for this test to mean anything.");

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Transform root = _ui.PauseScreen.transform;
        for (int i = 0; i < 5; i++) Find<Button>(root, "LookUpButton").onClick.Invoke();
        for (int i = 0; i < 3; i++) Find<Button>(root, "VolumeDownButton").onClick.Invoke();
        Assert.AreEqual("150%", Find<TMP_Text>(root, "LookValue").text);
        Assert.AreEqual("70%", Find<TMP_Text>(root, "VolumeValue").text);
        Assert.AreEqual(1.5f, _player.LookSensitivityScale, 0.0001f);
        Assert.AreEqual(0.7f, AudioListener.volume, 0.0001f);
        Assert.AreEqual(1.5f, PlayerPrefs.GetFloat(GameOptions.LookScaleKey, -1f), 0.0001f, "Saved at once.");
        Assert.AreEqual(0.7f, PlayerPrefs.GetFloat(GameOptions.VolumeKey, -1f), 0.0001f);
        Assert.IsTrue(Find<Button>(root, "VolumeUpButton").interactable, "Below 100% the volume can go up again.");
        Assert.AreEqual(GameState.Paused, _state.CurrentState, "Changing an option is not a state change.");

        Find<Button>(root, "ResumeButton").onClick.Invoke();
        yield return WaitFor(GameState.Battle);
        yield return Frames(2);
        float after = 0f;
        yield return Turn(100f, degrees => after = degrees);
        Assert.AreEqual(before * 1.5f, after, 0.6f, "The same mouse travel turns the camera 1.5 times as far.");

        // A retry keeps the options, and the next run reads them back.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(150, _bootstrap.Options.LookPercent);
        Assert.AreEqual(1.5f, _player.LookSensitivityScale, 0.0001f);
        Assert.AreEqual(0.7f, AudioListener.volume, 0.0001f);

        AudioListener.volume = 1f;
        yield return Load();
        Assert.AreEqual(150, _bootstrap.Options.LookPercent, "A new run starts with the saved sensitivity.");
        Assert.AreEqual(70, _bootstrap.Options.VolumePercent);
        Assert.AreEqual(1.5f, _player.LookSensitivityScale, 0.0001f);
        Assert.AreEqual(0.7f, AudioListener.volume, 0.0001f);
    }

    [UnityTest]
    public IEnumerator SpaceOrEnterPressesTheStartAndRetryButtonsButNotBeforeTheResultHasCountedUp()
    {
        yield return Load();
        var transition = Object.FindFirstObjectByType<ScreenTransition>();
        float until = Time.realtimeSinceStartup + 5f;
        while (transition != null && transition.BlocksInput && Time.realtimeSinceStartup < until) yield return null;
        UiLayer layer = Layer();

        Confirm(layer);
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        Confirm(layer);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        Confirm(layer);
        yield return Frames(3);
        Assert.AreEqual(GameState.Battle, _state.CurrentState, "The key does nothing in battle.");

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);

        // The key that was skipping the outro arrives here too: the result must be seen first.
        ResultView result = _ui.ResultScreen;
        Confirm(layer);
        yield return Frames(5);
        Assert.AreEqual(GameState.Result, _state.CurrentState, "No retry while the numbers are still counting up.");
        float countUp = Mathf.Max(result.ScoreCountSeconds, result.StatDelaySeconds + result.StatStaggerSeconds * 3f + result.StatCountSeconds);
        until = Time.realtimeSinceStartup + countUp + 1.5f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Assert.AreEqual(GameState.Result, _state.CurrentState);
        Confirm(layer);
        yield return WaitFor(GameState.Ready);
    }

    [UnityTest]
    public IEnumerator RobotsGoDarkWhenTheOutroStartsAndComeBackLitNextRound()
    {
        yield return Load();
        EnemySettings settings = _enemies.Settings;
        Assert.IsTrue(settings.PowerDownAtRoundEnd);
        Color dark = settings.PoweredDownColor;
        float peak = Mathf.Max(dark.r, Mathf.Max(dark.g, dark.b)), low = Mathf.Min(dark.r, Mathf.Min(dark.g, dark.b));
        Assert.LessOrEqual((peak - low) / peak, 0.25f, "Too much hue: the enemy shader would still light the visor and chest.");
        yield return EnterBattle();
        yield return Frames(10);
        Assert.Greater(_enemies.Perf.ActiveEnemies.Count, 1);
        // One robot is already falling when the round ends: it keeps its own look.
        EnemyAgent falling = _enemies.Perf.ActiveEnemies[0];
        Assert.IsTrue(_enemies.TryApplyHit(falling, Vector3.forward, 1f, _state.Tick));

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            if (!enemy.Defeated) Assert.IsTrue(enemy.LightsOn, "Still lit through the slow motion.");
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        int standing = 0;
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
        {
            if (enemy.Defeated) continue;
            standing++;
            Assert.IsFalse(enemy.LightsOn, "The reboot erased the orders: lights out.");
            AssertColor(dark, ShownColor(enemy));
        }
        Assert.Greater(standing, 0);

        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
            if (!enemy.Defeated) Assert.IsFalse(enemy.LightsOn, "Still dark behind the result screen.");

        Find<Button>(_ui.ResultScreen.transform, "RetryButton").onClick.Invoke();
        yield return WaitFor(GameState.Ready);
        Assert.Greater(_enemies.Perf.ActiveEnemies.Count, 0);
        foreach (EnemyAgent enemy in _enemies.Perf.ActiveEnemies)
        {
            Assert.IsTrue(enemy.LightsOn, "The next round's robots are lit again.");
            AssertColor(enemy.SkinColor, ShownColor(enemy));
        }
    }

    // The start screen shows the best score once there is one, and a new record made in a round shows up on return.
    [UnityTest]
    public IEnumerator TheStartScreenShowsTheBestScoreOnceThereIsOne()
    {
        PlayerPrefs.DeleteKey(PlayerPrefsBestScoreStore.Key);   // put back in TearDown
        yield return Load();
        Transform start = _ui.StartScreen.transform;
        Assert.IsFalse(Find<RectTransform>(start, "BestScoreRow").gameObject.activeSelf, "No record yet: no BEST line.");

        // One kill and the round ends: the result screen saves the record, and the start screen shows it after RETRY.
        yield return EnterBattle();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        Assert.IsTrue(_enemies.TryApplyHit(_enemies.Perf.ActiveEnemies[0], Vector3.forward, 1f, _state.Tick));
        int score = combat.Stats.Score;
        Assert.Greater(score, 0);
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        Find<Button>(_ui.ResultScreen.transform, "RetryButton").onClick.Invoke();
        yield return WaitFor(GameState.Ready);
        Assert.IsTrue(Find<RectTransform>(start, "BestScoreRow").gameObject.activeSelf);
        Assert.AreEqual(score.ToString("D6"), Find<TMP_Text>(start, "BestScoreValue").text);
        Assert.AreEqual("BEST", Find<TMP_Text>(start, "BestScoreLabel").text);

        // The next run reads it back.
        yield return Load();
        start = _ui.StartScreen.transform;
        Assert.IsTrue(Find<RectTransform>(start, "BestScoreRow").gameObject.activeSelf);
        Assert.AreEqual(score.ToString("D6"), Find<TMP_Text>(start, "BestScoreValue").text);
    }

    // Safety net: a gap in the level's colliders must not end a round in an endless fall.
    [UnityTest]
    public IEnumerator APlayerThatFallsOutOfTheLevelIsPutBackOnTheSpawnPoint()
    {
        yield return Load();
        Vector3 spawn = _player.transform.position;
        yield return EnterBattle();
        yield return Frames(5);
        Assert.AreEqual(0, _player.FallRecoveries, "Standing on the floor is not a fall.");
        var controller = _player.GetComponent<CharacterController>();
        controller.enabled = false;
        _player.transform.position = spawn + Vector3.down * 200f;   // far below every floor
        controller.enabled = true;
        float until = Time.realtimeSinceStartup + 5f;
        while (_player.FallRecoveries == 0 && Time.realtimeSinceStartup < until) yield return null;
        Assert.AreEqual(1, _player.FallRecoveries);
        yield return Frames(30);
        Assert.Less(Vector3.Distance(spawn, _player.transform.position), 1.5f, "Back on the spawn point, standing.");
        Assert.AreEqual(GameState.Battle, _state.CurrentState, "The round goes on.");
        Assert.AreEqual(1, _player.FallRecoveries, "...and it stays there.");
    }

    // The round ends the way it began: a tick on each of its last three seconds, then TIME UP.
    [UnityTest]
    public IEnumerator TheLastThreeSecondsTickLikeTheCountdown()
    {
        _combatAsset.roundDuration = 4.4f;   // restored in TearDown
        yield return Load();
        var sfx = Object.FindFirstObjectByType<SfxRuntime>();
        var combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        Assert.IsNotNull(sfx);
        yield return EnterBattle();
        int before = sfx.PlayCount(SfxCue.CountdownTick);
        float until = Time.realtimeSinceStartup + 10f;
        while (combat.RoundRemaining > 3.4f && _state.CurrentState == GameState.Battle && Time.realtimeSinceStartup < until)
        {
            Assert.AreEqual(before, sfx.PlayCount(SfxCue.CountdownTick), "No tick while more than three seconds are left.");
            yield return null;
        }
        yield return WaitFor(GameState.Finishing);
        Assert.AreEqual(before + 3, sfx.PlayCount(SfxCue.CountdownTick), "3, 2, 1");

        // The next round's timer jumping back up is not a countdown.
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        _combatAsset.roundDuration = _oldRoundDuration;
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        yield return EnterBattle();
        // (The countdown before the battle may have ticked on its own "3".)
        int fresh = sfx.PlayCount(SfxCue.CountdownTick);
        yield return Frames(10);
        Assert.AreEqual(fresh, sfx.PlayCount(SfxCue.CountdownTick), "A fresh round's full timer does not tick.");
    }

    // The pool is filled while the game loads, so a mass spawn in the middle of a round instantiates nothing.
    [UnityTest]
    public IEnumerator EveryRobotIsCreatedWhileLoadingAndAMassSpawnCreatesNone()
    {
        yield return Load();
        int maximum = _enemies.Settings.MaximumEnemyCount;
        Assert.AreEqual(maximum, _enemies.PooledEnemyCount, "Ready: every robot a round can hold already exists.");
        EnemySettings original = _enemies.Settings;
        EnemySettings settings = Object.Instantiate(original);
        typeof(EnemySettings).GetField("spawnPhases", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, new[]
        {
            new EnemySpawnPhase(0f, 12, 1f, 2, 0, 0, 0f),
            new EnemySpawnPhase(1f, 60, 1f, 2, 30, 0, 0f)
        });
        FieldInfo settingsField = typeof(EnemyRuntime).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
        settingsField.SetValue(_enemies, settings);
        try
        {
            yield return EnterBattle();
            float until = Time.realtimeSinceStartup + 20f;
            while (_enemies.BattleSeconds < 1.3f + settings.SpawnWarningSeconds && Time.realtimeSinceStartup < until) yield return null;
            Assert.GreaterOrEqual(_enemies.Perf.ActiveEnemies.Count, 30, "The mass spawn has arrived.");
            Assert.AreEqual(maximum, _enemies.PooledEnemyCount, "...out of the pool: nothing was instantiated for it.");
        }
        finally
        {
            settingsField.SetValue(_enemies, original);
            Object.Destroy(settings);
        }
    }

    // ---------------------------------------------------------------- helpers

    private IEnumerator Turn(float pixels, System.Action<float> report)
    {
        Camera camera = Camera.main;
        float yaw = camera.transform.eulerAngles.y;
        InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(pixels, 0f) });
        yield return Frames(4);
        report(Mathf.Abs(Mathf.DeltaAngle(yaw, camera.transform.eulerAngles.y)));
    }

    private UiLayer Layer() =>
        (UiLayer)typeof(CoreBootstrap).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bootstrap);

    // The Space/Enter handler of the UI layer (the keyboard is read in UiLayer.Update).
    private static void Confirm(UiLayer layer) =>
        typeof(UiLayer).GetMethod("HandleConfirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);

    private static Color ShownColor(EnemyAgent enemy)
    {
        var block = new MaterialPropertyBlock();
        enemy.GetComponentInChildren<SkinnedMeshRenderer>(true).GetPropertyBlock(block);
        return block.GetColor(BaseColorId);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.01f);
        Assert.AreEqual(expected.g, actual.g, 0.01f);
        Assert.AreEqual(expected.b, actual.b, 0.01f);
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
        _bootstrap = Object.FindFirstObjectByType<CoreBootstrap>();
        _ui = Object.FindFirstObjectByType<UIRoot>();
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

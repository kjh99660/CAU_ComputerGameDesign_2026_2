using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 실제 Scene에서 본 HUD 추가 요소: 콤보 표시(전투와 종료 연출), 전투 중 무전 자막(50초·10초), 게이지 옆 E·Q 키 표시,
// 결과 화면의 최고 점수, 성과에 반응하는 마무리의 Retry 처리, 시작 화면의 조작 안내, 그리고 서로 겹치지 않는 배치.
// 모두 Pause에서 멈추고 Retry 뒤에 남지 않아야 한다.
public sealed class HudAdditionsSceneTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameStateManager _state;
    private EventManager _events;
    private CombatRoundRuntime _combat;
    private EnemyRuntime _enemies;
    private CombatSettings _settings;
    private float _oldRoundDuration;
    private float _oldComboStep;
    private float _oldComboMaxBonus;
    private bool _oldComboCountsSpecialKills;
    private int _oldCaptureFramerate;

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
        _settings = Resources.Load<CombatSettings>("CombatSettings");
        _oldRoundDuration = _settings.roundDuration;
        _oldComboStep = _settings.comboScoreStep;
        _oldComboMaxBonus = _settings.comboScoreMaxBonus;
        _oldComboCountsSpecialKills = _settings.comboCountsSpecialKills;
    }

    [TearDown]
    public void TearDown()
    {
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        _settings.roundDuration = _oldRoundDuration;
        _settings.comboScoreStep = _oldComboStep;
        _settings.comboScoreMaxBonus = _oldComboMaxBonus;
        _settings.comboCountsSpecialKills = _oldComboCountsSpecialKills;
    }

    [UnityTest]
    public IEnumerator ComboShowsFromTwoKillsPopsDrainsAndFadesWhenItEnds()
    {
        yield return Load();
        yield return EnterBattle();
        // This test follows the plain counter; the game turns the combo bonus on (TearDown restores the asset).
        _settings.comboScoreStep = 0f;
        HoldCrowdHits();
        var battle = Object.FindFirstObjectByType<BattleView>();
        var combo = Find<HudComboCounter>(battle.transform, "ComboCounter");
        Assert.IsFalse(combo.IsShowing, "No combo at the start of the battle.");
        foreach (Graphic graphic in combo.GetComponentsInChildren<Graphic>(true))
            Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take clicks.");

        // 실제 적중 경로(EnemyRuntime)로 쓰러뜨린 적도 콤보로 센다. 한 번은 아직 콤보가 아니다.
        EnemyAgent enemy = Object.FindFirstObjectByType<EnemyAgent>();
        Assert.IsNotNull(enemy, "Ready spawns enemies.");
        Assert.IsTrue(_enemies.TryApplyHit(enemy, Vector3.forward, 1f, _state.Tick));
        Assert.AreEqual(1, _combat.Stats.ComboAt(_combat.CombatTime));
        Assert.IsFalse(combo.IsShowing, "One kill is not a combo yet.");
        Kill(1);
        Assert.IsTrue(combo.IsShowing, "The second kill inside the window shows the combo.");
        Assert.AreEqual("2", combo.CountShown);
        Assert.AreEqual("COMBO", combo.LabelShown, "The combo score bonus is off, so no multiplier is shown.");
        Assert.Greater(combo.CountScale, 1.2f, "The number pops when it goes up.");
        Assert.AreEqual(1f, combo.TimeFillNormalized, 0.02f, "The time bar starts full.");

        // 프레임은 1/60초로 고정되어 있다(captureFramerate). 15프레임 = 0.25초 뒤.
        yield return Frames(15);
        Assert.AreEqual(1f, combo.CountScale, 0.001f, "The pop settles after a moment.");
        Assert.Less(combo.TimeFillNormalized, 0.95f, "The bar drains with the round clock.");
        Assert.Greater(combo.TimeFillNormalized, 0.8f);
        Assert.IsTrue(combo.IsShowing);

        // Pause는 콤보와 막대를 그대로 붙잡는다.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedFill = combo.TimeFillNormalized;
        yield return new WaitForSecondsRealtime(0.25f);
        Assert.IsTrue(combo.IsShowing, "The combo stays on screen while paused.");
        Assert.AreEqual(pausedFill, combo.TimeFillNormalized, 1e-5f, "The combo bar holds still while paused.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        Kill(1);
        Assert.AreEqual("3", combo.CountShown);
        Assert.Greater(combo.CountScale, 1.2f, "Each increase pops again.");
        Assert.AreEqual(1f, combo.TimeFillNormalized, 0.02f, "A kill refills the bar.");

        // 창(2초)이 끝나면 막대가 경고색이 되었다가 콤보가 빠르게 사라진다.
        bool sawLowTime = false, sawFade = false;
        for (int frame = 0; frame < 400 && combo.IsShowing; frame++)
        {
            sawLowTime |= !combo.IsFading && combo.TimeFillColor.r > 0.9f && combo.TimeFillColor.g < 0.5f;
            sawFade |= combo.IsFading && combo.Alpha < 0.99f;
            yield return null;
        }
        Assert.IsFalse(combo.IsShowing, "The combo disappears once its window has passed.");
        Assert.IsTrue(sawLowTime, "The bar turns to the alert colour shortly before the combo drops.");
        Assert.IsTrue(sawFade, "The combo fades out instead of vanishing at once.");
        Assert.AreEqual(0, _combat.Stats.ComboAt(_combat.CombatTime));

        // 플레이어가 맞으면 콤보가 바로 사라지기 시작한다.
        Kill(2);
        Assert.IsTrue(combo.IsShowing);
        HoldCrowdHits(false);
        Assert.IsTrue(_combat.Stats.RecordPlayerHit(_state.Tick, _combat.CombatTime));
        HoldCrowdHits();
        Assert.IsTrue(combo.IsFading, "A hit on the player starts the fade at once.");
        yield return Frames(24);
        Assert.IsFalse(combo.IsShowing);

        // Retry 뒤에는 이전 콤보가 남지 않는다. Ready에서는 전투 화면 전체가 꺼져 있으므로 보이는지가 아니라 표시 자체를
        // 본다. 지워지지 않고 사라지는 중으로 남으면 다음 전투가 시작될 때 이전 숫자가 잠깐 번쩍인다.
        Kill(3);
        Assert.IsTrue(combo.IsShowing);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.IsFalse(combo.gameObject.activeSelf, "Retry clears the combo itself, not only the battle screen around it.");
        Assert.IsFalse(combo.IsFading, "No fade is left over for the next battle.");
        Assert.AreEqual(1f, combo.Alpha);
        Assert.AreEqual(0, combo.ShownCount);
        Assert.AreEqual(1f, combo.CountScale, 1e-4f);
        Assert.AreEqual(1f, Time.timeScale);
        yield return EnterBattle();
        for (int frame = 0; frame < 5; frame++)
        {
            Assert.IsFalse(combo.IsShowing, "The next battle starts without the previous combo.");
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ComboBonusShowsItsMultiplierWhenTurnedOn()
    {
        _settings.comboScoreStep = 0.25f;
        yield return Load();
        yield return EnterBattle();
        HoldCrowdHits();
        var combo = Find<HudComboCounter>(Object.FindFirstObjectByType<BattleView>().transform, "ComboCounter");
        int baseScore = _settings.hitScore + _settings.killScore;
        Kill(2);
        Assert.AreEqual(baseScore + RoundCombatStats.ComboKillScore(baseScore, 2, 0.25f, _settings.comboScoreMaxBonus), _combat.Stats.Score);
        Assert.AreEqual("x1.25  COMBO", combo.LabelShown);
        Kill(1);
        Assert.AreEqual("x1.50  COMBO", combo.LabelShown);
        Assert.AreEqual("3", combo.CountShown);
    }

    [UnityTest]
    public IEnumerator ComboCarriesIntoFinishingInThePlaceItHadInBattle()
    {
        yield return Load();
        yield return EnterBattle();
        HoldCrowdHits();
        var battle = Object.FindFirstObjectByType<BattleView>();
        var battleCombo = Find<HudComboCounter>(battle.transform, "ComboCounter");
        Kill(3);
        Assert.IsTrue(battleCombo.IsShowing);
        yield return Frames(15);
        Rect battleRect = WorldRect((RectTransform)battleCombo.transform);

        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        try
        {
            var finishing = Object.FindFirstObjectByType<FinishingView>();
            var finishingCombo = Find<HudComboCounter>(finishing.transform, "ComboCounter");
            Assert.IsFalse(battleCombo.gameObject.activeInHierarchy, "The battle HUD is hidden while finishing.");
            Assert.IsTrue(finishingCombo.IsShowing, "The combo stays on screen while TIME UP shows.");
            Assert.AreEqual("3", finishingCombo.CountShown);
            Assert.AreEqual(1f, finishingCombo.CountScale, 1e-3f, "Switching screens does not pop the number again.");
            Rect finishingRect = WorldRect((RectTransform)finishingCombo.transform);
            Assert.Less(Vector2.Distance(battleRect.min, finishingRect.min), 0.5f, "The combo stays where the battle HUD showed it.");
            Assert.Less(Vector2.Distance(battleRect.max, finishingRect.max), 0.5f);
            Assert.IsFalse(finishingRect.Overlaps(WorldRect(Find<RectTransform>(finishing.transform, "TimeUpFrame"))), "The combo must not cover TIME UP.");

            // 100초 전에 시작한 공격이 종료 연출 중에 맞히면 콤보가 이어지고 다시 튄다.
            Kill(1);
            Assert.AreEqual("4", finishingCombo.CountShown);
            Assert.Greater(finishingCombo.CountScale, 1.2f);

            // 라운드가 끝나면 콤보도 끝난다. 두 화면이 모두 꺼져 있으므로 보이는지가 아니라 표시 자체가 지워졌는지 본다.
            _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
            yield return WaitFor(GameState.OutroDialogue);
            Assert.IsFalse(finishingCombo.gameObject.activeSelf, "The combo ends with the round: the finishing screen does not keep it.");
            Assert.AreEqual(0, finishingCombo.ShownCount);
            Assert.IsFalse(finishingCombo.IsFading);
            Assert.IsFalse(battleCombo.gameObject.activeSelf, "Nor does the hidden battle HUD.");
            Assert.AreEqual(0, battleCombo.ShownCount);
            _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
            yield return WaitFor(GameState.Result);
            _events.EnqueueRequest(new RestartRequest(_state.Tick));
            yield return WaitFor(GameState.Ready);
            Assert.IsFalse(finishingCombo.gameObject.activeSelf, "Nothing brings the finishing combo back after Retry.");
            Assert.IsFalse(battleCombo.gameObject.activeSelf);
        }
        finally
        {
            Time.timeScale = 1f;
        }
    }

    // 실제 특수 공격 경로(CombatRoundRuntime.TryUseSpecial → EnemyRuntime.ApplySpecial)에서 특수 공격 자신의 처치는
    // CombatSettings.comboCountsSpecialKills를 따른다. 기본값: 이어 오던 콤보가 끊기고 그 처치들이 새 콤보로 바로 보이며,
    // 보너스가 켜져 있으면 처치마다 그 콤보의 배율을 받는다. 끄면: 기본 점수만 받고 콤보를 만들지 않아 표시가 사라진다.
    [UnityTest]
    public IEnumerator TheSpecialsOwnKillsFollowTheComboSetting()
    {
        _settings.comboScoreStep = 0.25f;
        _settings.comboScoreMaxBonus = 1f;
        Assert.IsTrue(_settings.comboCountsSpecialKills, "The game asset keeps the default rule.");
        yield return Load();
        yield return EnterBattle();
        HoldCrowdHits();
        var combo = Find<HudComboCounter>(Object.FindFirstObjectByType<BattleView>().transform, "ComboCounter");
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        int baseScore = _settings.hitScore + _settings.killScore;

        Kill(13);   // fills the gauge
        Assert.AreEqual("13", combo.CountShown);
        PlaceEnemiesAround(player.transform.position, 3);
        int kills = _combat.Stats.KillCount, score = _combat.Stats.Score;
        Assert.IsTrue(_combat.TryUseSpecial(_state.Tick, player.transform.position));
        int special = _combat.Stats.KillCount - kills;
        Assert.GreaterOrEqual(special, 3, "The special kills the enemies placed next to the player.");
        int expected = 0;
        for (int n = 1; n <= special; n++) expected += RoundCombatStats.ComboKillScore(baseScore, n, 0.25f, 1f);
        Assert.AreEqual(expected, _combat.Stats.Score - score, "Each special kill scores with the multiplier of the combo it makes.");
        Assert.AreEqual(special, _combat.Stats.ComboAt(_combat.CombatTime), "The special ends the 13 combo and its kills start a new one.");
        Assert.IsTrue(combo.IsShowing);
        Assert.IsFalse(combo.IsFading);
        Assert.AreEqual(special.ToString(), combo.CountShown, "The counter shows the special's own combo at once.");
        Assert.Greater(combo.CountScale, 1.2f);

        // 설정을 끈 새 판: 특수 공격의 처치는 기본 점수만 받고, 이어 오던 콤보는 끊긴 채 사라진다.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _settings.comboCountsSpecialKills = false;
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        yield return EnterBattle();
        HoldCrowdHits();
        Kill(13);
        Assert.IsTrue(combo.IsShowing);
        PlaceEnemiesAround(player.transform.position, 3);
        kills = _combat.Stats.KillCount;
        score = _combat.Stats.Score;
        Assert.IsTrue(_combat.TryUseSpecial(_state.Tick, player.transform.position));
        special = _combat.Stats.KillCount - kills;
        Assert.GreaterOrEqual(special, 3);
        Assert.AreEqual(special * baseScore, _combat.Stats.Score - score, "Left out of the combo, each special kill scores the base value.");
        Assert.AreEqual(0, _combat.Stats.ComboAt(_combat.CombatTime), "The special's kills start no combo.");
        Assert.AreEqual(13, _combat.Stats.MaxCombo, "Max Combo keeps the combo from before the special.");
        Assert.IsTrue(combo.IsFading, "Using the special ends the combo and the counter fades.");
        yield return Frames(24);
        Assert.IsFalse(combo.IsShowing);
    }

    [UnityTest]
    public IEnumerator RadioLinesPlayAtFiftyAndTenSecondsHoldInPauseAndClearOnRetry()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        BattleRadioLine half = Line(data, 50f), last = Line(data, 10f);

        // 51초 판: 50초 줄이 1초쯤 뒤에 뜬다.
        _settings.roundDuration = 51f;
        yield return Load();
        yield return EnterBattle();
        var battle = Object.FindFirstObjectByType<BattleView>();
        var radio = Find<HudRadioSubtitle>(battle.transform, "RadioSubtitle");
        Assert.IsFalse(radio.IsShowing, "No radio at the start of the battle.");
        yield return UntilFrames(() => radio.IsShowing, 180);
        Assert.IsTrue(radio.IsShowing, "The 50 s line plays when the clock reaches 50 s.");
        Assert.LessOrEqual(_combat.RoundRemaining, 50f);
        Assert.Greater(_combat.RoundRemaining, 49f, "It plays the moment the clock passes 50 s.");
        Assert.AreEqual(half.speaker, radio.SpeakerShown);
        Assert.AreEqual(half.text, radio.TextShown);

        // 한글은 대화 화면과 같은 TMP 폰트로 그려지고, 빠진 글자가 없다. 클릭을 가로채지 않는다.
        var dialogue = Object.FindFirstObjectByType<DialogueView>(FindObjectsInactive.Include);
        TMP_FontAsset dialogueFont = Find<TMP_Text>(dialogue.transform, "BodyText").font;
        Assert.AreSame(dialogueFont, radio.Font, "The radio reuses the dialogue's Korean TMP font asset.");
        Assert.IsTrue(radio.Font.HasCharacters(half.speaker + half.text + last.text, out List<char> missing),
            "Missing glyphs: " + (missing == null ? string.Empty : new string(missing.ToArray())));
        Assert.IsFalse(radio.GetComponent<CanvasGroup>().blocksRaycasts);
        foreach (Graphic graphic in radio.GetComponentsInChildren<Graphic>(true))
            Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take clicks.");

        // Pause 동안 그대로 머문다.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedAt = radio.Elapsed;
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.IsTrue(radio.IsShowing, "The radio holds while paused.");
        Assert.AreEqual(pausedAt, radio.Elapsed, 1e-5f);
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        // 몇 초 뒤 스스로 사라지고, 같은 판에서 다시 뜨지 않는다(다음 줄은 10초에야 뜬다).
        int shownFrames = Mathf.CeilToInt((radio.TotalSeconds - radio.Elapsed) * 60f);
        yield return UntilFrames(() => !radio.IsShowing, shownFrames + 30);
        Assert.IsFalse(radio.IsShowing, "The radio line goes away by itself.");
        Assert.Greater(_combat.RoundRemaining, 40f, "It went away after a few seconds, long before the 10 s line.");
        yield return Frames(30);
        Assert.IsFalse(radio.IsShowing);

        // 11초 판(Retry): 50초 줄은 뜨지 않고 10초 줄만 뜬다. 그때 시간 표시는 10초 경고 중이고 자막과 겹치지 않는다.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _settings.roundDuration = 11f;
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.IsFalse(radio.IsShowing);
        yield return EnterBattle();
        yield return UntilFrames(() => radio.IsShowing, 180);
        Assert.IsTrue(radio.IsShowing, "The 10 s line plays.");
        Assert.AreEqual(last.text, radio.TextShown, "A round shorter than 50 s never plays the 50 s line.");
        TMP_Text timer = Find<TMP_Text>(battle.transform, "TimerValue");
        Assert.AreEqual("00:10", timer.text);
        AssertColor(UiPalette.Alert, timer.color);
        Rect radioRect = WorldRect((RectTransform)radio.transform);
        Assert.IsFalse(radioRect.Overlaps(WorldRect(Find<RectTransform>(battle.transform, "TimerPanel"))),
            "The radio must not sit on the 10-second timer warning.");

        // 전투가 끝나면 보이지 않고, Retry 뒤에는 지워져 있다.
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        Assert.IsFalse(radio.gameObject.activeInHierarchy, "No radio outside the battle.");
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
        Assert.IsFalse(radio.gameObject.activeInHierarchy);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.IsFalse(radio.IsShowing, "Retry clears the radio.");
        Assert.IsFalse(radio.gameObject.activeSelf);
        Assert.AreEqual(1f, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator SpecialKeyLightsWhileTheGaugeIsFullAndSkillKeyStaysReady()
    {
        yield return Load();
        yield return EnterBattle();
        var battle = Object.FindFirstObjectByType<BattleView>();
        var special = Find<HudKeyBadge>(battle.transform, "SpecialKeyBadge");
        var skill = Find<HudKeyBadge>(battle.transform, "SkillKeyBadge");
        Assert.AreEqual("E", Find<TMP_Text>(special.transform, "Key").text);
        Assert.AreEqual("Q", Find<TMP_Text>(skill.transform, "Key").text);
        Assert.AreEqual(HudKeyBadge.State.Dim, special.Current, "E is dim while the gauge is not full.");
        Assert.Less(special.Alpha, 1f);
        Assert.AreEqual(HudKeyBadge.State.Ready, skill.Current, "The skill has no cooldown, so Q is always ready.");
        Assert.AreEqual(1f, skill.Alpha);

        for (int i = 0; i < 13; i++) _combat.Stats.RecordEnemyKill(_state.Tick, true, _combat.CombatTime);
        Assert.AreEqual(100f, _combat.Stats.Gauge);
        Assert.AreEqual(HudKeyBadge.State.Lit, special.Current, "E lights up when the gauge is full.");
        Assert.AreEqual(1f, special.Alpha);
        AssertColor(UiPalette.Accent, special.KeyColor);
        float maxScale = 1f;
        float until = Time.realtimeSinceStartup + 1.2f;
        while (Time.realtimeSinceStartup < until)
        {
            maxScale = Mathf.Max(maxScale, special.PulseScale);
            yield return null;
        }
        Assert.Greater(maxScale, 1.05f, "The lit E pulses.");

        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        float pausedScale = special.PulseScale;
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.AreEqual(pausedScale, special.PulseScale, 1e-5f, "The pulse holds still while paused.");
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);

        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.IsTrue(_combat.TryUseSpecial(_state.Tick, player.transform.position));
        Assert.AreEqual(HudKeyBadge.State.Dim, special.Current, "Using the special dims E again.");
        Assert.AreEqual(1f, special.PulseScale, 1e-4f);

        // 재사용 대기 표시는 나중을 위한 자리다. 넘긴 비율만큼 Q를 덮고, 0이면 다시 준비된 모습이다.
        var cooldown = Find<RectTransform>(skill.transform, "CooldownFill");
        battle.RenderSkillCooldown(0.5f);
        Assert.AreEqual(HudKeyBadge.State.Dim, skill.Current);
        Assert.IsTrue(cooldown.gameObject.activeSelf);
        Assert.AreEqual(0.5f, cooldown.localScale.y, 1e-4f);
        battle.RenderSkillCooldown(0f);
        Assert.AreEqual(HudKeyBadge.State.Ready, skill.Current);
        Assert.IsFalse(cooldown.gameObject.activeSelf);

        // 가득 찬 채로 Retry해도 E가 켜진 채 남지 않는다.
        for (int i = 0; i < 13; i++) _combat.Stats.RecordEnemyKill(_state.Tick, true, _combat.CombatTime);
        Assert.AreEqual(HudKeyBadge.State.Lit, special.Current);
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(HudKeyBadge.State.Dim, special.Current);
        Assert.AreEqual(1f, special.PulseScale, 1e-4f);
        Assert.AreEqual(HudKeyBadge.State.Ready, skill.Current);
    }

    [UnityTest]
    public IEnumerator ResultKeepsTheBestScoreAcrossRunsAndMarksANewBest()
    {
        string key = PlayerPrefsBestScoreStore.Key;
        bool had = PlayerPrefs.HasKey(key);
        int old = PlayerPrefs.GetInt(key, 0);
        try
        {
            PlayerPrefs.DeleteKey(key);
            yield return Load();
            yield return EnterBattle();
            HoldCrowdHits();
            Kill(3);
            int first = _combat.Stats.Score;
            Assert.Greater(first, 0);
            yield return EndRound();

            var result = Object.FindFirstObjectByType<ResultView>();
            TMP_Text best = Find<TMP_Text>(result.transform, "BestScoreValue");
            Assert.AreEqual("BEST", Find<TMP_Text>(result.transform, "BestScoreLabel").text);
            Assert.AreEqual("000000", best.text, "While the score counts up, BEST shows the previous record (none yet).");
            Assert.IsFalse(result.NewBestShown);
            Assert.AreEqual(first, PlayerPrefs.GetInt(key, -1), "The new best is saved when the result screen opens.");
            yield return UntilReal(() => result.NewBestShown, 5f);
            Assert.IsTrue(result.NewBestShown, "Beating the record shows NEW BEST once the score has counted up.");
            Assert.AreEqual(first.ToString("D6"), best.text);
            var mark = Find<RectTransform>(result.transform, "NewBestMark");
            Assert.IsFalse(WorldRect(mark).Overlaps(TextWorldRect(Find<TMP_Text>(result.transform, "FinalScoreValue"))),
                "NEW BEST must not sit on the final score digits.");
            Assert.IsFalse(WorldRect(Find<RectTransform>(result.transform, "BestScoreRow")).Overlaps(
                WorldRect(Find<RectTransform>(result.transform, "KillsTile"))), "The BEST line keeps clear of the statistics.");
            foreach (Graphic graphic in mark.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take clicks.");

            Find<Button>(result.transform, "RetryButton").onClick.Invoke();
            yield return WaitFor(GameState.Ready);
            Assert.IsFalse(result.NewBestShown, "Retry clears the mark.");

            // 낮은 점수: 최고 점수는 그대로이고 NEW BEST가 뜨지 않는다.
            yield return EnterBattle();
            HoldCrowdHits();
            Kill(1);
            int second = _combat.Stats.Score;
            Assert.Less(second, first);
            yield return EndRound();
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsFalse(result.NewBestShown);
            Assert.AreEqual(first.ToString("D6"), best.text);
            Assert.AreEqual(first, PlayerPrefs.GetInt(key, -1));
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(key, old);
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Time.timeScale = 1f;
        }
    }

    // 성과에 반응하는 마무리: 마무리 대화는 이번 판의 통계로 고르고, Retry 뒤에는 이전 판의 통계를 쓰지 않는다.
    // 게임 에셋의 변형 배열은 비어 있으므로 이 테스트에서만 시험용 변형을 넣었다가 되돌린다.
    [UnityTest]
    public IEnumerator OutroFollowsThisRoundsStatsAndARetryNeverUsesThePreviousRound()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        DialogueLine[] oldManyHits = data.outroManyHits, oldPrecise = data.outroPrecise;
        DialogueLine[] baseOutro = data.outro;
        Assert.GreaterOrEqual(baseOutro.Length, 3);
        var manyHits = new[]
        {
            baseOutro[0],
            new DialogueLine { speaker = "노아", text = "전투 로그 확인. 너 많이 맞았어.", leftSpeaker = true },
            new DialogueLine { speaker = "K-7", text = "기록은 무사해. 그거면 됐어.", leftSpeaker = false },
            baseOutro[baseOutro.Length - 2],
            baseOutro[baseOutro.Length - 1]
        };
        try
        {
            data.outroManyHits = manyHits;
            data.outroPrecise = new DialogueLine[0];
            yield return Load();
            yield return EnterBattle();
            UiLayer layer = Layer();
            for (int i = 0; i < 15; i++)
                Assert.IsTrue(_combat.Stats.RecordPlayerHit(_state.Tick, _combat.CombatTime + 1f + i));
            Assert.GreaterOrEqual(_combat.Stats.HitCount, 15);
            yield return EndRoundToOutro();
            Assert.AreEqual(OutroVariant.ManyHits, layer.Dialogue.ChosenOutro, "15 hits picks the many-hits outro.");
            yield return ReadDialogue(manyHits);
            yield return WaitFor(GameState.Result);

            // Result의 Retry: 새 판은 피격 0이다. 이전 판의 15회로 고르면 안 된다.
            Find<Button>(Object.FindFirstObjectByType<ResultView>().transform, "RetryButton").onClick.Invoke();
            yield return WaitFor(GameState.Ready);
            yield return EnterBattle();
            Assert.AreEqual(0, _combat.Stats.HitCount);
            yield return EndRoundToOutro();
            Assert.AreEqual(OutroVariant.Base, layer.Dialogue.ChosenOutro, "A retry chooses from its own round only.");
            yield return ReadDialogue(baseOutro);
            yield return WaitFor(GameState.Result);
        }
        finally
        {
            data.outroManyHits = oldManyHits;
            data.outroPrecise = oldPrecise;
            Time.timeScale = 1f;
        }
    }

    // 시작 화면의 조작 안내가 게임이 실제로 듣는 키를 모두 말한다(공격, 차지, 회피, 스킬 Q, 특수 공격 E, ESC).
    [UnityTest]
    public IEnumerator StartHintNamesTheRealKeyOfEveryActionInTwoReadableLines()
    {
        yield return Load();
        var start = Object.FindFirstObjectByType<StartView>();
        TMP_Text hintText = Find<TMP_Text>(start.transform, "ControlHint");
        string hint = Regex.Replace(hintText.text, @"\s+", " ");

        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var actions = (InputActionAsset)typeof(PlayerRuntime).GetField("inputActions", Private).GetValue(player);
        InputActionMap map = actions.FindActionMap("Player", true);
        string attack = KeyLabel(map.FindAction((string)typeof(PlayerRuntime).GetField("attackActionName", Private).GetValue(player), true));
        string special = KeyLabel(map.FindAction((string)typeof(PlayerRuntime).GetField("specialActionName", Private).GetValue(player), true));
        // 스킬과 회피는 팀원의 기본 어셈블리에 있어 이름으로 찾는다. 게임이 쓰는 액션 이름을 읽는다.
        Component skill = player.GetComponent("PlayerSkill");
        Assert.IsNotNull(skill, "The player must carry its skill component.");
        var skills = (Array)skill.GetType().GetField("skills", Private).GetValue(skill);
        Assert.Greater(skills.Length, 0);
        object firstSkill = skills.GetValue(0);
        string skillKey = KeyLabel(map.FindAction((string)firstSkill.GetType().GetField("inputActionName").GetValue(firstSkill), true));
        Component dodge = player.GetComponent("PlayerDodge");
        string dodgeKey = KeyLabel(map.FindAction((string)dodge.GetType().GetField("dodgeActionName", Private).GetValue(dodge), true));

        Assert.AreEqual("LMB", attack);
        StringAssert.Contains("WASD MOVE", hint);
        StringAssert.Contains("MOUSE CAMERA", hint);
        StringAssert.Contains(attack + " ATTACK", hint);
        StringAssert.Contains("HOLD 3RD " + attack + " CHARGE", hint, "Holding the third attack press charges.");
        StringAssert.Contains(dodgeKey + " DODGE", hint);
        StringAssert.Contains(skillKey + " SKILL DASH", hint);
        StringAssert.Contains(special + " SPECIAL AT 100%", hint, "The special needs a full gauge.");
        StringAssert.Contains("ESC PAUSE", hint);

        // 두 줄로, 칸 안에서, 1280x720에서도 읽히는 크기로(12px 이상) 보인다.
        hintText.ForceMeshUpdate();
        Assert.AreEqual(2, hintText.textInfo.lineCount);
        Assert.IsFalse(hintText.isTextOverflowing, "The hint must fit its box.");
        Assert.GreaterOrEqual(hintText.fontSize, 18f, "At 1280x720 the hint stays at 12 px or more.");
        Rect panel = WorldRect(Find<RectTransform>(start.transform, "ControlHintPanel"));
        Rect text = TextWorldRect(hintText);
        Assert.IsTrue(panel.Contains(text.min) && panel.Contains(text.max), "The hint text stays inside its panel.");
        Assert.IsFalse(WorldRect(Find<RectTransform>(start.transform, "StartButton")).Overlaps(panel));
    }

    // 새 요소(콤보, 무전, E·Q)가 기존 HUD(일시정지, 점수, 시간, 게이지)와 서로 겹치지 않고 화면 안에 있다.
    [UnityTest]
    public IEnumerator HudAdditionsKeepClearOfTheExistingHud()
    {
        yield return Load();
        yield return EnterBattle();
        HoldCrowdHits();
        var battle = Object.FindFirstObjectByType<BattleView>();
        Kill(2);
        BattleRadioLine half = Line(Resources.Load<DialogueSequenceData>("DialogueSequence"), 50f);
        battle.ShowRadio(half.speaker, half.text, half.leftSpeaker);
        yield return null;
        string[] names =
        {
            "PauseButton", "ScorePanel", "TimerPanel", "ComboCounter", "RadioSubtitle", "SpecialGaugeFrame",
            "SkillKeyBadge", "SpecialKeyBadge"
        };
        var rects = new List<(string, Rect)>();
        foreach (string name in names)
            rects.Add((name, WorldRect(Find<RectTransform>(battle.transform, name))));
        rects.Add(("SkillKeyBadge/KeyLabel", WorldRect(Find<RectTransform>(Find<Transform>(battle.transform, "SkillKeyBadge"), "KeyLabel"))));
        rects.Add(("SpecialKeyBadge/KeyLabel", WorldRect(Find<RectTransform>(Find<Transform>(battle.transform, "SpecialKeyBadge"), "KeyLabel"))));
        Rect screen = WorldRect((RectTransform)battle.transform);
        foreach ((string name, Rect rect) in rects)
            Assert.IsTrue(rect.xMin >= screen.xMin - 0.5f && rect.xMax <= screen.xMax + 0.5f &&
                          rect.yMin >= screen.yMin - 0.5f && rect.yMax <= screen.yMax + 0.5f, name + " must stay inside the screen.");
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                if (rects[j].Item1.StartsWith(rects[i].Item1 + "/", StringComparison.Ordinal)) continue;
                Assert.IsFalse(rects[i].Item2.Overlaps(rects[j].Item2), $"{rects[i].Item1} overlaps {rects[j].Item1}.");
            }
        foreach (string name in new[] { "ComboCounter", "RadioSubtitle", "SkillKeyBadge", "SpecialKeyBadge" })
            foreach (Graphic graphic in Find<Transform>(battle.transform, name).GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, $"{name}/{graphic.name} must not take clicks.");
    }

    // ------------------------------------------------------------------ helpers

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
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
        _combat = Object.FindFirstObjectByType<CombatRoundRuntime>();
        Assert.IsNotNull(_combat);
    }

    private IEnumerator EndRoundToOutro()
    {
        _events.EnqueueRequest(new TimerReachedZeroRequest(_state.Tick));
        yield return WaitFor(GameState.Finishing);
        _events.EnqueueRequest(new FinishingCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.OutroDialogue);
    }

    private IEnumerator EndRound()
    {
        yield return EndRoundToOutro();
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Result);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }

    private static IEnumerator UntilReal(Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    // 전투 시계와 HUD 연출(FeedbackTime)은 captureFramerate로 프레임마다 1/60초씩 간다. 배치 모드의 프레임은 실제 시간보다
    // 훨씬 빨라서, 전투 시계에 걸린 기다림은 실제 시간이 아니라 프레임으로 센다.
    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator UntilFrames(Func<bool> condition, int maximumFrames)
    {
        for (int i = 0; i < maximumFrames && !condition(); i++)
            yield return null;
    }

    // 대화를 한 줄씩 넘기며 보이는 줄이 기대한 줄인지 본다(첫 입력은 타이핑을 마치고, 다음 입력은 다음 줄로 간다).
    private static IEnumerator ReadDialogue(DialogueLine[] lines)
    {
        var view = Object.FindFirstObjectByType<DialogueView>();
        TMP_Text body = Find<TMP_Text>(view.transform, "BodyText");
        TMP_Text speaker = Find<TMP_Text>(view.transform, "SpeakerName");
        Button next = Find<Button>(view.transform, "AdvanceArea");
        yield return new WaitForSecondsRealtime(0.45f);
        foreach (DialogueLine line in lines)
        {
            Assert.AreEqual(line.text, body.text);
            Assert.AreEqual(line.speaker, speaker.text);
            if (body.maxVisibleCharacters < line.text.Length) next.onClick.Invoke();
            next.onClick.Invoke();
        }
    }

    // 군중의 실제 공격이 테스트가 만드는 콤보와 점수를 바꾸지 않게 플레이어 피격 무적 시간을 이 판 끝까지 늘린다(false면 푼다).
    private void HoldCrowdHits(bool hold = true) =>
        typeof(RoundCombatStats).GetField("_invulnerableUntil", Private)
            .SetValue(_combat.Stats, hold ? float.PositiveInfinity : float.NegativeInfinity);

    // 이번 판의 시계로 처치를 기록한다(CombatRoundRuntime이 적중을 받을 때와 같은 시각).
    private void Kill(int count)
    {
        for (int i = 0; i < count; i++)
            Assert.IsTrue(_combat.Stats.RecordEnemyKill(_state.Tick, true, _combat.CombatTime));
    }

    // 서 있는 적 몇을 center 바로 옆(같은 높이, 특수 공격 반경 안)으로 옮기고, 특수 공격이 찾아보는 탐색 격자를 갱신한다.
    private void PlaceEnemiesAround(Vector3 center, int count)
    {
        int placed = 0;
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID))
        {
            if (placed == count) break;
            if (enemy.ActiveTick != _state.Tick || enemy.State == EnemyAgentState.Pooled ||
                enemy.State == EnemyAgentState.Knockback || enemy.State == EnemyAgentState.Dying) continue;
            float angle = placed * Mathf.PI * 2f / count;
            enemy.transform.position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
            placed++;
        }
        Assert.AreEqual(count, placed, "The battle has enough standing enemies to place.");
        Physics.SyncTransforms();
        _enemies.UpdateAI(0f, _state.Tick);
    }

    private static UiLayer Layer()
    {
        var bootstrap = Object.FindFirstObjectByType<CoreBootstrap>();
        return (UiLayer)typeof(CoreBootstrap).GetField("_ui", Private).GetValue(bootstrap);
    }

    private static BattleRadioLine Line(DialogueSequenceData data, float remaining)
    {
        foreach (BattleRadioLine line in data.battleRadio)
            if (Mathf.Approximately(line.remainingSeconds, remaining)) return line;
        Assert.Fail($"DialogueSequence.asset has no radio line at {remaining} s.");
        return default;
    }

    // 액션에 묶인 키를 안내문 낱말로 바꾼다(<Keyboard>/q → Q, leftShift → SHIFT, <Mouse>/leftButton → LMB).
    private static string KeyLabel(InputAction action)
    {
        foreach (InputBinding binding in action.bindings)
        {
            string path = binding.effectivePath;
            if (path == "<Mouse>/leftButton") return "LMB";
            const string keyboard = "<Keyboard>/";
            if (string.IsNullOrEmpty(path) || !path.StartsWith(keyboard, StringComparison.Ordinal)) continue;
            string key = path.Substring(keyboard.Length);
            return key.EndsWith("Shift", StringComparison.OrdinalIgnoreCase) ? "SHIFT" : key.ToUpperInvariant();
        }
        Assert.Fail(action.name + " has no keyboard or mouse binding.");
        return null;
    }

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    // 글자가 실제로 그려진 범위(캔버스 배율이 적용된 좌표).
    private static Rect TextWorldRect(TMP_Text text)
    {
        text.ForceMeshUpdate();
        Bounds bounds = text.textBounds;
        Vector3 min = text.rectTransform.TransformPoint(bounds.min);
        Vector3 max = text.rectTransform.TransformPoint(bounds.max);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.01f, "red");
        Assert.AreEqual(expected.g, actual.g, 0.01f, "green");
        Assert.AreEqual(expected.b, actual.b, 0.01f, "blue");
    }

    private static T Find<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }
}

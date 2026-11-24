using NUnit.Framework;
using UnityEngine;

// 콤보 표시에 쓰는 값(살아 있는 콤보와 남은 시간)과, 설정으로만 켜지는 콤보 점수 보너스(AGENTS.md 9절).
// 보너스가 꺼져 있으면 지금 점수와 한 점도 다르지 않아야 하고, 켜면 단계마다 오르다 상한에서 멈추며,
// 반올림은 한 곳(내림)에서만 한다. 특수 공격 자신의 처치는 comboCountsSpecialKills가 정한 대로 세어진다.
// Reset 뒤에는 콤보가 남지 않는다.
public sealed class ComboScoreTests
{
    private CombatSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<CombatSettings>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_settings);

    private RoundCombatStats Begin(int tick = 1)
    {
        var stats = new RoundCombatStats(_settings);
        stats.Reset(tick);
        stats.SetAccepting(true);
        return stats;
    }

    [Test]
    public void TheGameAssetUsesTheChosenComboBonus()
    {
        var asset = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(asset, "Resources/CombatSettings.asset is missing.");
        // Chosen by the user on 2026-11-25 (provisional): +5% per combo step, at most +50%.
        Assert.AreEqual(0.05f, asset.comboScoreStep, 0.0001f);
        Assert.AreEqual(0.5f, asset.comboScoreMaxBonus, 0.0001f);
        Assert.AreEqual(0f, _settings.comboScoreStep, "A new CombatSettings starts with the bonus off.");
    }

    // Score gain raised by 50% on 2026-11-25 (user request, provisional): the game asset scores a hit at 15 and a kill
    // at 150 more, so a one-hit kill is 165 and a hit a tough enemy survives is 15. A new CombatSettings keeps the base
    // 10 and 100 that the other tests here calculate with.
    [Test]
    public void TheGameAssetScoresHalfAgainAsMuchAsTheBaseValues()
    {
        var asset = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(asset, "Resources/CombatSettings.asset is missing.");
        Assert.AreEqual(10, _settings.hitScore);
        Assert.AreEqual(100, _settings.killScore);
        Assert.AreEqual(_settings.hitScore * 3 / 2, asset.hitScore);
        Assert.AreEqual(_settings.killScore * 3 / 2, asset.killScore);

        var stats = new RoundCombatStats(asset);
        stats.Reset(1);
        stats.SetAccepting(true);
        Assert.IsTrue(stats.RecordEnemyHit(1, true, 0f));
        Assert.AreEqual(15, stats.Score, "A hit the enemy survives.");
        // The second landed hit is combo 2: the kill's 165 with the asset's combo bonus, by the one rounding rule.
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 0.1f));
        Assert.AreEqual(15 + RoundCombatStats.ComboKillScore(165, 2, asset.comboScoreStep, asset.comboScoreMaxBonus), stats.Score);
        Assert.AreEqual(15 + 173, stats.Score, "165 x 1.05 = 173.25, rounded down.");
    }

    // A tough enemy survives its first hit: that hit scores as a hit only and counts toward the combo.
    [Test]
    public void AHitTheEnemySurvivesScoresAsAHitAndExtendsTheCombo()
    {
        var stats = Begin();
        int hitScore = _settings.hitScore, killScore = _settings.killScore;
        Assert.IsTrue(stats.RecordEnemyHit(1, true, 0f));
        Assert.AreEqual(hitScore, stats.Score, "The hit's own score.");
        Assert.AreEqual(0, stats.KillCount, "No kill yet.");
        Assert.AreEqual(1, stats.ComboAt(0f));
        Assert.AreEqual(_settings.hitGauge, stats.Gauge, 0.0001f, "The hit's own gauge.");
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 0.4f));
        Assert.AreEqual(hitScore + hitScore + killScore, stats.Score, "The second hit destroys it: hit + kill score.");
        Assert.AreEqual(1, stats.KillCount);
        Assert.AreEqual(2, stats.MaxCombo, "Both hits count toward the combo.");
        Assert.IsFalse(stats.RecordEnemyHit(2, true, 0.5f), "A hit of another round is ignored.");

        // With the bonus on, the surviving hit takes it by the same rounding rule as a kill.
        _settings.comboScoreStep = 0.25f;
        _settings.comboScoreMaxBonus = 1f;
        var bonus = Begin(3);
        Assert.IsTrue(bonus.RecordEnemyHit(3, true, 0f));
        Assert.IsTrue(bonus.RecordEnemyHit(3, true, 0.2f));
        Assert.AreEqual(hitScore + RoundCombatStats.ComboKillScore(hitScore, 2, 0.25f, 1f), bonus.Score);
    }

    [Test]
    public void BonusOffScoresEveryKillExactlyAsBefore()
    {
        var stats = Begin();
        int baseScore = _settings.hitScore + _settings.killScore;
        // 한 콤보 안의 열두 번, 끊긴 뒤, 피격 뒤, 특수 공격 자신의 처치: 모두 처치당 같은 점수다(감점은 피격 규칙 그대로).
        for (int i = 0; i < 12; i++) Assert.IsTrue(stats.RecordEnemyKill(1, true, i * 0.5f));
        Assert.AreEqual(12, stats.MaxCombo);
        Assert.AreEqual(12 * baseScore, stats.Score);
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 20f));
        Assert.AreEqual(13 * baseScore, stats.Score);
        int beforeHit = stats.Score;
        Assert.IsTrue(stats.RecordPlayerHit(1, 20.5f));
        int afterHit = stats.Score;
        Assert.AreEqual(beforeHit - Mathf.FloorToInt(beforeHit * _settings.playerHitPenalty), afterHit);
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 21f));
        Assert.AreEqual(afterHit + baseScore, stats.Score);
        Assert.AreEqual(1f, stats.ComboScoreMultiplier(12));
        Assert.AreEqual(baseScore, RoundCombatStats.ComboKillScore(baseScore, 50, 0f, 1f));

        // 특수 공격이 한순간에 쓰러뜨린 열다섯: 기본 규칙에서는 예전처럼 새 콤보로 세어지고(최대 콤보 15), 점수는 같다.
        Assert.IsTrue(_settings.comboCountsSpecialKills);
        Assert.IsTrue(stats.TryUseSpecial(1));
        int before = stats.Score;
        for (int i = 0; i < 15; i++) Assert.IsTrue(stats.RecordEnemyKill(1, false, 22f, specialKill: true));
        Assert.AreEqual(15 * baseScore, stats.Score - before);
        Assert.AreEqual(15, stats.MaxCombo, "The special's kills count toward Max Combo as they always have.");
        // 콤보에서 빼는 설정이어도 보너스가 꺼져 있으면 점수는 같다.
        _settings.comboCountsSpecialKills = false;
        for (int i = 0; i < 13; i++) Assert.IsTrue(stats.RecordEnemyKill(1, true, 30f + i * 0.1f));
        Assert.IsTrue(stats.TryUseSpecial(1));
        before = stats.Score;
        for (int i = 0; i < 15; i++) Assert.IsTrue(stats.RecordEnemyKill(1, false, 32f, specialKill: true));
        Assert.AreEqual(15 * baseScore, stats.Score - before);
    }

    // 특수 공격 자신의 처치(CombatRoundRuntime이 TryUseSpecial 안에서 같은 순간에 기록한다)에 대한 규칙은
    // CombatSettings.comboCountsSpecialKills 하나로 정한다. 기본값(true)은 지금까지 세던 그대로다: 특수 공격이 이어 오던
    // 콤보를 끊고, 그 처치들이 새 콤보가 되며, 보너스가 켜져 있으면 처치마다 그 콤보의 배율을 받는다.
    [Test]
    public void ByDefaultTheSpecialsOwnKillsStartANewComboThatEarnsTheBonus()
    {
        Assert.IsTrue(_settings.comboCountsSpecialKills, "The default keeps counting the special's kills as combo kills.");
        _settings.comboScoreStep = 0.25f;
        _settings.comboScoreMaxBonus = 1f;
        var stats = Begin();
        for (int i = 0; i < 13; i++) Assert.IsTrue(stats.RecordEnemyKill(1, true, i * 0.1f));   // fills the gauge
        Assert.AreEqual(13, stats.ComboAt(1.2f));
        Assert.IsTrue(stats.TryUseSpecial(1));
        Assert.AreEqual(0, stats.ComboAt(1.2f), "Using the special ends the running combo.");

        int before = stats.Score;
        for (int i = 0; i < 20; i++) Assert.IsTrue(stats.RecordEnemyKill(1, false, 1.2f, specialKill: true));
        // x1, x1.25, x1.5, x1.75, then the x2 cap for the other sixteen: 4124 instead of 20 x 110 = 2200.
        Assert.AreEqual(110 + 137 + 165 + 192 + 16 * 220, stats.Score - before);
        Assert.AreEqual(20, stats.ComboAt(1.2f), "The special's kills are a new combo of their own.");
        Assert.AreEqual(20, stats.MaxCombo);
        Assert.AreEqual(33, stats.KillCount);

        before = stats.Score;
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 2f));
        Assert.AreEqual(21, stats.ComboAt(2f), "A kill inside the window carries the special's combo on.");
        Assert.AreEqual(220, stats.Score - before);
    }

    [Test]
    public void LeftOutOfTheComboTheSpecialsKillsScoreTheBaseAndStartNoCombo()
    {
        _settings.comboScoreStep = 0.25f;
        _settings.comboScoreMaxBonus = 1f;
        _settings.comboCountsSpecialKills = false;
        var stats = Begin();
        for (int i = 0; i < 13; i++) Assert.IsTrue(stats.RecordEnemyKill(1, true, i * 0.1f));
        Assert.IsTrue(stats.TryUseSpecial(1));

        int before = stats.Score;
        for (int i = 0; i < 20; i++) Assert.IsTrue(stats.RecordEnemyKill(1, false, 1.2f, specialKill: true));
        Assert.AreEqual(20 * 110, stats.Score - before, "Each special kill scores hitScore + killScore.");
        Assert.AreEqual(33, stats.KillCount, "They are still kills.");
        Assert.AreEqual(0, stats.ComboAt(1.2f), "They start no combo.");
        Assert.AreEqual(13, stats.MaxCombo, "Max Combo keeps the combo from before the special.");

        before = stats.Score;
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 1.5f));
        Assert.AreEqual(1, stats.ComboAt(1.5f), "The first kill after the special starts a combo from 1.");
        Assert.AreEqual(110, stats.Score - before);
        before = stats.Score;
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 1.6f));
        Assert.AreEqual(137, stats.Score - before);
    }

    [Test]
    public void BonusOnRisesPerComboStepAndStopsAtTheCap()
    {
        _settings.comboScoreStep = 0.25f;
        _settings.comboScoreMaxBonus = 0.5f;
        var stats = Begin();
        Assert.AreEqual(1f, stats.ComboScoreMultiplier(1), "The first kill of a chain never gets a bonus.");
        Assert.AreEqual(1.25f, stats.ComboScoreMultiplier(2), 1e-6f);
        Assert.AreEqual(1.5f, stats.ComboScoreMultiplier(3), 1e-6f);
        Assert.AreEqual(1.5f, stats.ComboScoreMultiplier(4), 1e-6f, "The bonus stops at comboScoreMaxBonus.");
        Assert.AreEqual(1.5f, stats.ComboScoreMultiplier(40), 1e-6f);

        int[] expected = { 110, 137, 165, 165, 165 };   // 110 x 1, x1.25 = 137.5 -> 137, x1.5, cap, cap
        int total = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int before = stats.Score;
            Assert.IsTrue(stats.RecordEnemyKill(1, true, i * 1f));
            Assert.AreEqual(expected[i], stats.Score - before, $"kill {i + 1}");
            total += expected[i];
        }
        Assert.AreEqual(total, stats.Score);
    }

    [Test]
    public void TheComboWindowIsInclusiveAndAHitOrSpecialRestartsTheBonus()
    {
        _settings.comboScoreStep = 0.5f;
        _settings.comboScoreMaxBonus = 2f;
        var stats = Begin();
        float window = stats.ComboWindowSeconds;
        Assert.AreEqual(2f, window, "Default comboTimeoutSeconds.");

        stats.RecordEnemyKill(1, true, 0f);
        int before = stats.Score;
        stats.RecordEnemyKill(1, true, window);                      // exactly at the window: the chain goes on
        Assert.AreEqual(165, stats.Score - before);
        before = stats.Score;
        stats.RecordEnemyKill(1, true, window * 2f + 0.001f);        // just past it: a new chain
        Assert.AreEqual(110, stats.Score - before);

        stats.RecordEnemyKill(1, true, 5f);
        Assert.AreEqual(2, stats.ComboAt(5f));
        Assert.IsTrue(stats.RecordPlayerHit(1, 5.1f));
        Assert.AreEqual(0, stats.ComboAt(5.1f), "A hit on the player ends the combo.");
        before = stats.Score;
        stats.RecordEnemyKill(1, true, 5.2f);
        Assert.AreEqual(110, stats.Score - before, "The chain after a hit starts without a bonus.");

        for (int i = 0; i < 20; i++) stats.RecordEnemyKill(1, true, 6f + i * 0.1f);
        Assert.IsTrue(stats.TryUseSpecial(1));
        Assert.AreEqual(0, stats.ComboAt(8.1f), "A special ends the combo.");
        before = stats.Score;
        stats.RecordEnemyKill(1, false, 8.2f);
        Assert.AreEqual(110, stats.Score - before);
    }

    [Test]
    public void KillScoreIsFlooredOnceAndToleratesFloatStorage()
    {
        // 110 x 1.05 = 115.5 -> 115 (내림). 0.7은 float로 0.69999999라 오차를 흡수하지 않으면 186이 된다.
        Assert.AreEqual(115, RoundCombatStats.ComboKillScore(110, 2, 0.05f, 1f));
        Assert.AreEqual(187, RoundCombatStats.ComboKillScore(110, 2, 0.7f, 1f));
        Assert.AreEqual(143, RoundCombatStats.ComboKillScore(110, 4, 0.1f, 1f));
        Assert.AreEqual(110, RoundCombatStats.ComboKillScore(110, 1, 0.7f, 1f));
        Assert.AreEqual(110, RoundCombatStats.ComboKillScore(110, 9, 0.5f, 0f), "A cap of 0 turns the bonus off.");
        Assert.AreEqual(110, RoundCombatStats.ComboKillScore(110, 9, -1f, 1f), "A negative step is treated as off.");
        Assert.AreEqual(0, RoundCombatStats.ComboKillScore(0, 9, 0.5f, 1f));
        Assert.AreEqual(1.7f, RoundCombatStats.ComboMultiplier(2, 0.7f, 1f), 1e-6f);
    }

    [Test]
    public void ComboAndItsTimeLeftFollowTheRoundClock()
    {
        var stats = Begin();
        Assert.AreEqual(0, stats.ComboAt(0f));
        Assert.AreEqual(0f, stats.ComboTimeLeft(0f));
        stats.RecordEnemyKill(1, true, 10f);
        stats.RecordEnemyKill(1, true, 10.5f);
        Assert.AreEqual(2, stats.ComboAt(10.5f));
        Assert.AreEqual(2f, stats.ComboTimeLeft(10.5f), 1e-5f);
        Assert.AreEqual(0.5f, stats.ComboTimeLeft(12f), 1e-5f);
        Assert.AreEqual(2, stats.ComboAt(12.5f), "Alive up to and including the window.");
        Assert.AreEqual(0, stats.ComboAt(12.51f));
        Assert.AreEqual(0f, stats.ComboTimeLeft(12.51f));

        var model = new ComboUiModel(2, stats.ComboTimeLeft(11.5f), stats.ComboWindowSeconds, stats.ComboScoreMultiplier(2));
        Assert.AreEqual(0.5f, model.TimeLeftNormalized, 1e-5f);
        Assert.AreEqual(1f, model.ScoreMultiplier);
        Assert.AreEqual(0f, new ComboUiModel(0, 5f, 0f, 0.5f).TimeLeftNormalized);
    }

    [Test]
    public void ResetClearsTheComboAndTheBonusChain()
    {
        _settings.comboScoreStep = 0.25f;
        var stats = Begin();
        for (int i = 0; i < 6; i++) stats.RecordEnemyKill(1, true, i * 0.2f);
        Assert.AreEqual(6, stats.ComboAt(1f));
        stats.Reset(2);
        stats.SetAccepting(true);
        Assert.AreEqual(0, stats.ComboAt(1f));
        Assert.AreEqual(0, stats.MaxCombo);
        Assert.AreEqual(0, stats.Score);
        Assert.IsTrue(stats.RecordEnemyKill(2, true, 1.1f));
        Assert.AreEqual(110, stats.Score, "The first kill after a reset starts a new chain without a bonus.");
        Assert.AreEqual(1, stats.ComboAt(1.1f));
        Assert.IsFalse(stats.RecordEnemyKill(1, true, 1.2f), "The old round's tick is rejected.");
    }
}

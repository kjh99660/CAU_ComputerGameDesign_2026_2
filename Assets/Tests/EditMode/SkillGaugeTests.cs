using NUnit.Framework;
using UnityEngine;

// 스킬(E)과 게이지: 한 번 쓰는 데 게이지의 정해진 몫(임시값 절반)이 필요하고 그만큼만 쓴다. 스킬로 맞힌 타격은 평소
// 게이지 획득량의 정해진 몫만 채운다(임시값 0: 채우지 않는다). 점수와 콤보는 일반 타격과 같다. 다른 판의 요청은 무시한다.
public sealed class SkillGaugeTests
{
    private CombatSettings _settings;
    private RoundCombatStats _stats;

    [SetUp]
    public void SetUp()
    {
        _settings = ScriptableObject.CreateInstance<CombatSettings>();
        _stats = new RoundCombatStats(_settings);
        _stats.Reset(7);
        _stats.SetAccepting(true);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_settings);

    private void Kill(int count, float gaugeScale = 1f)
    {
        for (int i = 0; i < count; i++)
            Assert.IsTrue(_stats.RecordEnemyKill(7, true, i * 0.1f, false, gaugeScale));
    }

    [Test]
    public void TheDefaultsAreHalfTheGaugeToUseAndNothingBackFromItsBlows()
    {
        Assert.AreEqual(0.5f, _settings.skillCostFraction);
        Assert.AreEqual(0f, _settings.skillGaugeGainFraction, "A blow of the skill charges nothing.");
        Assert.IsFalse(_settings.specialKillsRestoreGauge, "Nor do the special's own kills.");
        Assert.AreEqual(50f, _stats.SkillCost);
        Assert.AreEqual(8f, _settings.specialRadius, "The special reaches 8 m.");
    }

    // The game's own settings (Assets/Resources/CombatSettings.asset, 2026-11-25): a hit charges 2 and the kill 3 more,
    // so a robot destroyed by a normal attack charges 5 (it was 3 + 5 = 8); being hit still charges 8. From an empty
    // gauge the skill (half of it) takes 10 robots and the special (all of it) 20.
    [Test]
    public void TheGameChargesTwoForAHitAndThreeMoreForTheKill()
    {
        var asset = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(asset, "Assets/Resources/CombatSettings.asset");
        Assert.AreEqual(2f, asset.hitGauge);
        Assert.AreEqual(3f, asset.killGauge);
        Assert.AreEqual(8f, asset.playerHitGauge, "Being hit charges as before.");
        var stats = new RoundCombatStats(asset);
        stats.Reset(1);
        stats.SetAccepting(true);
        Assert.IsTrue(stats.RecordEnemyKill(1, true, 0f));
        Assert.AreEqual(5f, stats.Gauge, "A robot destroyed by a normal attack.");
        Assert.IsTrue(stats.RecordEnemyHit(1, true, 0.1f));
        Assert.AreEqual(7f, stats.Gauge, "A hit a red robot survives.");

        stats.Reset(2);
        stats.SetAccepting(true);
        int kills = 0;
        while (!stats.CanUseSkill(2) && kills < 100) { stats.RecordEnemyKill(2, true, kills * 0.1f); kills++; }
        Assert.AreEqual(10, kills, "Robots to the skill's share.");
        while (stats.Gauge < asset.maximumGauge && kills < 100) { stats.RecordEnemyKill(2, true, kills * 0.1f); kills++; }
        Assert.AreEqual(20, kills, "Robots to a full gauge.");
    }

    [Test]
    public void ABlowThatChargesNothingStillScoresAndCountsLikeAnyOther()
    {
        Kill(3);
        Assert.AreEqual(24f, _stats.Gauge);
        int score = _stats.Score;
        Assert.IsTrue(_stats.RecordEnemyKill(7, true, 0.3f, false, 0f));
        Assert.IsTrue(_stats.RecordEnemyHit(7, true, 0.4f, 0f));
        Assert.AreEqual(24f, _stats.Gauge, "The skill's share of the gain is nothing.");
        Assert.AreEqual(4, _stats.KillCount);
        Assert.AreEqual(5, _stats.MaxCombo, "The kill and the survived hit both go on with the combo.");
        Assert.Greater(_stats.Score, score);
    }

    [Test]
    public void TheSkillNeedsItsShareOfTheGaugeAndSpendsOnlyThat()
    {
        Assert.IsFalse(_stats.CanUseSkill(7), "An empty gauge.");
        Assert.IsFalse(_stats.TryUseSkill(7));
        Kill(6);
        Assert.AreEqual(48f, _stats.Gauge);
        Assert.IsFalse(_stats.CanUseSkill(7), "48 of the 50 it needs.");
        Assert.IsFalse(_stats.TryUseSkill(7));
        Assert.AreEqual(48f, _stats.Gauge, "A refused use spends nothing.");
        Assert.AreEqual(0, _stats.SkillUseCount);

        Kill(1);
        Assert.AreEqual(56f, _stats.Gauge);
        int combo = _stats.ComboAt(0.6f);
        int changes = 0;
        _stats.Changed += () => changes++;
        Assert.IsTrue(_stats.CanUseSkill(7));
        Assert.AreEqual(0, changes, "Asking spends nothing and tells nobody.");
        Assert.IsTrue(_stats.TryUseSkill(7));
        Assert.AreEqual(6f, _stats.Gauge);
        Assert.AreEqual(1, _stats.SkillUseCount);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, _stats.SpecialUseCount, "The skill is not the special.");
        Assert.AreEqual(combo, _stats.ComboAt(0.6f), "Unlike the special, the skill leaves the running combo alone.");
        Assert.IsFalse(_stats.TryUseSkill(7), "6 left.");
    }

    [Test]
    public void AFullGaugePaysForTwoSkillsOrOneSpecial()
    {
        Kill(13);
        Assert.AreEqual(100f, _stats.Gauge);
        Assert.IsTrue(_stats.TryUseSkill(7));
        Assert.AreEqual(50f, _stats.Gauge);
        Assert.IsFalse(_stats.TryUseSpecial(7), "The special still needs the whole gauge.");
        Assert.IsTrue(_stats.TryUseSkill(7), "Exactly the share that is left is enough.");
        Assert.AreEqual(0f, _stats.Gauge);
        Assert.IsFalse(_stats.TryUseSkill(7));
        Assert.AreEqual(2, _stats.SkillUseCount);

        Kill(13);
        Assert.IsTrue(_stats.TryUseSpecial(7));
        Assert.AreEqual(0f, _stats.Gauge);
        Assert.IsFalse(_stats.CanUseSkill(7), "The special spent everything.");
    }

    [Test]
    public void ABlowOfTheSkillChargesItsShareOfTheUsualGain()
    {
        // A kill is hitGauge 3 + killGauge 5, a hit the enemy survives is hitGauge 3.
        Assert.IsTrue(_stats.RecordEnemyKill(7, true, 0f, false, 0.5f));
        Assert.AreEqual(4f, _stats.Gauge);
        Assert.IsTrue(_stats.RecordEnemyHit(7, true, 0.1f, 0.5f));
        Assert.AreEqual(5.5f, _stats.Gauge);
        Assert.IsTrue(_stats.RecordEnemyKill(7, true, 0.2f));
        Assert.AreEqual(13.5f, _stats.Gauge, "A normal kill still gives all of it.");
        Assert.IsTrue(_stats.RecordEnemyKill(7, false, 0.3f, false, 0.5f));
        Assert.AreEqual(13.5f, _stats.Gauge, "A blow that charges nothing charges nothing.");
        Assert.IsTrue(_stats.RecordEnemyKill(7, true, 0.4f, false, -1f));
        Assert.AreEqual(13.5f, _stats.Gauge, "A share below zero never takes gauge away.");
    }

    [Test]
    public void TheSkillsShareChangesTheGaugeOnlyNotTheScoreOrTheCombo()
    {
        var other = new RoundCombatStats(_settings);
        other.Reset(7);
        other.SetAccepting(true);
        for (int i = 0; i < 5; i++)
        {
            _stats.RecordEnemyKill(7, true, i * 0.1f, false, 0.5f);
            other.RecordEnemyKill(7, true, i * 0.1f);
        }
        _stats.RecordEnemyHit(7, true, 0.5f, 0.5f);
        other.RecordEnemyHit(7, true, 0.5f);
        Assert.AreEqual(other.Score, _stats.Score);
        Assert.AreEqual(other.KillCount, _stats.KillCount);
        Assert.AreEqual(other.MaxCombo, _stats.MaxCombo);
        Assert.AreEqual(43f, other.Gauge);
        Assert.AreEqual(21.5f, _stats.Gauge);
    }

    [Test]
    public void AnotherRoundsRequestOrAClosedRoundSpendsNothing()
    {
        Kill(13);
        Assert.IsFalse(_stats.CanUseSkill(6));
        Assert.IsFalse(_stats.TryUseSkill(6), "The tick of the round before.");
        Assert.IsFalse(_stats.TryUseSkill(8));
        _stats.SetAccepting(false);
        Assert.IsFalse(_stats.CanUseSkill(7));
        Assert.IsFalse(_stats.TryUseSkill(7), "The round is not taking combat results.");
        Assert.AreEqual(100f, _stats.Gauge);
        Assert.AreEqual(0, _stats.SkillUseCount);

        _stats.SetAccepting(true);
        Assert.IsTrue(_stats.TryUseSkill(7));
        _stats.Reset(8);
        Assert.AreEqual(0, _stats.SkillUseCount, "A new round starts with no uses.");
        Assert.AreEqual(0f, _stats.Gauge);
    }

    [TestCase(0f, 0f)]      // free: always usable, spends nothing
    [TestCase(0.25f, 25f)]
    [TestCase(1f, 100f)]    // the whole gauge, like the special
    [TestCase(3f, 100f)]    // out of range: never more than the whole gauge
    [TestCase(-1f, 0f)]
    public void TheCostFollowsTheSetting(float fraction, float expectedCost)
    {
        _settings.skillCostFraction = fraction;
        Assert.AreEqual(expectedCost, _stats.SkillCost, 1e-4f);
        Assert.AreEqual(expectedCost <= 0f, _stats.CanUseSkill(7), "Only a free skill works on an empty gauge.");
        Kill(13);
        Assert.IsTrue(_stats.TryUseSkill(7));
        Assert.AreEqual(100f - expectedCost, _stats.Gauge, 1e-4f);
    }

    // A skill that was called off before it did anything gives its share back, once, and only to its own round.
    [Test]
    public void ACalledOffSkillGivesItsShareBackOnce()
    {
        Assert.IsFalse(_stats.RefundSkill(7), "Nothing was used.");
        Kill(8);
        Assert.AreEqual(64f, _stats.Gauge);
        Assert.IsTrue(_stats.TryUseSkill(7));
        Assert.AreEqual(14f, _stats.Gauge);
        Assert.IsFalse(_stats.RefundSkill(6), "Another round's tick.");
        Assert.IsTrue(_stats.RefundSkill(7));
        Assert.AreEqual(64f, _stats.Gauge);
        Assert.AreEqual(0, _stats.SkillUseCount);
        Assert.IsFalse(_stats.RefundSkill(7), "There is no use left to give back.");
        Assert.AreEqual(64f, _stats.Gauge);

        // Giving back is not a combat result: it does not wait for the round to be taking results (a wind-up can be
        // called off in the frame the game is paused). The round decides how long it still gives back.
        Assert.IsTrue(_stats.TryUseSkill(7));
        _stats.SetAccepting(false);
        Assert.IsTrue(_stats.RefundSkill(7));
        Assert.AreEqual(64f, _stats.Gauge);
        _stats.SetAccepting(true);

        // The gauge went on filling while the skill wound up: giving back never goes over the full gauge.
        Assert.IsTrue(_stats.TryUseSkill(7));
        Kill(12);
        Assert.AreEqual(100f, _stats.Gauge);
        Assert.IsTrue(_stats.RefundSkill(7));
        Assert.AreEqual(100f, _stats.Gauge);

        Assert.IsTrue(_stats.TryUseSkill(7));
        _stats.Reset(8);
        _stats.SetAccepting(true);
        Assert.IsFalse(_stats.RefundSkill(7), "The round the skill was used in is over.");
        Assert.IsFalse(_stats.RefundSkill(8), "The new round has no use to give back.");
        Assert.AreEqual(0f, _stats.Gauge);
    }

    // The round's side of the same rule: the key works only in Battle, a refusal spends nothing, and a use is given
    // back in Battle, Paused and Finishing but no longer once the score window has closed.
    [Test]
    public void TheRoundSpendsOnlyInBattleAndGivesBackUntilTheScoreWindowCloses()
    {
        var root = new GameObject("SkillGaugeRoundTest");
        try
        {
            var enemy = root.AddComponent<EnemyRuntime>();
            var ui = root.AddComponent<UIRoot>();
            var combat = root.AddComponent<CombatRoundRuntime>();
            combat.Configure(enemy, ui, _settings);
            var initialize = combat.InitializeRound(1);
            while (initialize.MoveNext()) { }
            Assert.IsFalse(combat.TryUseSkill(1), "Not in Battle yet.");
            combat.ApplyState(new GameStateChangedEvent(GameState.Countdown, GameState.Battle, 1));
            Assert.IsFalse(combat.TryUseSkill(1), "An empty gauge.");
            for (int i = 0; i < 7; i++) combat.Stats.RecordEnemyKill(1, true, i * 0.1f);
            Assert.AreEqual(56f, combat.Stats.Gauge);
            Assert.IsFalse(combat.TryUseSkill(0), "Another round's tick.");
            Assert.AreEqual(56f, combat.Stats.Gauge);
            Assert.IsTrue(combat.TryUseSkill(1));
            Assert.AreEqual(6f, combat.Stats.Gauge);
            Assert.IsFalse(combat.TryUseSkill(1));
            Assert.IsTrue(combat.RefundSkill(1), "The wind-up was called off.");
            Assert.AreEqual(56f, combat.Stats.Gauge);

            Assert.IsTrue(combat.TryUseSkill(1));
            combat.ApplyState(new GameStateChangedEvent(GameState.Battle, GameState.Paused, 1));
            Assert.IsFalse(combat.TryUseSkill(1), "Paused.");
            Assert.AreEqual(6f, combat.Stats.Gauge);
            Assert.IsTrue(combat.RefundSkill(1), "A wind-up called off in the frame the game was paused is given back.");
            Assert.AreEqual(56f, combat.Stats.Gauge);
            combat.ApplyState(new GameStateChangedEvent(GameState.Paused, GameState.Battle, 1));
            for (int i = 0; i < 6; i++) combat.Stats.RecordEnemyKill(1, true, 1f + i * 0.1f);
            Assert.AreEqual(100f, combat.Stats.Gauge);
            Assert.IsTrue(combat.TryUseSkill(1));
            Assert.IsTrue(combat.TryUseSkill(1));
            Assert.AreEqual(0f, combat.Stats.Gauge);
            combat.ApplyState(new GameStateChangedEvent(GameState.Battle, GameState.Finishing, 1));
            Assert.IsFalse(combat.TryUseSkill(1), "No new skill once the time is up.");
            Assert.IsTrue(combat.RefundSkill(1), "A skill pressed before the time was up can still be given back.");
            Assert.AreEqual(50f, combat.Stats.Gauge);
            combat.Advance(0.05f, 10f);   // the slow motion runs out: the score window closes
            Assert.IsFalse(combat.RefundSkill(1), "Nothing is given back once the round's score window has closed.");
            Assert.AreEqual(50f, combat.Stats.Gauge);
            Assert.AreEqual(1, combat.Stats.SkillUseCount);
        }
        finally { Time.timeScale = 1f; Object.DestroyImmediate(root); }
    }
}

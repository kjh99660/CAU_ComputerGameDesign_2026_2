using NUnit.Framework;
using UnityEngine;

public sealed class RoundCombatStatsTests
{
    [Test]
    public void KillHitAndResetRespectSettingsTickAndInvulnerability()
    {
        var settings = ScriptableObject.CreateInstance<CombatSettings>();
        try
        {
            var stats = new RoundCombatStats(settings);
            stats.Reset(1);
            stats.SetAccepting(true);
            Assert.IsFalse(stats.RecordEnemyKill(0));
            Assert.IsTrue(stats.RecordEnemyKill(1));
            Assert.AreEqual(110, stats.Score);
            Assert.AreEqual(8f, stats.Gauge);
            Assert.IsTrue(stats.RecordPlayerHit(1, 1f));
            Assert.AreEqual(99, stats.Score);
            Assert.AreEqual(16f, stats.Gauge);
            Assert.IsFalse(stats.RecordPlayerHit(1, 1.7f));
            Assert.IsTrue(stats.RecordPlayerHit(1, 1.8f));
            Assert.AreEqual(90, stats.Score);
            stats.Reset(2);
            Assert.AreEqual(0, stats.Score);
            Assert.AreEqual(0f, stats.Gauge);
            Assert.AreEqual(0, stats.KillCount);
            Assert.IsFalse(stats.RecordEnemyKill(1));
        }
        finally { Object.DestroyImmediate(settings); }
    }
}

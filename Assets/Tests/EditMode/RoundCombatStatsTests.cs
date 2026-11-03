using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public sealed class RoundCombatStatsTests
{
    [Test]
    public void FinishingClosesScoreWindowAndRestoresTimeScale()
    {
        var root = new GameObject("FinishingTest");
        var settings = ScriptableObject.CreateInstance<CombatSettings>();
        try
        {
            settings.finishingDuration = 0.2f;
            var enemy = root.AddComponent<EnemyRuntime>();
            var ui = root.AddComponent<UIRoot>();
            var combat = root.AddComponent<CombatRoundRuntime>();
            combat.Configure(enemy, ui, settings);
            var initialize = combat.InitializeRound(1);
            while (initialize.MoveNext()) { }
            combat.ApplyState(new GameStateChangedEvent(GameState.Battle, GameState.Finishing, 1));
            Assert.AreEqual(0.5f, Time.timeScale);
            Assert.IsTrue(combat.Stats.RecordEnemyKill(1));
            combat.Advance(0.05f, 0.1f);
            Assert.IsTrue(combat.Stats.RecordEnemyKill(1));
            combat.Advance(0.05f, 0.1f);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(combat.Stats.RecordEnemyKill(1));
            Assert.AreEqual(220, combat.Stats.Score);
        }
        finally { Time.timeScale = 1f; Object.DestroyImmediate(root); Object.DestroyImmediate(settings); }
    }

    [Test]
    public void AcceptedEnemyHitIsCountedOnceAndOldTickIsRejected()
    {
        var root = new GameObject("CombatTest");
        var enemyObject = new GameObject("Enemy");
        var enemySettings = ScriptableObject.CreateInstance<EnemySettings>();
        var settings = ScriptableObject.CreateInstance<CombatSettings>();
        try
        {
            var runtime = root.AddComponent<EnemyRuntime>();
            typeof(EnemyRuntime).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runtime, enemySettings);
            runtime.SetTarget(root.transform);
            var ui = root.AddComponent<UIRoot>();
            var combat = root.AddComponent<CombatRoundRuntime>();
            combat.Configure(runtime, ui, settings);
            var initialize = combat.InitializeRound(7);
            while (initialize.MoveNext()) { }
            combat.ApplyState(new GameStateChangedEvent(GameState.Countdown, GameState.Battle, 7));
            runtime.ApplyState(GameState.Battle, 7);
            var enemy = enemyObject.AddComponent<EnemyAgent>();
            enemy.Initialize(runtime);
            enemy.Activate(Vector3.forward, 7);
            Assert.IsTrue(runtime.TryApplyHit(enemy, Vector3.forward, 1f, 7));
            Assert.IsFalse(runtime.TryApplyHit(enemy, Vector3.forward, 1f, 7));
            Assert.IsFalse(runtime.TryApplyHit(enemy, Vector3.forward, 1f, 6));
            Assert.AreEqual(110, combat.Stats.Score);
            Assert.AreEqual(1, combat.Stats.KillCount);
            Assert.AreEqual(8f, combat.Stats.Gauge);
            combat.StopProducing();
            var reset = combat.ResetRound(8);
            while (reset.MoveNext()) { }
            Assert.AreEqual(0, combat.Stats.Score);
            Assert.AreEqual(0, combat.Stats.KillCount);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(enemySettings);
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void EnemyAttackChecksRangeOnlyOnce()
    {
        var go = new GameObject("EnemyAttackTest");
        var settings = ScriptableObject.CreateInstance<EnemySettings>();
        try
        {
            var enemy = go.AddComponent<EnemyAgent>();
            enemy.Initialize(null);
            enemy.Activate(Vector3.zero, 4);
            enemy.RunAI(Vector3.forward, new System.Collections.Generic.List<EnemyAgent>(), settings, 0.01f, 4);
            Assert.IsTrue(enemy.TryAttackHit(Vector3.forward, settings, settings.AttackDuration, 4));
            Assert.IsFalse(enemy.TryAttackHit(Vector3.forward, settings, settings.AttackDuration, 4));
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(settings); }
    }

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

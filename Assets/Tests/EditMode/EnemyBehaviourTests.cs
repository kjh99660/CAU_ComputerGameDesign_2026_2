using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Enemy attacks the player can read and avoid, turns to attack, and the lively crowd (EnemySettings "Attack telegraph"
// and "Liveliness"). With the settings' defaults the first prototype's attack is unchanged (other tests cover it).
public sealed class EnemyBehaviourTests
{
    private readonly List<Object> _made = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object made in _made)
            if (made != null) Object.DestroyImmediate(made);
        _made.Clear();
    }

    [Test]
    public void ATelegraphedStrikeLandsOnlyWhenItsWindupEndsAndOnlyInFront()
    {
        EnemySettings settings = Settings(windup: 0.5f, arc: 120f, reach: 1.8f, attackers: 0, liveliness: 0f);
        EnemyAgent enemy = Enemy(null);
        var none = new List<EnemyAgent>();
        var front = new Vector3(0f, 0f, 1.2f);

        enemy.Activate(Vector3.zero, 3);   // faces +z
        enemy.RunAI(front, none, settings, 0.01f, 3);
        Assert.AreEqual(EnemyAgentState.Attack, enemy.State);
        Assert.IsTrue(enemy.IsWindingUp);
        Assert.IsFalse(enemy.TryAttackHit(front, settings, 0.3f, 3), "No hit while it winds up.");
        enemy.RunAction(settings, 0.3f, 3);
        Assert.IsTrue(enemy.IsWindingUp);
        Assert.IsTrue(enemy.TryAttackHit(front, settings, 0.3f, 3), "The strike lands when the wind-up ends.");
        Assert.IsFalse(enemy.IsWindingUp);
        Assert.IsFalse(enemy.TryAttackHit(front, settings, 0.3f, 3), "One strike per attack.");
        enemy.RunAction(settings, settings.AttackTotal, 3);
        Assert.AreNotEqual(EnemyAgentState.Attack, enemy.State, "Wind-up, swing and recovery make the whole attack.");

        Assert.IsFalse(Strike(enemy, settings, front, new Vector3(0f, 0f, -1.2f), 4), "Behind the enemy is safe.");
        Assert.IsFalse(Strike(enemy, settings, front, new Vector3(0f, 0f, 1.95f), 5), "Past its reach is safe.");
        Assert.IsTrue(Strike(enemy, settings, front, new Vector3(0.6f, 0f, 1.2f), 6), "A little to the side is still in front.");
    }

    [Test]
    public void OnlyAFewEnemiesAttackAtOnceAndTurnsAreHandedBack()
    {
        EnemySettings settings = Settings(windup: 0.5f, arc: 360f, reach: 0f, attackers: 2, liveliness: 0f);
        EnemyRuntime runtime = Runtime(settings);
        var enemies = new List<EnemyAgent>();
        for (int i = 0; i < 4; i++)
        {
            EnemyAgent enemy = Enemy(runtime);
            enemy.Activate(new Vector3(i * 0.3f, 0f, 0f), 2);
            enemies.Add(enemy);
        }
        var none = new List<EnemyAgent>();
        var player = new Vector3(0.45f, 0f, 1f);
        foreach (EnemyAgent enemy in enemies) enemy.RunAI(player, none, settings, 0.01f, 2);
        Assert.AreEqual(2, runtime.ActiveAttackers);
        Assert.AreEqual(2, enemies.FindAll(e => e.State == EnemyAgentState.Attack).Count, "The others wait their turn.");

        EnemyAgent first = enemies.Find(e => e.State == EnemyAgentState.Attack);
        first.RunAction(settings, settings.AttackTotal + 0.01f, 2);
        Assert.AreEqual(1, runtime.ActiveAttackers, "A finished attack hands its turn back.");
        EnemyAgent waiting = enemies.Find(e => e.State != EnemyAgentState.Attack && e != first);
        waiting.RunAI(player, none, settings, 0.01f, 2);
        Assert.AreEqual(EnemyAgentState.Attack, waiting.State, "A waiting enemy takes the free turn.");
        Assert.AreEqual(2, runtime.ActiveAttackers);
        waiting.ApplyHit(Vector3.back, settings, 2);
        Assert.AreEqual(1, runtime.ActiveAttackers, "An attack cut short by a hit hands its turn back too.");
    }

    [Test]
    public void LivelyEnemiesGetEveryTemperamentAndKeepTheirOwn()
    {
        EnemySettings settings = Settings(windup: 0f, arc: 360f, reach: 0f, attackers: 0, liveliness: 1f);
        EnemyAgent enemy = Enemy(Runtime(settings));
        var seen = new HashSet<EnemyTemperament>();
        for (int i = 0; i < 40; i++)
        {
            enemy.Activate(Vector3.zero, 5, i / 40f);
            seen.Add(enemy.Temperament);
            Assert.That(enemy.HoverRadius, Is.InRange(settings.HoverRingMinimum, settings.HoverRingMaximum));
        }
        Assert.AreEqual(4, seen.Count, "Rushers, flankers, circlers and hesitant ones all show up.");
        enemy.Activate(Vector3.zero, 5, 0.37f);
        EnemyTemperament temperament = enemy.Temperament;
        float ring = enemy.HoverRadius;
        enemy.Activate(Vector3.zero, 5, 0.37f);
        Assert.AreEqual(temperament, enemy.Temperament, "The same enemy in the same round behaves the same way.");
        Assert.AreEqual(ring, enemy.HoverRadius);
    }

    [Test]
    public void AnEnemyWaitingForItsTurnCirclesInsteadOfStandingStill()
    {
        EnemySettings settings = Settings(windup: 0.5f, arc: 120f, reach: 1.8f, attackers: 1, liveliness: 1f);
        EnemyRuntime runtime = Runtime(settings);
        Assert.IsTrue(runtime.TryTakeAttackSlot(Enemy(runtime)), "Someone else holds the only turn.");
        EnemyAgent enemy = Enemy(runtime);
        for (int i = 0; i < 50; i++)
        {
            enemy.Activate(Vector3.zero, 7, i / 50f);
            if (enemy.Temperament == EnemyTemperament.Circler) break;
        }
        Assert.AreEqual(EnemyTemperament.Circler, enemy.Temperament);
        var player = new Vector3(0f, 0f, enemy.HoverRadius);
        var none = new List<EnemyAgent>();
        Vector3 last = enemy.transform.position;
        float travelled = 0f, closest = float.MaxValue, farthest = 0f;
        for (int i = 0; i < 80; i++)
        {
            enemy.RunAI(player, none, settings, 0.05f, 7);
            Assert.AreNotEqual(EnemyAgentState.Attack, enemy.State, "No turn, no attack.");
            Vector3 position = enemy.transform.position;
            travelled += Vector3.Distance(position, last);
            last = position;
            Vector3 gap = player - position;
            gap.y = 0f;
            closest = Mathf.Min(closest, gap.magnitude);
            farthest = Mathf.Max(farthest, gap.magnitude);
        }
        Assert.Greater(travelled, 1.5f, "It keeps moving around the player while it waits.");
        Assert.Greater(closest, settings.AttackRange * 0.5f, "...without walking into the player...");
        Assert.Less(farthest, settings.HoverRingMaximum + 2.5f, "...or wandering off.");
    }

    // Starts an attack at "start" and checks the strike against "at" once the wind-up is over.
    private static bool Strike(EnemyAgent enemy, EnemySettings settings, Vector3 start, Vector3 at, int tick)
    {
        enemy.Activate(Vector3.zero, tick);
        enemy.RunAI(start, new List<EnemyAgent>(), settings, 0.01f, tick);
        Assert.AreEqual(EnemyAgentState.Attack, enemy.State);
        return enemy.TryAttackHit(at, settings, settings.AttackWindup + 0.01f, tick);
    }

    private EnemySettings Settings(float windup, float arc, float reach, int attackers, float liveliness)
    {
        var settings = ScriptableObject.CreateInstance<EnemySettings>();
        _made.Add(settings);
        var serialized = new SerializedObject(settings);
        serialized.FindProperty("attackWindup").floatValue = windup;
        serialized.FindProperty("attackArcDegrees").floatValue = arc;
        serialized.FindProperty("attackReach").floatValue = reach;
        serialized.FindProperty("windupTurnSpeed").floatValue = 0f;
        serialized.FindProperty("maximumAttackers").intValue = attackers;
        serialized.FindProperty("liveliness").floatValue = liveliness;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return settings;
    }

    private EnemyRuntime Runtime(EnemySettings settings)
    {
        var go = new GameObject("EnemyRuntimeTest");
        _made.Add(go);
        var runtime = go.AddComponent<EnemyRuntime>();
        var serialized = new SerializedObject(runtime);
        serialized.FindProperty("settings").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return runtime;
    }

    private EnemyAgent Enemy(EnemyRuntime owner)
    {
        var go = new GameObject("EnemyBehaviourTest");
        _made.Add(go);
        var enemy = go.AddComponent<EnemyAgent>();
        enemy.Initialize(owner);
        return enemy;
    }
}

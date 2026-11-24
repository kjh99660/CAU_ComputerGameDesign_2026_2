using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// One enemy on the cells of a level with two floors: ground 12 x 6 m, a deck 3 m up over x = 8..12, and a ramp
// along z = 0 from x = 2 up to the deck. Cells are 1 m.
public sealed class EnemyNavigationAgentTests
{
    private const float Step = 1f / 60f;
    private const int Tick = 3;
    private readonly List<Object> _created = new();
    private EnemySettings _settings;

    [SetUp]
    public void SetUp() => _settings = Track(ScriptableObject.CreateInstance<EnemySettings>());

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in _created)
            if (item != null) Object.DestroyImmediate(item);
        _created.Clear();
    }

    private static EnemyNavField Level(float maxDrop)
    {
        var builder = new EnemyNavField.Builder(Vector3.zero, 1f, 12, 6);
        for (int z = 0; z < 6; z++)
            for (int x = 0; x < 12; x++)
            {
                bool ramp = z == 0 && x >= 2 && x <= 7;
                builder.Add(x, z, ramp ? (x - 1) * 0.5f : 0f);
                if (x >= 8)
                    builder.Add(x, z, 3f);
            }
        return builder.Build(40f, null, maxDrop);
    }

    private EnemyAgent Spawn(Vector3 position, EnemyNavField field)
    {
        var gameObject = Track(new GameObject("Enemy"));
        EnemyAgent enemy = gameObject.AddComponent<EnemyAgent>();
        enemy.Initialize(null);
        enemy.Activate(position, Tick);
        enemy.UseNavigation(field);
        return enemy;
    }

    private T Track<T>(T item) where T : Object
    {
        _created.Add(item);
        return item;
    }

    [Test]
    public void EnemyUnderADeckWalksRoundByTheRampAndOnlyThenAttacks()
    {
        EnemyNavField field = Level(0f);
        var player = new Vector3(10.5f, 3f, 3.5f);
        field.SetGoal(player);
        EnemyAgent enemy = Spawn(new Vector3(10.5f, 0f, 3.5f), field);   // straight under the player
        var none = System.Array.Empty<EnemyAgent>();

        enemy.RunAI(player, none, _settings, Step, Tick);
        Assert.AreEqual(EnemyAgentState.Chase, enemy.State, "Under the player is not next to the player.");
        Assert.IsFalse(enemy.TryAttackHit(player, _settings, Step, Tick));

        bool attacked = false;
        float attackedAt = -1f, previousHeight = enemy.transform.position.y, highestStep = 0f;
        for (int frame = 0; frame < 900 && !attacked; frame++)
        {
            enemy.RunAI(player, none, _settings, Step, Tick);
            enemy.RunAction(_settings, Step, Tick);
            float height = enemy.transform.position.y;
            highestStep = Mathf.Max(highestStep, Mathf.Abs(height - previousHeight));
            previousHeight = height;
            if (enemy.State == EnemyAgentState.Attack)
            {
                attacked = true;
                attackedAt = frame * Step;
            }
            else
                Assert.AreNotEqual(EnemyNavField.None, enemy.NavCell);
        }
        Assert.IsTrue(attacked, "It never got to the player.");
        Assert.Greater(attackedAt, 4f, "The walk round by the ramp is about 20 m.");
        Assert.AreEqual(3f, enemy.transform.position.y, 0.05f, "It attacks from the deck.");
        Assert.Less(highestStep, 0.11f, "It walked up; it did not jump floors.");
    }

    [Test]
    public void EnemyOnADeckStepsOffAfterAPlayerBelowFallsAndLands()
    {
        EnemyNavField field = Level(4f);
        var player = new Vector3(4.5f, 0f, 3.5f);
        field.SetGoal(player);
        EnemyAgent enemy = Spawn(new Vector3(10.5f, 3f, 3.5f), field);
        var none = System.Array.Empty<EnemyAgent>();

        int airFrames = 0;
        float landedAt = -1f, fastestDrop = 0f, previousHeight = 3f;
        for (int frame = 0; frame < 360; frame++)
        {
            enemy.RunAI(player, none, _settings, Step, Tick);
            enemy.RunAction(_settings, Step, Tick);
            Vector3 position = enemy.transform.position;
            fastestDrop = Mathf.Max(fastestDrop, previousHeight - position.y);
            previousHeight = position.y;
            if (position.y > 0.3f && position.y < 2.7f)
            {
                airFrames++;
                Assert.Less(position.x, 8.2f, "It falls beyond the edge of the deck, not through it.");
                Assert.AreEqual(0f, field.Height(enemy.NavCell), "In the air it already belongs to the floor below.");
                Assert.AreNotEqual(EnemyAgentState.Attack, enemy.State);
            }
            if (landedAt < 0f && airFrames > 0 && position.y < 0.01f)
                landedAt = frame * Step;
        }
        Assert.Greater(airFrames, 15, "A 3 m fall takes about half a second.");
        Assert.Less(fastestDrop, 0.25f, "It falls; it does not appear on the ground.");
        Assert.Greater(landedAt, 0f);
        Assert.Less(landedAt, 2.5f, "Straight over the edge, not round by the ramp.");
        Assert.Less(Vector3.Distance(enemy.transform.position, player), _settings.AttackRange + 0.5f);
    }

    [Test]
    public void EnemyPushedTowardAnEdgeStaysOnTheDeck()
    {
        EnemyNavField field = Level(4f);
        field.SetGoal(new Vector3(10.5f, 3f, 3.5f));   // the player is up here too: no reason to go down
        EnemyAgent enemy = Spawn(new Vector3(8.3f, 3f, 3.5f), field);
        for (int frame = 0; frame < 60; frame++)
            enemy.YieldTo(new Vector3(8.9f - frame * 0.01f, 3f, 3.5f), _settings, Step, Tick);   // the player walks at it
        Assert.GreaterOrEqual(enemy.transform.position.x, 8f, "Giving way never sends an enemy over an edge.");
        Assert.AreEqual(3f, enemy.transform.position.y, 0.001f);
    }

    [Test]
    public void PlayerOnAnotherFloorIsNeitherHitNorMadeRoomFor()
    {
        EnemyNavField field = Level(0f);
        EnemyAgent enemy = Spawn(new Vector3(10.5f, 0f, 3.5f), field);
        var above = new Vector3(10.6f, 3f, 3.5f);
        Vector3 before = enemy.transform.position;
        enemy.YieldTo(above, _settings, Step, Tick);
        Assert.AreEqual(before, enemy.transform.position, "The player on the deck does not push an enemy under it.");

        // Same spot on the same floor: the enemy does give way.
        enemy.YieldTo(new Vector3(10.6f, 0f, 3.5f), _settings, Step, Tick);
        Assert.AreNotEqual(before, enemy.transform.position);
    }

    [Test]
    public void EnemiesOnAnotherFloorDoNotPushEachOther()
    {
        EnemyNavField field = Level(0f);
        var player = new Vector3(1.5f, 0f, 4.5f);
        field.SetGoal(player);
        EnemyAgent below = Spawn(new Vector3(9.5f, 0f, 4.5f), field);
        EnemyAgent above = Spawn(new Vector3(9.6f, 3f, 4.5f), field);
        EnemyAgent alone = Spawn(new Vector3(9.5f, 0f, 4.5f), field);

        below.RunAI(player, new List<EnemyAgent> { below, above }, _settings, Step, Tick);
        alone.RunAI(player, new List<EnemyAgent> { alone }, _settings, Step, Tick);
        Assert.AreEqual(alone.PlanarVelocity, below.PlanarVelocity, "A body one floor up is not in the way.");
    }
}

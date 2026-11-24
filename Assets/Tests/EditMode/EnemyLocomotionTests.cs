using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// How an enemy moves and gives way. Attack range, cooldown and hit rules are covered by EnemySystemsTests.
public sealed class EnemyLocomotionTests
{
    private const float Step = 1f / 60f;
    private const int Tick = 7;
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

    [Test]
    public void ChasingEnemyAcceleratesAndTurnsInsteadOfSnapping()
    {
        EnemyAgent enemy = Spawn(Vector3.zero);
        Vector3 target = new Vector3(20f, 0f, 0f);   // to the right, while the enemy faces +Z
        var none = System.Array.Empty<EnemyAgent>();

        enemy.RunAI(target, none, _settings, Step, Tick);
        Assert.AreEqual(EnemyAgentState.Chase, enemy.State);
        Assert.Less(enemy.PlanarVelocity.magnitude, _settings.MoveSpeed * 0.5f, "The first frame must not jump to full speed.");
        Assert.Less(Quaternion.Angle(Quaternion.identity, enemy.transform.rotation), _settings.TurnSpeed * Step + 0.01f, "The first frame must not snap to the new facing.");

        for (int i = 0; i < 60; i++) enemy.RunAI(target, none, _settings, Step, Tick);
        Assert.AreEqual(_settings.MoveSpeed, enemy.PlanarVelocity.magnitude, 0.01f, "After a second it runs at the configured speed.");
        Assert.Less(Vector3.Angle(Vector3.right, enemy.transform.forward), 1f);
        Assert.AreEqual(0f, enemy.transform.position.y, 0.0001f, "Movement stays on the ground plane.");
    }

    [Test]
    public void EnemyInRangeHoldsItsGroundWhileCoolingDown()
    {
        EnemyAgent enemy = Spawn(Vector3.zero);
        Vector3 target = Vector3.forward * (_settings.AttackRange * 0.9f);
        var none = System.Array.Empty<EnemyAgent>();
        enemy.RunAI(target, none, _settings, Step, Tick);
        Assert.AreEqual(EnemyAgentState.Attack, enemy.State);
        enemy.RunAction(_settings, _settings.AttackDuration, Tick);   // attack over, cooldown still running

        Vector3 before = enemy.transform.position;
        for (int i = 0; i < 20; i++) enemy.RunAI(target, none, _settings, Step, Tick);
        Assert.AreEqual(before, enemy.transform.position, "An enemy waiting for its cooldown stands still.");
        Assert.AreEqual(Vector3.zero, enemy.PlanarVelocity);
        Assert.IsFalse(enemy.ShowsLocomotion, "It must not show a walk cycle while standing.");
    }

    [Test]
    public void EnemyDoesNotWalkIntoABodyThatIsAlreadyThere()
    {
        EnemyAgent blocker = Spawn(new Vector3(0f, 0f, _settings.BodySpacing * 0.8f));
        EnemyAgent enemy = Spawn(Vector3.zero);
        var neighbours = new List<EnemyAgent> { blocker, enemy };
        Vector3 target = Vector3.forward * 20f;   // straight through the blocker

        float closest = float.MaxValue;
        for (int i = 0; i < 120; i++)
        {
            enemy.RunAI(target, neighbours, _settings, Step, Tick);
            closest = Mathf.Min(closest, Vector3.Distance(enemy.transform.position, blocker.transform.position));
        }
        Assert.GreaterOrEqual(closest, _settings.BodySpacing * 0.75f, "The enemy must not push into the body in front of it.");
        Assert.Less(Vector3.Dot(enemy.PlanarVelocity, Vector3.forward), 0.3f, "It stops pressing forward and waits.");
    }

    [Test]
    public void EnemyStepsOutOfThePlayersSpace()
    {
        EnemyAgent enemy = Spawn(new Vector3(0.2f, 0f, 0f));
        Vector3 player = Vector3.zero;
        float moved = 0f;
        for (int i = 0; i < 30; i++)
        {
            Vector3 before = enemy.transform.position;
            enemy.YieldTo(player, _settings, Step, Tick);
            moved = Mathf.Max(moved, Vector3.Distance(before, enemy.transform.position));
        }
        Assert.AreEqual(_settings.PlayerPersonalRadius, Vector3.Distance(player, enemy.transform.position), 0.001f);
        Assert.LessOrEqual(moved, _settings.PlayerPushSpeed * Step + 0.0001f, "The step back is paced, not a teleport.");

        Vector3 settled = enemy.transform.position;
        enemy.YieldTo(player, _settings, Step, Tick);
        Assert.AreEqual(settled, enemy.transform.position, "Outside the player's space nothing moves.");
        enemy.YieldTo(player, _settings, Step, Tick + 1);
        Assert.AreEqual(settled, enemy.transform.position, "Another round's tick is ignored.");
    }

    [Test]
    public void BodyInterpolatesOnlyWhilePhysicsMovesIt()
    {
        // An interpolated kinematic body writes its own pose back over the Transform, which swallowed the AI's movement.
        var gameObject = Track(new GameObject("Enemy"));
        gameObject.AddComponent<BoxCollider>();
        Rigidbody body = gameObject.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        EnemyAgent enemy = gameObject.AddComponent<EnemyAgent>();
        enemy.Initialize(null);

        enemy.Activate(Vector3.zero, Tick);
        Assert.IsTrue(body.isKinematic);
        Assert.AreEqual(RigidbodyInterpolation.None, body.interpolation);

        Assert.IsTrue(enemy.ApplyHit(Vector3.up, _settings, Tick));
        Assert.IsFalse(body.isKinematic);
        Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);

        enemy.RunAction(_settings, _settings.KnockbackFallbackDuration + 0.01f, Tick);   // lands, starts dying
        Assert.AreEqual(EnemyAgentState.Dying, enemy.State);
        Assert.IsTrue(body.isKinematic);
        Assert.AreEqual(RigidbodyInterpolation.None, body.interpolation);
    }

    [Test]
    public void VariationSpreadsSpeedAroundTheConfiguredValueAndTheNeutralEnemyIsExact()
    {
        var owner = Track(new GameObject("Runtime"));
        EnemyRuntime runtime = owner.AddComponent<EnemyRuntime>();
        typeof(EnemyRuntime).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runtime, _settings);
        var none = System.Array.Empty<EnemyAgent>();
        Vector3 target = Vector3.forward * 500f;

        float SpeedFor(float variation)
        {
            var gameObject = Track(new GameObject("Enemy"));
            EnemyAgent enemy = gameObject.AddComponent<EnemyAgent>();
            enemy.Initialize(runtime);
            enemy.Activate(Vector3.zero, Tick, variation);
            for (int i = 0; i < 120; i++) enemy.RunAI(target, none, _settings, Step, Tick);
            return enemy.PlanarVelocity.magnitude;
        }

        Assert.AreEqual(_settings.MoveSpeed, SpeedFor(0.5f), 0.001f);
        Assert.AreEqual(_settings.MoveSpeed * (1f - _settings.SpeedVariance), SpeedFor(0f), 0.001f);
        Assert.AreEqual(_settings.MoveSpeed * (1f + _settings.SpeedVariance), SpeedFor(1f), 0.001f);
    }

    private EnemyAgent Spawn(Vector3 position)
    {
        var gameObject = Track(new GameObject("Enemy"));
        EnemyAgent enemy = gameObject.AddComponent<EnemyAgent>();
        enemy.Initialize(null);
        enemy.Activate(position, Tick);
        return enemy;
    }

    private T Track<T>(T item) where T : Object
    {
        _created.Add(item);
        return item;
    }
}

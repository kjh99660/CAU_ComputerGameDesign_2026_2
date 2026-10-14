using NUnit.Framework;
using UnityEngine;

public sealed class PlayerSystemsTests
{
    [Test]
    public void PlayerInputIsCapturedOnlyDuringBattle()
    {
        var runtime = new FakePlayerRuntime();
        var system = new PlayerInputSystem(runtime);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Initializing, GameState.Ready, 1));
        system.Update(0.02f, 0.02f);
        Assert.AreEqual(0, runtime.CaptureCount);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 1));
        system.Update(0.02f, 0.02f);
        Assert.AreEqual(1, runtime.CaptureCount);
        Assert.AreEqual(GameState.Battle, runtime.State);
    }

    [Test]
    public void PlayerActionContinuesInFinishingWithoutCapturingNewInput()
    {
        var runtime = new FakePlayerRuntime();
        var input = new PlayerInputSystem(runtime);
        var action = new PlayerActionSystem(runtime);
        var notification = new GameStateChangedEvent(GameState.Battle, GameState.Finishing, 3);

        input.OnGameStateChanged(notification);
        action.OnGameStateChanged(notification);
        input.Update(0.02f, 0.02f);
        action.Update(0.02f, 0.02f);

        Assert.AreEqual(0, runtime.CaptureCount);
        Assert.AreEqual(1, runtime.ActionCount);
    }

    [Test]
    public void EnemyTargetUsesRegisteredPlayerTransform()
    {
        GameObject enemyObject = new("EnemyRuntime");
        GameObject playerObject = new("Player");
        try
        {
            EnemyRuntime runtime = enemyObject.AddComponent<EnemyRuntime>();
            runtime.SetTarget(playerObject.transform);
            Assert.AreSame(playerObject.transform, runtime.TargetTransform);
        }
        finally
        {
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(playerObject);
        }
    }

    private sealed class FakePlayerRuntime : IPlayerRuntime
    {
        public Transform TargetTransform => null;
        public GameState State { get; private set; }
        public int CaptureCount { get; private set; }
        public int ActionCount { get; private set; }

        public void ApplyState(GameState state, int tick) => State = state;
        public void CaptureInput() => CaptureCount++;
        public void UpdateAction(float deltaTime) => ActionCount++;
    }
}

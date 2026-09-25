using System;
using System.Collections.Generic;
using NUnit.Framework;

// 일반 C# 계층의 실행 순서와 상태 필터를 검증한다.
public sealed class GameLoopHierarchyTests
{
    // 각 하위 요소가 허용 상태와 거부 상태를 정확히 구분하는지 확인한다.
    [TestCase(typeof(PlayerInputSystem), GameState.Battle, true)]
    [TestCase(typeof(PlayerInputSystem), GameState.Finishing, false)]
    [TestCase(typeof(PlayerActionSystem), GameState.Finishing, true)]
    [TestCase(typeof(BattleHudSystem), GameState.Result, false)]
    [TestCase(typeof(BattleHudSystem), GameState.Finishing, true)]
    [TestCase(typeof(DialogueSystem), GameState.IntroDialogue, true)]
    [TestCase(typeof(DialogueSystem), GameState.Battle, false)]
    [TestCase(typeof(PhysicsBodySystem), GameState.Ready, false)]
    public void SystemFiltersMatchTheirStatePolicy(Type systemType, GameState state, bool expected)
    {
        var system = (StateAwareSystem)Activator.CreateInstance(systemType);
        Assert.AreEqual(expected, system.AllowsExecutionIn(state));
    }

    // 상태 변경을 받은 뒤에만 실행되고 이전 Tick 알림은 무시한다.
    [Test]
    public void StateNotificationUpdatesFilterAndRejectsOldTick()
    {
        var system = new PlayerInputSystem();
        Assert.IsFalse(system.IsExecutionAllowed);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 2));
        Assert.IsTrue(system.IsExecutionAllowed);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Battle, GameState.Paused, 1));
        Assert.IsTrue(system.IsExecutionAllowed);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Battle, GameState.Paused, 2));
        Assert.IsFalse(system.IsExecutionAllowed);
    }

    // 상위 계층이 상태 알림과 Update를 자식에게 등록 순서대로 전달한다.
    [Test]
    public void LayerCallsChildrenInRegistrationOrder()
    {
        var calls = new List<string>();
        var layer = new TestLayer(new SpySystem("first", calls), new SpySystem("second", calls));

        layer.OnGameStateChanged(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 1));
        layer.Update(0.02f, 0.02f);

        CollectionAssert.AreEqual(
            new[] { "first:state", "second:state", "first:update", "second:update" }, calls);
    }

    // 테스트용 요소를 지정 순서대로 담는 계층이다.
    private sealed class TestLayer : GameLoopLayer
    {
        // 두 하위 요소를 기반 계층에 등록한다.
        public TestLayer(IGameLoopSystem first, IGameLoopSystem second) : base(first, second) { }
    }

    // 상태와 프레임 호출 순서를 기록하는 테스트용 요소다.
    private sealed class SpySystem : IGameLoopSystem
    {
        private readonly string _name;
        private readonly List<string> _calls;

        // 호출 기록 대상과 표시 이름을 보관한다.
        public SpySystem(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        // 상태 알림 전달 순서를 기록한다.
        public void OnGameStateChanged(GameStateChangedEvent notification) =>
            _calls.Add(_name + ":state");

        // 프레임 갱신 전달 순서를 기록한다.
        public void Update(float deltaTime, float unscaledDeltaTime) =>
            _calls.Add(_name + ":update");
    }
}

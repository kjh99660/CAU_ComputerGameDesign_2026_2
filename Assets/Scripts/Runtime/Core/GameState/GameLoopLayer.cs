using System;

// 같은 계층의 하위 요소를 등록 순서대로 호출하는 일반 C# 상위 계층이다.
public abstract class GameLoopLayer : IGameLoopSystem
{
    private readonly IGameLoopSystem[] _children;

    // 하위 요소를 지정된 순서로 보관한다.
    protected GameLoopLayer(params IGameLoopSystem[] children)
    {
        _children = children ?? throw new ArgumentNullException(nameof(children));
        foreach (var child in _children)
            if (child == null) throw new ArgumentException("A game loop child cannot be null.", nameof(children));
    }

    // 상태 변경을 모든 하위 요소에 순서대로 전달한다.
    public void OnGameStateChanged(GameStateChangedEvent notification)
    {
        foreach (var child in _children)
            child.OnGameStateChanged(notification);
    }

    // 하위 요소의 Update를 등록 순서대로 호출한다.
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        foreach (var child in _children)
            child.Update(deltaTime, unscaledDeltaTime);
    }
}

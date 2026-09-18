// CoreBootstrap의 순차 호출과 상태 알림을 받는 일반 C# 계층의 계약이다.
public interface IGameLoopNode
{
    // 현재 라운드의 상태 변경을 전달받는다.
    void OnGameStateChanged(GameStateChangedEvent notification);

    // CoreBootstrap에서 내려온 프레임 갱신을 실행한다.
    void Update(float deltaTime, float unscaledDeltaTime);
}

// Unity 물리 주기에 맞춰 추가 갱신이 필요한 계층의 계약이다.
public interface IFixedGameLoopNode
{
    // CoreBootstrap의 FixedUpdate에서 내려온 물리 갱신을 실행한다.
    void FixedUpdate(float fixedDeltaTime);
}

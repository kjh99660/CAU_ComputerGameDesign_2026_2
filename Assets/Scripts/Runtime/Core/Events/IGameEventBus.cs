using System;

// 요청 큐와 상태 전환 알림을 교환하는 이벤트 버스 계약이다.
public interface IGameEventBus
{
    event Action<IGameRequest> RequestDequeued;
    event Action<GameStateChangedEvent> StateChanged;

    // 요청을 다음 프레임 처리 대상에 추가한다.
    void EnqueueRequest(IGameRequest request);
    // 성공한 상태 전환을 구독자에게 즉시 알린다.
    void PublishStateChanged(GameStateChangedEvent notification);
}

using System;
using UnityEngine;

// CoreBootstrap이 요청 큐를 비울 수 있게 하고 상태 변경 알림을 즉시 전달한다.
public sealed class EventManager : MonoBehaviour, IGameEventBus
{
    private readonly FrameRequestQueue _requests = new FrameRequestQueue();

    public event Action<IGameRequest> RequestDequeued;
    public event Action<GameStateChangedEvent> StateChanged;

    // 요청을 현재 프레임 번호와 함께 큐에 넣는다.
    public void EnqueueRequest(IGameRequest request)
    {
        _requests.Enqueue(request, Time.frameCount);
    }

    // 상태 변경 알림을 지연 없이 구독자에게 전달한다.
    public void PublishStateChanged(GameStateChangedEvent notification)
    {
        StateChanged?.Invoke(notification);
    }

    // 이전 프레임까지 쌓인 요청을 처리한다.
    public void DrainPreviousFrames()
    {
        _requests.DrainPreviousFrames(Time.frameCount, DeliverRequest);
    }

    // 큐에서 꺼낸 요청을 상태 관리자 등 구독자에게 전달한다.
    private void DeliverRequest(IGameRequest request)
    {
        RequestDequeued?.Invoke(request);
    }

    // 오브젝트 파괴 시 대기 요청과 이벤트 구독을 정리한다.
    private void OnDestroy()
    {
        _requests.Clear();
        RequestDequeued = null;
        StateChanged = null;
    }
}

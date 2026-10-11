using UnityEngine;

// 요청을 다음 프레임까지 FIFO로 보관하고 명시적인 Handler에 직접 전달한다.
// 기존 Scene 참조를 보존하기 위해 컴포넌트 이름은 EventManager를 유지한다.
public sealed class EventManager : MonoBehaviour, IGameRequestQueue
{
    private readonly FrameRequestQueue _requests = new FrameRequestQueue();

    // 요청을 현재 프레임 번호와 함께 큐에 넣는다.
    public void EnqueueRequest(IGameRequest request)
    {
        _requests.Enqueue(request, Time.frameCount);
    }

    // 이전 프레임까지 쌓인 요청을 지정된 Command Handler가 순서대로 처리한다.
    public void DrainPreviousFrames(IGameRequestHandler handler)
    {
        if (handler == null)
        {
            Debug.LogError("A game request handler is required to drain the request queue.", this);
            return;
        }

        _requests.DrainPreviousFrames(Time.frameCount, handler.Handle);
    }

    // 오브젝트 파괴 시 대기 요청을 정리한다.
    private void OnDestroy()
    {
        _requests.Clear();
    }
}

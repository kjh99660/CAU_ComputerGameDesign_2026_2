// 게임 요청을 다음 프레임 처리 대상으로 보관하는 Queue 계약이다.
public interface IGameRequestQueue
{
    void EnqueueRequest(IGameRequest request);
    void DrainPreviousFrames(IGameRequestHandler handler);
}

// Queue에서 꺼낸 요청 하나를 처리하는 명시적인 Command Handler 계약이다.
public interface IGameRequestHandler
{
    void Handle(IGameRequest request);
}

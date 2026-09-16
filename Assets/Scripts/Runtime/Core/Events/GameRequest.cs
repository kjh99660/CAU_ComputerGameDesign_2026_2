// 모든 상태 변경 요청이 공유하는 라운드 식별 계약이다.
public interface IGameRequest
{
    int Tick { get; }
}

// 요청의 라운드 Tick을 보관하는 공통 기반 클래스다.
public abstract class GameRequest : IGameRequest
{
    public int Tick { get; }

    // 요청이 생성된 라운드의 Tick을 저장한다.
    protected GameRequest(int tick)
    {
        Tick = tick;
    }
}

// 필수 시스템의 초기화가 끝났음을 알리는 요청이다.
public sealed class InitializationCompletedRequest : GameRequest
{
    // 초기화 완료 요청에 현재 Tick을 담는다.
    public InitializationCompletedRequest(int tick) : base(tick) { }
}

// 준비 화면에서 도입 대화를 시작하는 요청이다.
public sealed class StartRequest : GameRequest
{
    // 시작 요청에 현재 Tick을 담는다.
    public StartRequest(int tick) : base(tick) { }
}

// 도입 또는 종료 대화의 완료를 알리는 요청이다.
public sealed class DialogueCompleteRequest : GameRequest
{
    // 대화 완료 요청에 현재 Tick을 담는다.
    public DialogueCompleteRequest(int tick) : base(tick) { }
}

// 카운트다운 연출의 완료를 알리는 요청이다.
public sealed class CountdownCompleteRequest : GameRequest
{
    // 카운트다운 완료 요청에 현재 Tick을 담는다.
    public CountdownCompleteRequest(int tick) : base(tick) { }
}

// 진행 중인 전투를 일시정지하는 요청이다.
public sealed class PauseRequest : GameRequest
{
    // 일시정지 요청에 현재 Tick을 담는다.
    public PauseRequest(int tick) : base(tick) { }
}

// 일시정지된 전투를 재개하는 요청이다.
public sealed class ResumeRequest : GameRequest
{
    // 재개 요청에 현재 Tick을 담는다.
    public ResumeRequest(int tick) : base(tick) { }
}

// 전투 제한 시간이 0이 됐음을 알리는 요청이다.
public sealed class TimerReachedZeroRequest : GameRequest
{
    // 타이머 종료 요청에 현재 Tick을 담는다.
    public TimerReachedZeroRequest(int tick) : base(tick) { }
}

// 마무리 정산 시간이 끝났음을 알리는 요청이다.
public sealed class FinishingCompleteRequest : GameRequest
{
    // 정산 완료 요청에 현재 Tick을 담는다.
    public FinishingCompleteRequest(int tick) : base(tick) { }
}

// 현재 라운드를 초기화하고 시작 화면으로 돌아가는 요청이다.
public sealed class RestartRequest : GameRequest
{
    // 재시작 요청에 현재 Tick을 담는다.
    public RestartRequest(int tick) : base(tick) { }
}

// 일시정지 메뉴에서 시작 화면으로 돌아가는 요청이다.
public sealed class QuitToStartRequest : GameRequest
{
    // 시작 화면 복귀 요청에 현재 Tick을 담는다.
    public QuitToStartRequest(int tick) : base(tick) { }
}

using System.Collections;

// Reset에 참여하는 시스템의 생산 중지와 초기화 계약이다.
public interface IRoundResettable
{
    // Reset이 시작되기 전에 새 전투 이벤트 생산을 멈춘다.
    void StopProducing();
    // 지정된 Tick의 초기 상태로 복원할 때까지 실행한다.
    IEnumerator ResetRound(int tick);
}

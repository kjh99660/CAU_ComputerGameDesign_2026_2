using System.Collections;

// 라운드 시작 전 준비 과정을 제공하는 시스템 계약이다.
public interface IRoundInitializable
{
    // 지정된 Tick의 라운드 준비를 끝낼 때까지 실행한다.
    IEnumerator InitializeRound(int tick);
}

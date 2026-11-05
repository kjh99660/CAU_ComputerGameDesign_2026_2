# 병합 회귀 검증 (2026-11-05)

Unity 6000.3.24f1, Windows, `d0876ba` 기반.

## 수정 근거

- `84d00be`(sikG)의 Enemy Animator/Visual 초기화와 원래 크기 복원은 유지했다.
- `db13784`(wps-ynx)의 `TryAttackHit`가 병합 결과에서 누락되어
  EnemyRuntime 호출이 컴파일되지 않았다. 메서드, 공격별 판정 초기화와
  풀 반환 시 초기화를 복원했다. 작업 전 추가되어 있던 필드는 보존했다.
- WH 공격 이벤트 히트박스와 점수 연동 브랜치의 임시 구형 판정이 함께
  활성화되어 있었다. 활성화된 `IPlayerAttackHitSource`가 있으면 기존 구형
  판정을 생략한다. 히트박스에는 상태/Tick 검사를 추가했다.
- 풀 테스트의 고정 크기 1 가정을 제거하고 실제 대여 시 크기 복원을 검증한다.
- Scene 공격 테스트는 Animation Event 이전부터 재생하고 다른 적을 격리한다.

## 실행 결과

다음 두 명령을 원본 프로젝트에서 실행했다. 패키지나 Unity 버전은 변경하지 않았다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/merge-original-editmode.xml -logFile Logs/merge-original-editmode.log
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults Logs/merge-playmode.xml -logFile Logs/merge-playmode.log
```

- Edit Mode: **42/42 통과**. 상태 전이, 요청 큐, 통계, 물리 복원, 풀 초기화,
  추가한 공격별 단일 판정/빗나감/취소/이전 Tick 거부 테스트 포함.
- Play Mode: **8/8 통과**. Main 연결, 적 Animator와 풀 복원, 검/Animator 정지,
  공격 이벤트→점수/HUD→Retry, Special, 대화/Skip/재시작, Finishing 정산 포함.
- 스크립트 컴파일 성공. 컴파일 로그의 `error CS`/`warning CS` 없음.
- `git diff --check` 통과. 테스트 실행 중 생긴 TimeManager 직렬화 변경은 복구했다.

## 수동 검증 필요

`-nographics` 테스트로 화면 품질과 성능을 검증하지는 않았다.

1. Main Play: 일반 공격/차지의 표시 범위, 전진, 적중 위치와 연출을 확인한다.
2. 공격 도중 Pause→Resume, Retry 확인 취소/승인, Quit, Result Retry를 실행한다.
   시작 화면과 새 라운드의 적/점수/게이지 및 대화 재생을 확인한다.
3. 16:9 및 목표 해상도에서 UI 겹침을 확인하고, 목표 하드웨어에서
   적 100/200/300명 성능을 측정한다.

게임 규칙 수치와 provisional 설정은 변경하지 않았다.

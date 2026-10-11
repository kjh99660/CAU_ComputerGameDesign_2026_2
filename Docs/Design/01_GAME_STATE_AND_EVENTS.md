# GameState, Event Queue 및 Round Lifecycle 상세 설계

> 문서 상태: 상태·이벤트·물리·Reset 작업의 규범 문서  
> 원본: `GameStateManager_상세_설계서.pdf`  
> 최신 설계 반영: 원본의 `Playing`은 `Battle`로 통일하고, `Countdown` 및 `QuitToStartRequest`를 추가했다.

## 1. 핵심 정의

`GameStateManager`는 개별 기능을 직접 수행하는 객체가 아니다. 현재 게임 상태를 소유하고, 상태 변경 요청을 검증하며, 유효한 전환 결과를 각 독립 시스템에 알리는 중앙 상태 조정자다.

### 책임

- `CurrentState`, `PreviousState`, 현재 `Tick` 관리. Tick은 프레임 수가 아니라 Reset 때 증가하는 라운드 세대 토큰이다.
- Request와 현재 상태 조합 검증
- 이전 상태 이탈, 상태 값 변경, 새 상태 진입의 원자적 처리
- 전환 성공 직후 `GameStateChangedEvent` 즉시 Broadcast
- `RoundResetCoordinator`의 Reset 완료 보고를 받은 뒤 `Ready` 전환

### 책임이 아닌 것

- Player 이동, 공격, Enemy AI와 Spawn 위치 계산
- Dialogue 문장 출력과 UI Layout
- Score 계산식과 전투 판정의 세부 구현
- 개별 Rigidbody 정지와 복원
- 각 시스템의 Reset 세부 로직

## 2. 표준 상태

기존 enum이 없다면 다음 이름을 사용한다.

```csharp
public enum GameState
{
    Initializing,
    Ready,
    IntroDialogue,
    Countdown,
    Battle,
    Paused,
    Finishing,
    OutroDialogue,
    Result
}
```

`Playing`과 `Battle`을 동시에 만들지 않는다. 최신 표준 이름은 `Battle`이다.

## 3. 정상 상태 흐름

```text
Initializing
→ Ready
→ IntroDialogue
→ Countdown
→ Battle
→ Finishing
→ OutroDialogue
→ Result
```

보조 흐름:

- `Battle ↔ Paused`
- `Paused --RestartRequest→ Reset 완료 후 Ready`
- `Paused --QuitToStartRequest→ Reset 완료 후 Ready`
- `Result --RestartRequest→ Reset 완료 후 Ready`

Reset은 공개 GameState가 아니라 `RoundResetCoordinator` 또는 동등 시스템의 내부 절차다. Coordinator가 Reset 순서와 완료 Barrier를 소유하고, `GameStateManager`는 완료 보고를 받은 뒤 `Ready`로 전환한다. 기존 구조상 여러 프레임이 필요한 경우에도 새 상태를 임의로 추가하기 전에 팀과 합의한다.

## 4. 상태 정의

### Initializing

- Scene 로드 직후 최초 상태다.
- Pool, Player, Enemy, Arena, UI, Timer, Score, Physics 등 필수 시스템을 초기화한다.
- 입력을 받지 않고 초기화 완료 보고를 수집한다.
- 모든 필수 시스템이 준비되고 Player 및 초기 Enemy 배치가 끝난 뒤에만 Ready로 이동한다.

### Ready

- 한 Round를 시작할 준비가 끝난 상태다.
- Player와 Enemy는 초기 위치에 있으며 Enemy는 Idle이다.
- Timer와 Score는 초기화됐지만 진행하지 않는다.
- Start UI만 입력을 받는다.
- `StartRequest`를 받으면 IntroDialogue로 이동한다.

### IntroDialogue

- Start 이후 도입 대화를 재생하는 완전한 비전투 상태다.
- Player Input, AI, Spawn, Timer, Physics, Collision, Damage, Score를 정지한다.
- Dialogue 진행 및 Skip 입력만 unscaled time으로 동작한다.
- 완료 또는 Skip은 모두 `DialogueCompleteRequest`를 발행하고 Countdown으로 이동한다.

### Countdown

- `3 → 2 → 1 → START`로 전투 시작 프레임을 동기화한다.
- Player, Enemy AI, Spawn, Physics, Timer와 Camera 입력을 정지한다.
- Countdown은 unscaled time을 사용한다.
- START 연출이 끝나는 프레임에 `CountdownCompleteRequest`를 발행한다. 다음 프레임 초반에 Battle로 전환하며, 그전까지 Countdown의 정지 정책을 유지한다.

### Battle

- 정상 전투가 진행되는 유일한 상태다.
- Player Input, AI, Spawn, Timer, Animation, Physics, Collision, Damage와 Score를 활성화한다.
- `PauseRequest`는 다음 프레임에 Paused로, Timer 0은 `TimerReachedZero` 요청을 거쳐 다음 프레임에 Finishing으로 전환한다. Timer 0부터 새 전투 명령은 차단한다.

### Paused

- Battle 중 사용자의 Pause 요청으로만 진입한다.
- Input, AI, Spawn, Timer, Collision, Damage와 Score를 정지한다.
- Physics 관리 시스템이 운동 상태를 Snapshot하고 Simulation을 정지한다.
- Pause UI만 unscaled time으로 동작한다.
- Resume, Retry 확인, Quit to Start 입력을 허용한다.

### Finishing

- Timer 0 이후 기존 전투 결과를 정산하는 제한된 슬로모션 상태다.
- Player Input, Spawn, 새 AI 판단, 새 이동/공격 명령은 차단한다.
- 이미 시작된 Animation, Physics, Projectile, Collision, Damage, Death, Score는 계속 처리한다.
- `Time.timeScale = 0.5`를 사용한다.
- unscaled time 기준 2.5초 뒤 정산 창을 닫고 OutroDialogue로 이동한다.

### OutroDialogue

- Finishing 정산 후 Result 이전의 완전한 비전투 상태다.
- 전투와 물리를 정지하고 Dialogue 및 Skip만 unscaled time으로 동작한다.
- 완료 또는 Skip은 `DialogueCompleteRequest`를 발행하고 Result로 이동한다.

### Result

- Round의 최종 Score와 통계를 고정한다.
- 모든 전투 이벤트 생산자와 물리를 정지한다.
- Result UI와 Retry만 활성화한다.
- `RestartRequest`를 받으면 Reset 완료 후 Ready로 이동한다.

## 5. 허용 전이표

| 현재 상태 | 요청 또는 조건 | 다음 상태/처리 |
| --- | --- | --- |
| Initializing | InitializationCompleted | Ready |
| Ready | StartRequest | IntroDialogue |
| IntroDialogue | DialogueCompleteRequest | Countdown |
| Countdown | CountdownCompleteRequest | Battle |
| Battle | PauseRequest | Paused |
| Paused | ResumeRequest | Battle |
| Battle | TimerReachedZero | Finishing |
| Finishing | FinishingCompleteRequest | OutroDialogue |
| OutroDialogue | DialogueCompleteRequest | Result |
| Paused | RestartRequest | Reset → Ready |
| Paused | QuitToStartRequest | Reset → Ready |
| Result | RestartRequest | Reset → Ready |

### 금지 규칙

- Dialogue, Countdown, Finishing, Result에서는 Pause를 무시한다.
- Ready의 Resume, Result의 Start 등 의미가 맞지 않는 요청은 무시한다.
- Result에서 Battle로 직접 이동하지 않는다.
- Paused의 StartRequest로 새 Round를 열지 않는다.
- 같은 상태로의 중복 전이는 기본적으로 무시한다.
- 외부 시스템에 범용 `ChangeState(GameState)`를 공개하지 않는다.

거부된 요청은 개발 빌드에서 `Frame`, `Tick`, `CurrentState`, `Request`, `RejectReason`을 진단 가능하게 기록한다.

## 6. 전환 원자성

전환은 다음 순서를 하나의 논리 단위로 처리한다.

1. 현재 상태와 요청의 조합 검증
2. 이전 상태 `OnExit`
3. `PreviousState`와 `CurrentState` 갱신
4. 새 상태 `OnEnter`
5. 같은 프레임에 `GameStateChangedEvent` Broadcast

`OnExit` 또는 `OnEnter` 중 예외가 발생해도 `Time.timeScale`, 물리 정지 상태, 입력 소유권처럼 전역 영향을 주는 값이 누수되지 않도록 복구 경로를 둔다.

## 7. 시스템 구성과 의존 규칙

| 구성 요소 | 책임 | 금지 사항 |
| --- | --- | --- |
| Input/UI Adapter | 장치 입력을 Request로 정규화 | 상태 직접 변경 |
| EventManager | Request Queue와 Notification 전달 | 전이 규칙 판단 |
| GameStateManager | Request 검증과 상태 전환 | 도메인 객체 직접 제어 |
| Domain System | StateChanged에 따라 자신의 동작 변경 | 다른 시스템 내부 제어 |
| RoundResetCoordinator | Reset 순서와 완료 Barrier | 임의 상태 전환 |
| PhysicsPauseManager | Body Registry, Snapshot, Suspend/Restore | GameState 소유 |

`EventManager`와 `GameStateManager`는 각각 독립 Singleton으로 둘 수 있지만 서로를 생성하거나 소유하지 않는다. 순환 초기화에 의존하지 않고 테스트에서 교체 가능한 인터페이스 또는 주입 지점을 둔다.

## 8. Request Queue와 Notification

### 8.1 Request

- 외부에서 GameStateManager 방향으로 흐른다.
- 하나의 FIFO Queue에서 순서를 보장한다.
- 발행된 다음 프레임의 `Update` 초반에 처리한다. 현재 프레임에 발행된 Request는 다음 drain용 버퍼에 보관한다.
- 각 Request는 처리 시점의 최신 `CurrentState`로 다시 검증한다.
- Round 종속 Request에는 `Tick`을 포함한다.

대표 Request:

- `StartRequest`
- `PauseRequest`
- `ResumeRequest`
- `DialogueCompleteRequest`
- `CountdownCompleteRequest`
- `RestartRequest`
- `QuitToStartRequest`
- 내부 조건용 `InitializationCompleted`, `TimerReachedZero`, `FinishingCompleteRequest`

### 8.2 Notification

- GameStateManager에서 구독자 방향으로 흐른다.
- 성공한 전환만 `GameStateChangedEvent`를 발생시킨다.
- Queue에 넣어 다음 프레임으로 미루지 않고 전환 직후 즉시 Broadcast한다.
- 구독 시스템은 같은 알림을 다시 받아도 결과가 달라지지 않게 멱등적으로 처리한다.

### 8.3 입력 정규화

| 입력 원본 | Request |
| --- | --- |
| Start Button | StartRequest |
| ESC / Pause Button | PauseRequest |
| ESC / Resume Button | ResumeRequest |
| Dialogue 완료 / Skip | DialogueCompleteRequest |
| Countdown 종료 | CountdownCompleteRequest |
| Pause Retry 확인 | RestartRequest |
| Pause Quit | QuitToStartRequest |
| Result Retry | RestartRequest |

GameStateManager는 요청이 Keyboard에서 왔는지 UI에서 왔는지 알지 않는다.

### 8.4 같은 프레임의 요청

- Pause → Pause: 첫 요청으로 Paused가 되고 두 번째 요청은 거부
- Pause → Resume: FIFO 순서에 따라 Battle → Paused → Battle
- Timer 0 요청 → Pause 요청: 두 요청이 이 순서로 Queue에 들어왔다면 다음 프레임에 Finishing 진입 후 Pause 거부
- 처리 도중 생성된 Request도 다음 프레임의 drain으로 넘겨 무한 재진입을 막는다.

## 9. 상태별 Runtime 정책

| 상태 | Input | Enemy AI | Spawn | Timer | Physics/Collision | Damage/Score | 주요 UI |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Initializing | OFF | OFF | 준비 | OFF | OFF | OFF | Loading |
| Ready | Start만 | Idle | 초기 배치 | 정지 | 정지 | OFF | Start |
| IntroDialogue | 진행/Skip | OFF | OFF | 정지 | 정지 | OFF | Dialogue |
| Countdown | OFF | OFF | OFF | 정지 | 정지 | OFF | Countdown |
| Battle | ON | ON | ON | 감소 | ON | ON | HUD |
| Paused | Pause UI만 | OFF | OFF | 정지 | Snapshot 후 정지 | OFF | Pause |
| Finishing | OFF | 새 결정 OFF | OFF | 0 고정 | 기존 처리 유지 | 기존 판정 ON | TIME UP |
| OutroDialogue | 진행/Skip | OFF | OFF | 정지 | 정지 | OFF | Dialogue |
| Result | Retry만 | OFF | OFF | 정지 | 정지 | 확정 | Result |

Game Duration은 Battle에서만 감소한다. UI와 Dialogue Animation, Countdown, Finishing Duration은 필요에 따라 unscaled time을 사용한다.

## 10. 초기화 Barrier

Ready 진입 전 다음 조건을 모두 충족한다.

- Pool과 Enemy/Projectile/Effect 자원 준비
- Player 인스턴스, 초기 위치, 입력/전투 컴포넌트 준비
- EnemyManager, SpawnManager, Arena 데이터 준비
- Timer, Score, HUD, Start, Dialogue, Result UI 준비
- Physics Registry 준비
- 초기 Enemy Spawn 및 Idle 설정 완료

Script Execution Order의 우연한 순서에 의존하지 않는다. 초기화 실패 또는 미완료가 있으면 Start UI를 열지 않고 원인을 기록한다.

## 11. Pause와 Dialogue 정지

`Time.timeScale = 0`을 주 정지 수단으로 사용하지 않는다. 각 도메인 시스템이 `GameStateChangedEvent`에 반응해 Update와 입력을 멈춘다.

### Paused 진입

1. Battle에서 PauseRequest 검증
2. Paused 전이 및 즉시 알림
3. Input, AI, Spawn, Timer, Damage/Score 중지
4. Physics Snapshot 및 Suspend
5. Pause UI 활성화

### Resume

- Paused에서만 허용한다.
- Battle로 전환하고 Physics Snapshot을 복원한다.
- 다른 시스템도 Battle 정책에 따라 활성화한다.
- Dialogue 중 Pause를 허용하지 않으므로 State Stack을 만들지 않는다.

### Dialogue

- Intro는 Countdown으로, Outro는 Result로 이동한다.
- 일반 완료와 Skip은 같은 `DialogueCompleteRequest`를 사용한다.
- Dialogue UI만 unscaled time으로 동작한다.

## 12. PhysicsPauseManager

### Registry

- 활성화 또는 Pool 대여 시 Register
- 비활성화, 파괴 또는 Pool 반환 시 Unregister
- Pause/Dialogue마다 Scene 전체를 검색하지 않음
- Rigidbody/Rigidbody2D 차이는 Adapter로 숨길 수 있음

### Snapshot

- Linear Velocity
- Angular Velocity
- Simulation/Kinematic 관련 Flag
- Body ID
- Tick

### 상태별 처리

| 전이/상태 | 동작 |
| --- | --- |
| Battle → Paused | Snapshot 후 Suspend |
| Ready/Dialogue/Countdown/Result | 정지 유지 |
| Paused → Battle | 같은 Round의 Snapshot 복원 후 제거 |
| Battle → Finishing | Simulation 유지 |
| Finishing → OutroDialogue | Suspend |
| Reset | Snapshot 폐기, Registry 재정합 |

정지 중 새 Body가 등록되면 즉시 정지 상태로 편입한다. 같은 Body의 이중 저장·복원, Pool 반환 후 이전 속도 복원을 막는다.

## 13. Finishing과 Score 경계

### 진입

1. Battle Timer 0에서 `TimerReachedZero`를 발행하고 Timer를 0으로 고정
2. 같은 프레임부터 Input과 Spawn 차단, 다음 프레임 초반에 Finishing 전환
3. 새 AI Decision, 이동, 공격 명령 차단
4. `Time.timeScale = 0.5`
5. 기존 Animation, Physics, Collision, Damage, Death, Score 유지
6. unscaled time 기준 2.5초 정산 시작

### Score 처리

| 상황 | 처리 |
| --- | --- |
| Timer 0 전에 시작된 Player 공격이 Finishing 중 Enemy 처치 | Kill Score 반영 |
| Timer 0 전에 시작된 Enemy 공격이 Finishing 중 Player 적중 | Score 10% 감소 규칙 적용 |
| Timer 0 이후 새 Player 입력 | 생성 단계에서 차단 |
| Timer 0 이후 새 Enemy 판단 | 생성 단계에서 차단 |
| 2.5초 뒤 도착한 Damage/Score | State/Round Guard로 거부 |

### 종료

1. Damage/Score 수용 창 닫기
2. `Time.timeScale = 1.0` 복구
3. 전투 정지 및 물리 Suspend
4. OutroDialogue 전환

정상 완료, Reset, 예외 이탈 등 모든 경로에서 TimeScale 복구를 보장한다.

## 14. Reset, Retry, Quit to Start

Scene Reload를 사용하지 않는다. Reset은 여러 번 호출돼도 같은 결과가 나오는 멱등 연산이어야 한다.

### 순서

1. Input, AI, Spawn, Timer 및 전투 Event Producer 정지
2. `GameStateManager`가 수락한 Reset 요청에서 `Tick` 증가, Coordinator가 새 `Tick`을 기준으로 이전 Queue/Coroutine/Task/Animation Event 무효화
3. Enemy, Projectile, Effect Pool 반환
4. Player 위치, 회전, Action State, Gauge, 임시 효과 초기화
5. Score, Combo, Kill, Hit, Special Use, Timer 초기화
6. Spawn/AI/Physics Snapshot과 Registry 정리
7. 초기 Enemy를 다시 Spawn하여 Idle 배치
8. UI와 Dialogue 표시 상태 초기화
9. Coordinator의 모든 Reset 완료 보고 후 `GameStateManager`가 Ready 전환

### 입력별 차이

- Pause Retry: 확인 Modal 승인 후 `RestartRequest`
- Result Retry: 확인 없이 `RestartRequest`
- Pause Quit: 확인 없이 `QuitToStartRequest`
- Quit은 `Application.Quit()`을 호출하지 않는다.

Reset 도중 추가 Reset 요청은 무시한다.

## 15. 설정값

| Key | 기본값 | 시간 기준 |
| --- | ---: | --- |
| GameDuration | 100초 | Battle gameplay time |
| FinishingTimeScale | 0.5 | `Time.timeScale` |
| FinishDuration | 2.5초 | unscaled time |

값은 Inspector 또는 설정 데이터로 노출한다.

## 16. 필수 안전장치

- 상태별 Request Whitelist
- 중복 전이 거부
- Finishing 모든 이탈 경로의 TimeScale 복구
- Body ID와 Tick 기반 Snapshot Guard
- Battle/Finishing의 유효 Round에서만 Score Event 수락
- Reset 완료 전 Ready/Start UI 금지
- Event 구독 등록/해제 대칭
- 이전 Round의 Coroutine, Task, Animation Event와 Queue 무효화

## 17. 필수 테스트

1. Initializing → Ready → IntroDialogue → Countdown → Battle
2. Intro/Outro 일반 완료와 Skip이 동일 Request 경로 사용
3. Battle ↔ Paused 반복 시 Timer, AI, Input, Physics 정지/복원
4. Knockback 중 Pause에서 위치 고정 후 속도 복원
5. Countdown 동안 모든 전투와 Camera 입력 정지
6. Timer 0 직전 공격이 Finishing 중 처치하면 Score 반영
7. Finishing 중 새 입력, Spawn, AI Decision 차단
8. Finishing 종료 뒤 지연 Damage/Score 거부
9. Finishing, Reset, 예외 이탈 뒤 `Time.timeScale == 1.0`
10. Pause Retry 확인/취소
11. Pause Quit가 확인 없이 Reset 후 Ready로 이동하고 앱을 종료하지 않음
12. Result Retry가 확인 없이 Reset 후 Ready로 이동
13. Retry 후 IntroDialogue 재출력과 Skip
14. 이전 Tick Event와 Physics Snapshot 거부
15. Reset 연속 호출의 멱등성

## 18. 관련 문서

- 프로젝트 목표, Combat, Enemy, 최적화: `00_PROJECT_OVERVIEW.md`
- UI 화면, Canvas, UI Request: `02_UI_AND_SCREEN_FLOW.md`
- 전역 구현 및 검증 규칙: `../../AGENTS.md`

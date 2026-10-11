# AGENTS.md — 3D 다대일 전투 기술 데모 구현 지침

이 파일은 저장소 루트에 둔다. Codex는 구현, 수정, 리뷰, 테스트 전에 이 문서를 기준으로 프로젝트의 설계 의도와 완료 조건을 확인한다.

## 1. 프로젝트 목표

- Unity와 C#으로 단일 Scene 기반의 3D 다대일 전투 기술 데모를 만든다.
- 플레이 감각은 짧은 무쌍형 전투에 가깝고, 한 라운드는 100초다.
- 이 프로젝트의 핵심 시연 대상은 상태 기반 게임 흐름, 요청 이벤트 큐, 군중 처리, 물리 일시정지/복원, 풀링, 전투 피드백, UI 연동이다.
- 기능 수를 늘리는 것보다 상태 전환의 결정성, 재시작 안정성, 다수 적 상황의 성능, 검증 가능성을 우선한다.

## 2. Codex 작업 원칙

1. 작업 시작 시 저장소 구조, 기존 `AGENTS.md`/`AGENTS.override.md`, Unity 버전, 패키지, 어셈블리 정의, 테스트와 CI 명령을 먼저 확인한다.
2. 이 문서와 기존 코드가 충돌하면 사용자 요구가 최우선이다. 명시적 요구가 없다면 기존 코드의 공개 API, 네이밍, 폴더 구조를 보존하면서 이 설계에 수렴시킨다.
3. 범위가 큰 작업은 먼저 상태 전이와 소유 시스템을 식별하고, 작은 수직 슬라이스로 나눠 구현한다. 각 슬라이스마다 컴파일 또는 테스트를 수행한다.
4. 관련 없는 리팩터링, 대규모 파일 이동, 패키지 추가, Unity/렌더 파이프라인 업그레이드는 하지 않는다.
5. 사용자의 기존 변경을 보존한다. dirty worktree에서는 관련 파일만 수정하고 다른 변경을 되돌리지 않는다.
6. 값이 확정되지 않은 항목을 임의의 게임 규칙으로 확정하지 않는다. 조절 가능한 설정으로 노출하고, 결과에 큰 영향을 주면 질문한다.
7. 실행하지 못한 테스트를 실행했다고 말하지 않는다. 자동 실행이 불가능하면 이유와 정확한 수동 검증 절차를 남긴다.
8. 새 기능은 최소한 하나의 자동 테스트 또는 재현 가능한 Play Mode 검증 절차를 동반한다.

## 3. 변경 금지 요구사항

다음은 확정된 제품 요구사항이다. 별도 요청 없이 추가하거나 되돌리지 않는다.

- Player HP를 만들지 않는다.
- Enemy는 유효한 공격 한 번에 사망한다.
- Damage Number를 표시하지 않는다.
- Aim Assist와 Lock-on 기능 및 Indicator를 만들지 않는다.
- Retry는 Scene Reload가 아니라 현재 Scene 안에서 전체 라운드를 초기화한다.
- Pause 메뉴에는 `Resume`, `Retry`, `Quit`가 모두 있어야 한다.
- Pause 메뉴의 Retry만 확인 모달을 사용한다.
- Quit에는 확인창이 없으며 애플리케이션을 종료하지 않고 Start 화면으로 복귀한다.
- Result의 Retry에는 확인창이 없으며 Start 화면으로 복귀한다.
- Intro Dialogue는 매 Retry 이후에도 재생하되 화면 외곽의 Skip 버튼으로 건너뛸 수 있다.
- Pause와 Dialogue는 `Time.timeScale = 0` 하나에 의존하지 않고 각 시스템이 상태 변경에 반응해 멈춘다.
- Rigidbody 정지와 복원은 전용 Physics 관리 시스템이 담당한다.

## 4. 표준 게임 상태

기존 상태 enum이 없다면 아래 이름을 표준으로 사용한다. 같은 의미의 `Playing`과 `Battle`을 동시에 만들지 말고 `Battle`로 통일한다.

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

정상 흐름:

`Initializing → Ready → IntroDialogue → Countdown → Battle → Finishing → OutroDialogue → Result`

보조 흐름:

- `Battle ↔ Paused`
- `Result --RestartRequest→ Ready`
- `Paused --RestartRequest→ Ready`
- `Paused --QuitToStartRequest→ Ready`
- Reset이 여러 프레임에 걸려도 별도 공개 상태를 임의로 추가하지 않는다. 필요하면 Reset Coordinator의 내부 단계로 관리한다.

### 상태별 핵심 정책

| 상태 | 게임플레이 | 물리 | 시간/점수 | 표시 UI |
| --- | --- | --- | --- | --- |
| Initializing | 전부 차단 | 정지 | Timer 정지 | Loading 또는 숨김 |
| Ready | Enemy는 스폰된 Idle, Player 입력 차단 | 정지 | Timer 정지 | Start Overlay |
| IntroDialogue | 완전한 비전투 | 정지 | Timer 정지 | Intro Dialogue, Skip |
| Countdown | 입력·AI·Spawn 차단, Camera 고정 | 정지 | Timer 정지 | 3→2→1→START |
| Battle | Player·AI·Spawn 활성 | 활성 | Timer·Score 활성 | 전체 Battle HUD |
| Paused | 전투 전부 정지 | 스냅샷 후 정지 | Timer 정지 | Pause Menu |
| Finishing | 새 입력·AI 판단·Spawn 차단, 진행 중 결과만 처리 | 활성 | Timer 0, Score 활성 | TIME UP, Score, Timer |
| OutroDialogue | 완전한 비전투 | 정지 | Score 확정 전 상태 | Outro Dialogue, Skip |
| Result | 전투 전부 정지 | 정지 | 최종 Score 고정 | Result UI |

## 5. 상태 전환 소유권

- `GameStateManager`만 현재 상태를 소유하고 변경한다.
- 외부에 범용 `ChangeState(GameState)`를 공개하지 않는다.
- UI, 키보드, Timer, Dialogue, Countdown은 상태를 직접 바꾸지 않고 요청 이벤트만 발행한다.
- `GameStateManager`는 요청의 현재 상태 유효성을 검사한 뒤 전환하고, 성공한 전환만 알림으로 발행한다.
- `GameStateManager`는 Player, Enemy, Spawn, Physics 또는 UI의 세부 동작을 직접 제어하지 않는다.
- `RoundResetCoordinator`가 Reset 순서, 필수 시스템의 완료 보고와 Barrier를 소유한다. `GameStateManager`는 Reset 요청의 상태 유효성을 검사하고 Coordinator의 완료 보고를 받은 뒤 `Ready`로 전환한다.
- 각 시스템은 `GameStateChangedEvent`를 구독하고 자기 책임 범위만 활성화/정지한다.
- 금지된 전환은 무시하되 개발 빌드에서 요청명, 현재 상태, 거부 이유를 진단 가능하게 남긴다.

### 허용 요청과 전환

| 요청/조건 | 허용 상태 | 결과 |
| --- | --- | --- |
| InitializationCompleted | Initializing | Ready |
| StartRequest | Ready | IntroDialogue |
| DialogueCompleteRequest | IntroDialogue | Countdown |
| CountdownCompleteRequest | Countdown | Battle |
| PauseRequest | Battle | Paused |
| ResumeRequest | Paused | Battle |
| TimerReachedZero | Battle | Finishing |
| FinishingCompleteRequest | Finishing | OutroDialogue |
| DialogueCompleteRequest | OutroDialogue | Result |
| RestartRequest | Paused, Result | 라운드 Reset 후 Ready |
| QuitToStartRequest | Paused | 라운드 Reset 후 Ready |

Skip 버튼은 별도 우회 전이를 만들지 않고 현재 Dialogue를 완료한 뒤 `DialogueCompleteRequest`를 사용한다.

## 6. Event System 계약

### 요청과 알림을 분리한다

- Request Event: 외부 시스템에서 상태 관리자 방향. 순서와 재현성을 위해 FIFO Queue에 넣는다.
- Notification Event: 상태 관리자에서 구독자 방향. 상태 전환 직후 같은 프레임에 즉시 Broadcast한다.
- `GameStateChangedEvent`를 요청 큐에 다시 넣어 한 프레임 늦추지 않는다.

### 처리 규칙

- Request Queue는 요청이 발행된 다음 프레임의 `Update` 초반에 FIFO로 비운다. 현재 프레임에 발행된 요청은 다음 drain을 위한 버퍼에 보관한다.
- Keyboard와 UI 입력은 Adapter에서 동일한 요청 타입으로 정규화한다. 예: ESC와 Pause 버튼은 모두 `PauseRequest`다.
- 동일 프레임 중복 요청은 순서대로 검증한다. 첫 요청으로 상태가 바뀌면 뒤 요청은 바뀐 상태를 기준으로 다시 검증한다.
- 요청 처리 중 발생한 요청도 다음 프레임의 drain으로 넘겨 무한 재진입을 막는다.
- 이전 라운드의 지연 이벤트가 새 라운드에 적용되지 않도록 모든 라운드 종속 요청/전투 이벤트에 `Tick`을 포함한다. 여기서 Tick은 프레임 수가 아니라 Reset 때 증가하는 라운드 세대 토큰이다.
- Reset 시작 시 Producer를 먼저 막고, 오래된 Queue 항목과 비동기 콜백을 무효화한다.
- Event 구독은 `OnEnable`/`OnDisable` 또는 명확한 생명주기에서 대칭적으로 등록/해제한다.

### Singleton 관계

- `EventManager`와 `GameStateManager`는 각각 독립 서비스로 둘 수 있다.
- 둘 중 하나가 다른 하나를 생성하거나 소유하지 않는다.
- 순환 초기화와 정적 생성 순서 의존성을 만들지 않는다.
- 테스트에서 교체 가능한 인터페이스 또는 주입 지점을 제공한다.

## 7. Physics 관리 계약

전용 `PhysicsPauseManager` 또는 기존 동등 시스템이 다음 책임을 갖는다.

- 활성 Rigidbody를 Registry로 관리한다. 매 Pause마다 전역 검색하지 않는다.
- Pool에서 대여/활성화할 때 등록하고 반환/비활성화할 때 해제한다.
- Pause, Ready, IntroDialogue, Countdown, OutroDialogue, Result 진입 시 속도, 각속도, 시뮬레이션/kinematic 관련 복원 정보를 스냅샷하고 정지한다.
- Battle 복귀 시 같은 `Tick`의 유효한 스냅샷만 복원한다.
- Finishing에서는 물리를 계속 시뮬레이션한다.
- 정지 중 새로 등록된 Body는 즉시 정지 상태로 편입한다.
- 파괴되거나 Pool로 반환된 Body의 스냅샷은 폐기한다.
- 중복 Pause/Resume 호출과 Reset 중 호출에 대해 멱등성을 보장한다.
- 프로젝트가 Rigidbody와 Rigidbody2D를 함께 사용하면 Adapter로 구분하고 잘못된 속성 접근을 피한다.
- `GameStateManager`가 개별 Rigidbody를 직접 참조하지 않게 한다.

## 8. 초기화와 시작 시퀀스

1. Scene 로드 후 상태는 `Initializing`이다.
2. Pool, Player, Enemy, Arena, UI, Timer, Score, Physics 시스템이 각자 초기화 완료를 보고한다.
3. 초기화 Barrier가 모든 필수 시스템의 준비 완료를 확인한다.
4. Enemy를 실제 전투 공간에 미리 Spawn하고 Idle로 둔다.
5. 상태를 `Ready`로 전환하고 Start Overlay를 표시한다.
6. Start 입력을 받으면 `IntroDialogue`로 전환한다.
7. Dialogue 완료 또는 Skip 후 `Countdown`으로 전환한다.
8. `3 → 2 → 1 → START`를 unscaled time으로 재생한다. 이때 Camera와 모든 전투 시스템은 정지한다.
9. START 연출 종료 시 `CountdownCompleteRequest`를 발행한다. 다음 프레임 초반에 `Battle`로 전환하고 Player Input, AI, Spawn, Physics, Timer, Battle HUD를 활성화한다. 전환 전까지 Countdown의 정지 정책을 유지한다.

초기화 완료 순서는 Script Execution Order에 우연히 의존하지 말고 명시적 Barrier/Coordinator로 보장한다.

## 9. 전투 규칙과 피드백

- 기본 조작은 3인칭 숄더 뷰, WASD 이동, 마우스 카메라를 전제로 한다. 기존 Input System 설정을 우선한다.
- 지원 행동은 방향 회피, 누르고 있는 동안의 Guard, Jump, Normal Attack 1→2→3, Charge Attack, Special Skill이다.
- Combo 입력 기본 창은 0.4초이며 설정 가능해야 한다.
- 피격 무적 기본값은 0.8초이며 설정 가능해야 한다.
- Enemy는 유효 타격 한 번에 죽는다. 체력 바와 Damage Number를 만들지 않는다.
- Player 피격 시 HP 대신 현재 Score의 10%를 감소시킨다. 반올림 정책은 한 곳에서 정의하고 테스트한다.
- 공격 적중 또는 피격으로 Special Gauge가 증가한다. 정확한 증가량과 Skill 소모량은 설정 데이터로 둔다.
- Score 증가량, Kill 보너스, Combo 배율은 확정값이 없으면 하드코딩하지 말고 설정 가능하게 둔다.
- 타격 피드백은 Animation, VFX, SFX, Hit Stop, Camera 반응, Score/Gauge UI Animation으로 제공한다.
- Aim Assist, Lock-on, Lock-on Indicator는 구현하지 않는다.

## 10. Finishing 계약

Timer가 0이 되면 `TimerReachedZero`를 발행하고 Timer를 0으로 고정한다. 다음 프레임 초반에 `Battle → Finishing`으로 전환한다. 그 사이 새 입력·AI 판단·Spawn·이동·공격 시작을 차단하고, 이미 시작된 결과만 처리한다.

- 즉시 차단: Player Input, Enemy Spawn, 새 AI Decision, 새 이동 명령, 새 공격 시작.
- 계속 처리: 이미 시작된 Animation, Physics, Collision, Damage, Death, Score 계산.
- `Time.timeScale = 0.5`로 슬로모션을 적용한다.
- 기본 지속시간은 실제 시간 2.5초이며 `unscaled time`으로 측정하고 설정 가능하게 한다.
- 100초 전에 시작되어 Finishing 중 적중한 공격, 처치, Player 피격은 Score에 반영한다.
- Finishing 종료 시 Score 수용 창을 닫고 `Time.timeScale`을 1.0으로 반드시 복구한다.
- 복구는 정상 종료뿐 아니라 중단, Reset, 예외적인 상태 이탈에서도 보장한다.
- 그 후 물리를 정지하고 `OutroDialogue → Result`로 진행한다.
- Finishing HUD는 Timer 0과 Score만 유지하고 Pause와 Gauge는 숨기며 `TIME UP`을 표시한다.

## 11. UI 상세 계약

### Canvas 계층

기존 구조가 없다면 책임별로 다음 계층을 사용한다. 이름보다 역할 분리를 우선한다.

- `HUDLayer`: Pause, Score, Timer, Special Gauge
- `CombatFeedbackLayer`: Score Change, Hit Flash. Damage Number는 없음
- `StateOverlayLayer`: Start, Countdown, Pause, Finishing, Result
- `DialogueLayer`: 좌/우 Portrait, Dialogue Box, Speaker/Body Text, Skip
- `TransitionLayer`: Fade 및 입력 차단

입력 우선순위는 `Transition > Dialogue/Modal > State Overlay > HUD`다. 아래 레이어가 보이더라도 상위 레이어가 입력을 독점하면 클릭을 받지 않아야 한다.

### 화면별 요구사항

- Ready: 낮은 불투명도의 검은 Overlay와 중앙 Start 버튼.
- Battle: 좌상단 Pause와 Score, 우상단 Timer, 하단 중앙 원형 Special Gauge.
- Dialogue: 두 Portrait를 동시에 표시하고 현재 화자는 밝게, 비화자는 어둡게 처리한다. 하단 Dialogue Box를 사용하며 화면 외곽에 Skip을 둔다.
- Pause: Resume, Retry, Quit. Retry는 확인 모달을 띄운다. Quit는 모달 없이 즉시 `QuitToStartRequest`를 발행한다.
- Result: Final Score, Kill Count, Max Combo, Hit Count, Special Use Count, Retry.

### UI 구현 규칙

- 1920×1080 기준 Canvas Scaler와 Match 0.5를 기본 제안으로 사용하되 기존 프로젝트 설정을 우선한다.
- Safe Area와 Anchor를 사용하고 해상도별로 겹침을 검증한다.
- Dialogue, Countdown, Pause UI Animation은 unscaled time을 사용한다.
- 반복되는 Score Change/VFX 요소는 Pooling한다.
- 숨겨진 Panel은 불필요한 Update와 Layout Rebuild를 발생시키지 않게 한다.
- UI Button handler에서 상태를 직접 변경하지 말고 Request Event만 발행한다.

## 12. Reset/Retry/Quit 계약

Reset은 Scene Reload 없이 현재 라운드를 완전히 초기화하는 멱등 연산이다.

`RoundResetCoordinator`가 아래 순서와 완료 Barrier를 담당한다. `GameStateManager`는 Reset 요청을 검증하고 완료 보고 이후에만 `Ready`로 전환한다.

1. Player Input, AI, Spawn, Combat Event Producer를 차단한다.
2. `GameStateManager`가 수락한 Reset 요청에서 `Tick`을 증가시키고, Coordinator가 새 `Tick`을 기준으로 이전 라운드 Queue, Coroutine, Task, Animation Event를 무효화한다.
3. 활성 Enemy, Projectile, Hit Effect를 Pool로 반환한다.
4. Player 위치, 회전, Action State, Special Gauge와 잔여 물리 상태를 초기화한다.
5. Score, Combo, Kill, Hit, Special Use 통계와 Timer를 초기화한다.
6. Spawn/AI/Physics Registry와 Snapshot을 정리한다.
7. Enemy를 초기 위치에 다시 Spawn하여 Idle로 둔다.
8. UI를 초기 상태와 동기화한다.
9. 모든 필수 시스템 준비 완료를 Coordinator가 보고하면 `GameStateManager`가 `Ready`로 전환한다.

세 경로를 구분한다.

- Pause Retry: 확인 모달 승인 후 `RestartRequest`.
- Result Retry: 확인 없이 `RestartRequest`.
- Pause Quit: 확인 없이 `QuitToStartRequest`; 애플리케이션 종료가 아니라 동일한 Reset 후 Start 화면.

어느 경로에서도 이전 라운드의 Score, Gauge, Rigidbody 속도, 적, 투사체, 예약 이벤트가 남아서는 안 된다.

## 13. 성능과 코드 품질

- 다수 Enemy의 근접 탐색과 분리는 Spatial Partitioning/Grid 등으로 제한한다. 모든 Enemy 쌍을 매 프레임 비교하지 않는다.
- 일반 플레이의 활성 Enemy 상한은 100명이다. 성능 실험에서는 별도 설정으로 200명과 300명도 허용한다.
- 성능 요구치는 60 FPS 목표이며, 최적화 전 구성에서도 20 FPS 이상을 하한으로 검증한다. 측정 플랫폼과 하드웨어는 확정 후 기록한다.
- Enemy, Projectile, 반복 VFX/UI Feedback은 Pooling한다.
- Hot Path에서 `FindObjectsOfType`, 전역 Scene 검색, 할당이 큰 LINQ, 불필요한 문자열 생성과 GC 할당을 피한다.
- 반복 접근 Component와 서비스 참조를 캐시한다.
- Update 책임을 상태별로 끄거나 중앙 Scheduler를 사용해 비활성 상태의 비용을 줄인다.
- 공개 API는 최소화하고, 상태와 Queue 컬렉션을 외부에서 직접 수정하지 못하게 한다.
- 상태 전환, Reset, Score 변경과 Physics Pause는 순수 로직과 Unity Adapter를 가능한 범위에서 분리해 테스트 가능하게 만든다.
- 새 `.meta` GUID를 재생성하지 않고 기존 Asset/Prefab/Scene 참조를 보존한다.
- Unity YAML을 직접 수정할 때는 대상과 직렬화 구조를 검증하고, 안전하게 검증할 수 없으면 Editor에서 수행할 수동 절차를 제공한다.

## 14. 권장 코드 경계

기존 폴더가 있으면 재구성하지 말고 아래 책임을 대응시킨다. 새 프로젝트일 때만 예시 구조를 따른다.

```text
Assets/Scripts/Runtime/Core/GameState
Assets/Scripts/Runtime/Core/Events
Assets/Scripts/Runtime/Physics
Assets/Scripts/Runtime/UI
Assets/Scripts/Runtime/Dialogue
Assets/Scripts/Runtime/Player
Assets/Scripts/Runtime/Enemy
Assets/Scripts/Runtime/Combat
Assets/Scripts/Runtime/Spawn
Assets/Tests/EditMode
Assets/Tests/PlayMode
```

권장 책임 이름은 `GameStateManager`, `EventManager`, `RoundResetCoordinator`, `PhysicsPauseManager`, `UIRoot`다. 기존에 같은 책임을 가진 클래스가 있으면 새 중복 서비스를 만들지 말고 기존 구현을 확장한다.

## 15. 구현 순서

별도 우선순위가 없으면 다음 순서로 진행한다.

1. 기존 프로젝트 진단과 테스트 기준선 확보.
2. `GameState`, 허용 전이표, GameStateManager의 제한된 API.
3. Request FIFO Queue와 즉시 StateChanged 알림.
4. 각 시스템의 상태 구독과 기본 활성화 정책.
5. Physics Registry, Pause Snapshot, Resume Restore.
6. 초기화 Barrier와 Ready/Start 흐름.
7. Intro Dialogue, Skip, Countdown, Battle 진입.
8. Timer 100초와 Finishing 슬로모션/Score 창.
9. Outro Dialogue와 Result.
10. 멱등 Reset, Pause Retry, Result Retry, Quit to Start.
11. UI 상태 연결, 해상도 검증, 성능 점검.

각 단계 완료 후 컴파일과 관련 테스트를 먼저 통과시킨 뒤 다음 단계로 이동한다.

## 16. 필수 테스트

### Edit Mode 또는 순수 로직 테스트

- 모든 허용 전이가 성공하고 금지 전이는 거부된다.
- 같은 프레임의 중복 요청이 FIFO 및 변경된 현재 상태 기준으로 처리된다.
- StateChanged 알림은 성공한 전환에만 정확히 한 번 발행된다.
- `Battle` 외 Pause 요청, `Paused` 외 Resume 요청이 상태를 바꾸지 않는다.
- Player 피격 시 현재 Score의 10% 감소 정책과 반올림이 일관된다.
- `Tick`이 다른 지연 이벤트는 무시된다.
- Reset을 연속 호출해도 결과가 동일하다.

### Play Mode 테스트

- `Start → Intro Dialogue → Countdown → Battle` 전체 흐름과 Dialogue Skip.
- Countdown 중 Player, AI, Spawn, Physics, Timer가 움직이지 않는다.
- 이동 중 Rigidbody가 Pause에서 멈추고 Resume 후 저장된 속도로 복원된다.
- Pause 중 생성된 Body가 정지 상태로 등록된다.
- Timer 0 직전 시작한 공격이 Finishing 중 적중하면 Score에 반영된다.
- Finishing 종료 이후의 늦은 Damage/Score 이벤트는 반영되지 않는다.
- Finishing 종료, Retry, 비정상 이탈 뒤 `Time.timeScale == 1.0`이다.
- Pause Retry 확인 취소는 현재 Paused 상태를 유지하고, 승인은 전체 Reset 후 Ready로 간다.
- Pause Quit는 확인 없이 Ready/Start로 가며 애플리케이션을 종료하지 않는다.
- Result Retry는 확인 없이 전체 Reset 후 Ready로 간다.
- Retry 후 Intro Dialogue가 다시 나오며 Skip이 동작한다.
- Reset 뒤 이전 Enemy, Projectile, Score, Gauge, Physics Snapshot, Queue 이벤트가 남지 않는다.
- Ready의 Enemy는 Spawn되어 있으나 Idle이고, Battle 진입 전 공격/이동하지 않는다.
- 16:9 및 지원 목표 해상도에서 UI가 겹치지 않고 Safe Area를 침범하지 않는다.

## 17. 완료 정의

작업 완료 보고 전 다음을 확인한다.

- 요청 범위가 실제 플레이 흐름으로 연결되어 있다.
- 새 컴파일 오류와 경고를 만들지 않았다.
- 가능한 자동 테스트가 통과했다.
- 상태 변경을 직접 수행하는 우회 경로가 없다.
- Event 구독 해제, Coroutine/Task 취소, Pool 반환이 누락되지 않았다.
- Retry/Quit 후 새 라운드가 최초 실행과 동일한 초기 조건에서 시작된다.
- 모든 `Time.timeScale` 변경 경로에 복구가 있다.
- Hot Path에 전역 검색이나 명백한 per-frame 할당을 추가하지 않았다.
- 변경 파일, 설계상 선택, 실행한 검증, 실행하지 못한 검증을 최종 응답에 간결하게 기록한다.

## 18. 미확정 항목 처리

다음은 별도 자료나 기존 코드가 없으면 임의 확정하지 않는다.

- 정확한 Score/Kill/Combo 수치와 Special Gauge 증감량
- Input Key binding의 최종값
- Spawn 간격과 거리
- UI 색상, Font, 최종 Art Asset과 Dialogue 대사
- Target Platform과 측정 하드웨어, 최소 해상도

이 값들은 SerializeField, ScriptableObject 또는 프로젝트의 기존 설정 시스템으로 노출한다. 합리적인 임시 기본값이 필요하면 코드와 작업 보고에 `provisional`임을 명시한다.

## 19. Codex 결과 보고 형식

최종 응답은 다음 순서로 짧고 검증 가능하게 작성한다.

1. 구현된 사용자 관점 결과.
2. 핵심 설계 선택과 변경 파일.
3. 실행한 테스트/빌드 명령과 결과.
4. 자동 검증하지 못한 항목과 수동 확인 절차.
5. 남은 미확정값 또는 후속 작업. 없으면 없다고 명시한다.

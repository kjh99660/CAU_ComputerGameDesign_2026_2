# UI 및 Screen Flow 상세 설계

> 문서 상태: UI 구현 작업의 규범 문서  
> 원본: `UI_상세_설계서.pdf`  
> 기준 화면: PC, 1920×1080, 16:9

## 1. 설계 목표

- 전투 화면 중앙을 비우고 상태, 시간, Score, Special Skill 정보를 가장자리에서 전달한다.
- 화면 표시와 입력 소유권이 현재 `GameState`와 항상 일치해야 한다.
- UI는 상태를 직접 변경하지 않고 Request를 발행한다.
- Dialogue, Pause, Countdown 등 전투 시간과 독립적인 UI는 unscaled time을 사용한다.
- Retry/Quit 후 이전 Round의 표시, 입력, Animation이 남지 않아야 한다.

다음 요소는 표시하거나 구현하지 않는다.

- Damage Number
- Aim Assist UI 또는 Aim Assist 범위
- Lock-on 기능, Target Marker, Lock-on Indicator
- Player/Enemy HP Bar

## 2. 화면 흐름

```text
Ready / Start
→ IntroDialogue
→ Countdown
→ Battle
→ Finishing
→ OutroDialogue
→ Result
→ Reset
→ Ready / Start
```

Pause 흐름:

```text
Battle
→ Paused
├─ Resume → Battle
├─ Retry → 확인 → Reset → Ready
└─ Quit to Start → 확인 없음 → Reset → Ready
```

Result의 Retry는 확인 없이 Reset 후 Ready로 돌아간다.

## 3. Canvas 계층

기존 구조가 없다면 다음 책임 계층을 사용한다. 정확한 GameObject 이름보다 입력과 표시 책임의 분리가 중요하다.

```text
05 TransitionLayer
   Fade / 전체 입력 차단

04 DialogueLayer
   Portrait / Dialogue Text / Skip

03 StateOverlayLayer
   Start / Countdown / Pause / Finishing / Result / Modal

02 CombatFeedbackLayer
   Score Change / Hit Flash

01 HUDLayer
   Pause / Score / Timer / Special Gauge
```

입력 우선순위:

`Transition > Dialogue 또는 Modal > State Overlay > HUD`

상위 계층이 입력을 소유하면 보이는 하위 UI도 클릭을 받지 않아야 한다.

## 4. 공통 Canvas와 해상도 규칙

- Reference Resolution: 1920×1080
- Canvas Scaler Match: 0.5를 초기 기본값으로 사용하고 실제 화면 시험으로 조정
- Pause/Score: 좌측 상단 Anchor
- Timer: 우측 상단 Anchor
- Gauge: 중앙 하단 Anchor
- Dialogue와 Modal: Safe Area 안에 배치
- 16:9 외 지원 화면비에서도 잘림, 겹침, 과도한 이동이 없어야 함
- 숨긴 Panel은 Raycast, Update, Animation, Layout Rebuild를 불필요하게 유지하지 않음
- 반복 Score Feedback과 UI Effect는 Pooling

## 5. Ready / Start UI

### 표시

- 낮은 Alpha의 검은 전체 Overlay
- 중앙 Start Button
- 선택적으로 프로젝트 제목과 핵심 조작 안내 한 줄
- 전투 배경에는 초기화된 Player와 Idle Enemy가 보일 수 있음

### 동작

- 초기화 Barrier가 끝난 Ready에서만 Start Button을 활성화한다.
- Start 클릭은 `StartRequest`만 발행한다.
- Start 이후 안내 문구와 Overlay를 닫고 IntroDialogue를 표시한다.
- 초기화 중에는 Start 입력을 받지 않는다.

## 6. Intro Dialogue UI

### 구성

- 좌측 Portrait
- 우측 Portrait
- 하단 Dialogue Box
- Speaker Name
- Body Text
- 계속 입력 안내
- 화면 오른쪽 외곽 Skip Button

### 강조 규칙

- 현재 화자: 밝은 명도, 정상 채도, 테두리 또는 작은 Motion
- 비화자: 낮은 명도와 채도
- 화자가 바뀌면 좌우 강조를 교체한다.

### 동작

- 전투 HUD를 숨긴다.
- 전투는 완전히 정지한다.
- Portrait와 Text Animation은 unscaled time을 사용한다.
- 일반 완료와 Skip은 모두 `DialogueCompleteRequest`를 발행한다.
- Intro 완료 후 다음 상태는 Countdown이다.

## 7. Countdown UI

### 표시

- 중앙 `3 → 2 → 1 → START`
- 숫자는 확대 후 축소되며 교체할 수 있다.
- HUD 데이터는 준비할 수 있지만 입력 가능한 Battle HUD는 아직 열지 않는다.

### 동작

- Player, AI, Spawn, Timer, Physics와 Camera 입력을 정지한다.
- Countdown은 unscaled time으로 재생한다.
- START가 사라지는 프레임에 `CountdownCompleteRequest`를 발행한다.
- 다음 프레임 초반의 요청 처리로 Battle에 진입하면 `GameStateChangedEvent`가 즉시 전달되어 Battle HUD와 전투 시스템을 활성화한다. 전환 전까지 Countdown의 정지 정책을 유지한다.

## 8. Battle HUD

### 좌측 상단

- Pause Button
- `SCORE` Label
- 현재 Score
- Score 증감 Feedback

Score 변경 시 짧은 Scale/Color Animation과 증감량을 HUD 안에 표시한다. 전투 대상 위에 숫자를 띄우지 않는다.

### 우측 상단

- `TIME` Label
- 남은 시간
- 10초 이하 Warning Color
- 0초에서 Timer를 고정하고 새 전투 명령을 차단한다. 다음 프레임의 Finishing 진입 시 화면을 전환한다.

Timer의 실제 감소는 Timer 시스템이 소유하고 UI는 표시만 담당한다.

### 중앙 하단

- 원형 Special Gauge
- Skill Input Icon 또는 Label
- Empty, Charging, Ready 상태
- Ready 시 외곽 Glow/Pulse

Gauge UI는 Player 발밑과 전투 시야를 가리지 않는 높이에 둔다.

### 중앙 전투 영역

Damage Number, Aim Assist, Lock-on Indicator, Target Marker를 두지 않는다. Enemy 무리, Hit VFX와 Animation이 피드백을 담당한다.

## 9. Score와 피격 피드백

### Enemy 처치 또는 Score 증가

- 적 Hit/Death Animation
- Hit Stop
- VFX와 Sound
- HUD Score 증가량
- Score Text Scale/Color Animation

### Player 피격

- 화면 가장자리 Red Flash/Vignette
- 약한 Camera 반응
- Player Hit Reaction
- 현재 Score 10% 감소 표시
- Special Gauge 증가 반영

피격마다 월드 공간 Damage Number를 생성하지 않는다.

## 10. Special Gauge 상태

| 상태 | 표시 |
| --- | --- |
| Empty | 빈 원형 Gauge |
| Charging | 충전 비율 표시 |
| Ready | Glow/Pulse 및 사용 가능 강조 |
| Used | 사용 직후 0으로 초기화 |

규칙:

- Combat 시스템이 현재값과 최대값을 제공하고 UI는 표시만 한다.
- Ready Pulse가 전투 화면 전체를 과도하게 번쩍이지 않게 한다.
- Finishing에서는 사용 불가능하므로 Gauge를 숨긴다.
- Retry/Quit 후 Gauge는 0 또는 설정된 초기값으로 돌아간다.

## 11. Pause Menu

### 배치

- 전체 반투명 Overlay
- `PAUSED` 제목
- Resume
- Retry
- Quit to Start

기존 Battle HUD는 배경에 유지할 수 있지만 Pause Menu가 입력을 독점한다. Pause 상태에서는 HUD Button을 포함한 하위 입력을 차단한다.

### Resume

- `ResumeRequest` 발행
- 상태가 Battle로 전환된 뒤 Physics 복원
- Pause Menu를 닫고 Battle HUD 입력 복원

### Retry

- 확인 Modal 표시
- 문구 예: 현재 Score와 전투 진행도가 초기화됨을 알림
- 확인: `RestartRequest`
- 취소: Paused 유지

### Quit to Start

- 확인 Modal을 표시하지 않는다.
- `QuitToStartRequest` 발행
- 현재 Round Reset 후 Ready/Start UI로 복귀
- `Application.Quit()`을 호출하지 않는다.

## 12. Retry 확인 Modal

Modal은 Pause의 Retry에만 사용한다.

- Retry 확인: Reset → Ready
- Retry 취소: Modal만 닫고 Paused 유지
- Quit to Start: Modal 없음
- Result Retry: Modal 없음

Modal이 열려 있는 동안 Pause Menu 뒤의 Button 입력을 막는다.

## 13. Finishing UI

### 표시

- Timer `00:00` 고정
- Score 유지 및 기존 전투 결과에 따른 갱신
- 중앙 `TIME UP`
- 필요하면 작은 `Finishing / Slow Motion ×0.5` 보조 문구

### 숨김

- Pause Button
- Special Gauge
- 기타 전투 입력 안내

### 동작

- Timer 0 이전에 시작된 공격 결과가 2.5초 안에 확정되면 Score UI를 갱신한다.
- Finishing 종료 시점 이후 늦은 Score 이벤트를 표시하지 않는다.
- TIME UP은 전투 장면과 적 움직임을 과도하게 가리지 않는다.

## 14. Outro Dialogue UI

- Intro와 같은 두 Portrait, Dialogue Box, Skip 구조를 재사용한다.
- 현재 화자를 밝게, 비화자를 어둡게 처리한다.
- Battle HUD를 완전히 숨긴다.
- Finishing에서 확정한 Score는 Dialogue 중 변경하지 않는다.
- 완료 또는 Skip은 `DialogueCompleteRequest`를 발행한다.
- 다음 상태는 Result다.

## 15. Result UI

### 주요 정보

- Final Score: 가장 큰 시각 우선순위
- Kill Count
- Max Combo
- Player Hit Count
- Special Skill Use Count
- Retry Button

### 동작

- Score와 통계는 고정값이다.
- Result Retry는 확인 없이 `RestartRequest`를 발행한다.
- Reset이 완료되면 Ready/Start UI로 돌아간다.

## 16. 상태별 표시 Matrix

| GameState | Start | Dialogue | Countdown | Pause | Score | Timer | Gauge | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Initializing | OFF | OFF | OFF | OFF | OFF | OFF | OFF | OFF |
| Ready | ON | OFF | OFF | OFF | OFF | OFF | OFF | OFF |
| IntroDialogue | OFF | ON | OFF | OFF | OFF | OFF | OFF | OFF |
| Countdown | OFF | OFF | ON | OFF | OFF | OFF | OFF | OFF |
| Battle | OFF | OFF | OFF | Button | ON | ON | ON | OFF |
| Paused | OFF | OFF | OFF | Menu | 유지 | 유지 | 유지 | OFF |
| Finishing | OFF | OFF | OFF | OFF | ON | 0 고정 | OFF | OFF |
| OutroDialogue | OFF | ON | OFF | OFF | OFF | OFF | OFF | OFF |
| Result | OFF | OFF | OFF | OFF | OFF | OFF | OFF | ON |

Paused에서는 Battle HUD를 배경으로 유지할 수 있지만 Overlay와 Menu가 입력 우선권을 갖는다. Dialogue에서는 HUD를 숨기고 Dialogue UI만 표시한다.

## 17. UI Event 계약

| UI 입력 | Request | 허용 상태 | 결과 |
| --- | --- | --- | --- |
| Start | StartRequest | Ready | IntroDialogue |
| Dialogue 완료/Skip | DialogueCompleteRequest | IntroDialogue | Countdown |
| Countdown 종료 | CountdownCompleteRequest | Countdown | Battle |
| Pause | PauseRequest | Battle | Pause Menu |
| Resume | ResumeRequest | Paused | Battle HUD |
| Pause Retry 확인 | RestartRequest | Paused | Reset → Ready |
| Pause Quit | QuitToStartRequest | Paused | 확인 없이 Reset → Ready |
| Dialogue 완료/Skip | DialogueCompleteRequest | OutroDialogue | Result |
| Result Retry | RestartRequest | Result | 확인 없이 Reset → Ready |

Button Handler에서 Panel을 임의로 열고 닫으며 상태를 우회하지 않는다. Request 처리 후 전달된 `GameStateChangedEvent`를 기준으로 최종 화면을 적용한다.

## 18. UIManager 계약

- `GameStateChangedEvent` 구독과 해제를 대칭적으로 수행한다.
- 각 State의 화면 조합을 한 곳에서 적용한다.
- 동일 State를 다시 적용해도 결과가 달라지지 않게 멱등적으로 작성한다.
- Panel별 내부 Animation은 Panel 책임으로 둘 수 있지만 표시 가능 여부는 GameState 정책을 따른다.
- Reset 시 모든 Coroutine, Tween, Delayed Callback, Selection, Modal 상태를 취소한다.
- 이전 `RoundId`의 UI Event를 무시한다.
- 숨김 상태에서 Raycast Target과 입력 Action이 남지 않게 한다.

## 19. 필수 UI 테스트

1. 초기화 완료 전 Start Button 비활성
2. Start 후 IntroDialogue 표시 및 Battle HUD 숨김
3. Intro/Outro Skip이 일반 완료와 같은 Request 경로 사용
4. Countdown 종료 다음 프레임의 Battle 전환과 동시에 Battle HUD와 전투 활성
5. Pause 중 HUD 입력 차단과 Resume 복귀
6. Pause Retry에만 확인 Modal 표시
7. Retry 취소 후 Paused 상태와 HUD 값 유지
8. Pause Quit은 확인 없이 Start UI 복귀
9. Result Retry는 확인 없이 Start UI 복귀
10. Finishing 중 Score 갱신, Timer 0 고정, Gauge/Pause 숨김
11. Retry/Quit 후 Gauge, Score, Timer, Modal과 Selection 초기화
12. 이전 Round의 Tween, Animation, UI Event가 새 Round에 미노출
13. Dialogue와 Pause Animation이 정지된 gameplay time과 무관하게 동작
14. 16:9 외 지원 화면비의 Anchor와 Safe Area 검증
15. Damage Number, Aim Assist, Lock-on Indicator가 Scene/Prefab에 생성되지 않음

## 20. 관련 문서

- 프로젝트 목표, Combat, Enemy, 성능 비교: `00_PROJECT_OVERVIEW.md`
- GameState, Request Queue, Physics, Finishing, Reset: `01_GAME_STATE_AND_EVENTS.md`
- 전역 구현 및 검증 규칙: `../../AGENTS.md`

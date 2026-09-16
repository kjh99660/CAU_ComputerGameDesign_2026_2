# 프로젝트 개요 및 기술 시연 범위

> 문서 상태: 프로젝트 전역 설계 참고 문서  
> 원본: `game_engine_musou_project_proposal.pdf`  
> 최신 요구사항 반영: `AGENTS.md`, GameState 및 UI 상세 설계와 충돌하는 초기 제안은 최신 문서를 우선한다.

## 1. 문서 목적

이 문서는 프로젝트가 무엇을 만들고, 어떤 게임엔진 기술을 시연하며, 각 기능이 어떤 시스템 경계에 속하는지를 설명한다. 구현자는 작업 범위를 파악할 때 이 문서를 읽고, 상태 전환은 `01_GAME_STATE_AND_EVENTS.md`, 화면 동작은 `02_UI_AND_SCREEN_FLOW.md`를 추가로 확인한다.

## 2. 프로젝트 정의

- 형태: Unity/C# 기반 3D 게임 엔진 기술 데모
- 장르: 3인칭 1 대 다수 무쌍형 전투
- 구성: 단일 Scene, 100초 Score Attack
- 목표: 다수 Enemy 전투에서 엔진 기술을 적용하고 적용 전후의 성능 차이를 수치로 증명
- 우선순위: 콘텐츠 규모보다 안정적인 Core Loop, 결정적인 상태 전환, 다수 객체 성능, 재현 가능한 기술 비교

핵심 플레이는 제한 시간 동안 지속적으로 생성되는 Enemy를 공격해 Score와 Special Gauge를 쌓고, Special Skill로 다수 Enemy를 한 번에 날려 보내는 구조다.

## 3. 최종 사용자 흐름

```text
Initializing
→ Ready / Start
→ IntroDialogue
→ Countdown
→ Battle
→ Finishing
→ OutroDialogue
→ Result
→ Reset
→ Ready
```

- `Battle ↔ Paused`만 양방향 상태 전환이다.
- Retry와 Quit to Start는 Scene을 Reload하지 않고 현재 Round를 Reset한다.
- Pause의 Quit은 애플리케이션 종료가 아니라 Start 화면 복귀다.
- 자세한 전이 규칙은 `01_GAME_STATE_AND_EVENTS.md`를 따른다.

## 4. 확정 게임 규칙

| 항목 | 규칙 |
| --- | --- |
| Game Duration | 100초 |
| Player HP | 사용하지 않음 |
| Enemy HP | 유효한 공격 한 번에 사망 |
| 목표 | 제한 시간 동안 최대 Score 획득 |
| Player 피격 | 현재 Score의 10% 감소, 0 미만 금지 |
| 피격 무적 | 기본 0.8초, 설정 가능 |
| Combo | Normal Attack 1 → 2 → 3 |
| Combo Input Window | 기본 0.4초, 설정 가능 |
| Gauge 획득 | 공격 적중 또는 Player 피격 |
| Special Skill | Gauge가 가득 찼을 때 사용 가능 |

다음 요소는 구현하지 않는다.

- Player HP와 HP Bar
- Enemy HP Bar
- Damage Number
- Aim Assist
- Lock-on 기능 및 Lock-on Indicator

Score 증가량과 Gauge 증감량은 초기 제안서의 수치를 그대로 하드코딩하지 않는다. 프로젝트 설정 데이터에 노출하고, 별도 확정이 없다면 초기 제안값을 `provisional` 기본값으로만 사용할 수 있다.

| 설정 | 초기 제안값 | 상태 |
| --- | ---: | --- |
| Normal Hit Score | +10 | 임시 기본값 |
| Enemy Kill Score | +100 | 임시 기본값 |
| Normal Hit Gauge | +3 / Enemy | 임시 기본값 |
| Enemy Kill Gauge | +5 | 임시 기본값 |
| Player Damaged Gauge | +8 | 임시 기본값 |
| Gauge Maximum | 100 | 임시 기본값 |

## 5. Player와 전투

### 5.1 입력과 행동

- WASD: Camera Forward/Right를 지면에 투영한 방향 기준 이동
- Mouse: 3인칭 Shoulder Camera 회전
- Normal Attack: 1 → 2 → 3 Combo
- Charge Attack
- 방향 회피
- 누르고 있는 동안 Guard
- Jump
- Special Skill

실제 키 바인딩은 기존 Input System 설정을 우선하며 문서만 보고 새 키를 임의 배정하지 않는다.

### 5.2 Combo 계약

- 각 공격의 Combo Input Window에서 다음 입력을 Buffer한다.
- 기본 Window는 0.4초이고 설정 데이터로 조절한다.
- 입력 시간 초과, 마지막 Combo 종료, 피격, Special Skill, 상태 전환 시 Combo를 초기화한다.
- 공격 시작 시 해당 Attack의 `HitTargets`를 비워 같은 공격에서 동일 Enemy를 중복 판정하지 않는다.
- 공격 판정은 Animation Active Frame에서만 활성화되는 Hitbox 또는 동등한 데이터 기반 Window를 사용한다.

### 5.3 피격과 피드백

Player 피격 시 다음을 함께 처리한다.

1. 현재 Score의 10% 감소
2. Special Gauge 증가
3. Hit Reaction
4. 화면 가장자리 Red Flash/Vignette
5. 기본 0.8초 Invincibility

Damage Number 대신 Animation, Hit Stop, VFX, Sound, 약한 Camera 반응, Score/Gauge UI Animation으로 결과를 전달한다.

### 5.4 Special Skill

- Player 중심의 넓은 원형 범위를 한 번 조회한다.
- 범위 내 Enemy를 각각 한 번만 판정한다.
- 일반 공격보다 큰 Rigidbody Impulse를 적용한다.
- 다수 Enemy의 Knockback이 보이도록 짧은 Camera Zoom Out을 사용할 수 있다.
- Skill 자체의 Slow Motion은 선택 기능이며, GameState의 Finishing Slow Motion과 소유권을 섞지 않는다.

## 6. Enemy와 Spawn

### 6.1 Enemy 상태

최소 상태 흐름은 다음과 같다.

```text
Spawn / Idle → Chase → Attack → Hit / Knockback → Death → Pool Return
```

- Ready에서는 Enemy가 실제 Arena에 Spawn되어 보이지만 Idle이다.
- Battle에서만 새 AI Decision, 이동과 공격을 시작한다.
- Enemy Attack은 Animation 후반부의 Active Frame에 판정해 시각적 예고를 제공한다.
- Enemy는 유효한 Player 공격 한 번에 사망하지만, Hit/Knockback/Ground Collision/Death 연출은 마무리할 수 있다.

### 6.2 Spawn 설정

| Parameter | 초기 제안값 | 규칙 |
| --- | ---: | --- |
| Spawn Interval | 1.0초 | 설정 가능 |
| Count Per Wave | 5 | 설정 가능 |
| Minimum Distance | 15m | Player 근접 생성 방지 |
| Maximum Distance | 25m | Arena 크기에 따라 조절 |
| Maximum Enemy Count | 일반 플레이 100 | 성능 실험에서는 별도 설정으로 200/300까지 허용 |

Spawn 위치는 Player 주변 Random Angle과 Min/Max Distance로 계산한다. 가능하면 Camera 안의 매우 가까운 위치를 피한다. 값은 Balance 및 Stress Test용 설정으로 노출한다. 일반 플레이 상한 100과 실험용 200/300 설정을 구분한다.

## 7. Spatial Partitioning과 Separation

### 7.1 역할 구분

- Collider: 실제 충돌, Hit, Ground Collision, Knockback 처리
- Separation: AI 이동 벡터를 조정해 Enemy 군집, 떨림, 밀림 완화
- Spatial Partitioning: 주변 객체 후보 검색 비용을 줄이는 최적화 계층

Collider가 있다고 Separation이 자동으로 해결되는 것은 아니다. Enemy가 모두 Player의 동일 위치를 목표로 삼으면 물리 충돌만으로는 떨림과 과도한 밀집이 발생한다.

### 7.2 Uniform Grid 활용

World를 Uniform Grid로 나누고 현재 Cell 및 인접 Cell만 조회한다.

| 기능 | Grid Query |
| --- | --- |
| Enemy Separation | 현재 및 인접 Cell의 Enemy만 거리 검사 |
| Player Attack | 공격 범위가 걸치는 Cell에서 후보 수집 후 정확 판정 |
| Special Skill | Skill Radius와 겹치는 Cell만 조회 |
| Debug | 전체 Enemy 수와 Candidate 수 비교 |

모든 Enemy 쌍을 매 프레임 검사하는 O(N²) 구현은 금지한다.

## 8. Physics, Particle, Rendering

### 8.1 Rigidbody Knockback

- 공격 방향을 기준으로 Impulse를 적용한다.
- Normal Attack과 Special Skill의 Force는 별도 설정으로 둔다.
- Ground Collision 후 Death 상태로 전환하고 연출 완료 후 Pool로 반환한다.
- Pause/Dialogue 정지와 복원은 `PhysicsPauseManager`가 담당한다.
- Finishing에서는 Timer 전에 시작된 Knockback과 Collision을 마무리한다.

### 8.2 Particle

| 상황 | Effect |
| --- | --- |
| 일반 공격 적중 | Hit Spark |
| Enemy 착지 | Ground Dust |
| Special Skill | Circular Slash / Shockwave |

반복 Effect는 Pooling하고 State 또는 Round가 바뀐 뒤 이전 Callback이 실행되지 않게 한다.

### 8.3 Rendering

- Directional Light와 기본 Shadow
- Texture, Normal Map 등 Material 요소
- 동일 Enemy Mesh의 GPU Instancing 검토
- Camera Frustum Culling

개별 Animator 구조가 Instancing과 충돌하면 먼저 실제 렌더링 구조를 측정하고 비교 가능한 범위로 목표를 조정한다.

## 9. Technology Comparison Manager

같은 Enemy 수와 전투 조건에서 최적화 기술의 적용 전후를 비교한다.

| 기술 | OFF | ON | 주요 지표 |
| --- | --- | --- | --- |
| Object Pooling | Instantiate / Destroy | Pool Get / Release | FPS, GC Alloc, Frame Time |
| Frustum Culling | 가능한 모든 객체 렌더링 | Camera 밖 객체 제외 | Visible Count, Draw Calls, FPS |
| GPU Instancing | 개별 Draw | 동일 Mesh/Material Instance 처리 | Draw Calls, CPU Render Time |
| Spatial Partitioning | 전체 Enemy 후보 검사 | 주변 Cell 후보만 검사 | Candidate Count, CPU Time, FPS |

Debug/Performance UI는 최소 다음 정보를 제공한다.

- 기술별 ON/OFF 상태
- Active Enemy Count
- Visible Enemy Count
- Candidate Count
- FPS
- Frame Time
- Draw Calls
- 선택 항목: GC Alloc, CPU/GPU Time

ON/OFF 비교가 실제로 같은 조건인지 확인할 수 있도록 Enemy 수와 Spawn 조건을 고정하거나 동일 Seed/시나리오를 사용한다.

## 10. 설정 관리

아래 값은 Inspector, ScriptableObject 또는 프로젝트의 기존 설정 시스템에 둔다.

- Game Duration
- Invincibility Duration
- Combo Input Window
- Skill Radius
- Normal/Skill Knockback Force
- Score와 Gauge 규칙
- Enemy Attack Range/Interval/Move Speed
- Separation Distance/Weight
- Spawn Interval/Count/Distance/Maximum Count
- 기술 비교 Toggle 및 Stress Test Enemy Count

Runtime 코드의 Magic Number로 흩어놓지 않는다.

## 11. 구현 우선순위

1. 상태 머신과 `Ready → Result → Reset` Core Loop
2. Player 이동, Camera, Combo, Animation Hitbox
3. Enemy Spawn, Chase, Attack, Hit, Death
4. Score, Gauge, 피격 무적, Special Skill
5. Rigidbody Knockback과 Particle
6. Object Pooling
7. Spatial Partitioning과 Separation
8. Culling과 Instancing
9. Technology Comparison과 성능 측정 UI
10. 연출, Parameter 튜닝, Stress Test

각 단계는 상태 전환 및 Retry 검증을 포함한 수직 슬라이스로 완료한다.

## 12. 완료 기준

- Start부터 Result, Retry 후 다시 Start까지 반복 가능하다.
- Combo와 Enemy Attack의 Hit Timing이 Animation과 일치한다.
- Player Invincibility가 동시 연속 피격을 제한한다.
- Enemy가 Pooling을 통해 반복 생성/회수된다.
- Spatial Partitioning이 Attack/Separation 후보 수를 제한한다.
- Special Skill의 다수 Knockback, Particle, Camera 연출이 동작한다.
- 최적화 기술을 ON/OFF하고 동일 조건의 성능 지표를 비교할 수 있다.
- 최적화 전 구성에서도 최소 20 FPS를 하한 검증 기준으로 삼고 60 FPS를 목표로 한다. 측정 플랫폼과 하드웨어는 확정 후 기록한다.
- Retry/Quit 후 이전 Round의 객체, 이벤트, UI, 물리 상태가 남지 않는다.

## 13. 관련 문서

- 상태, Event Queue, Physics Pause, Finishing, Reset: `01_GAME_STATE_AND_EVENTS.md`
- 화면 흐름, Canvas, UI 요청, UI 테스트: `02_UI_AND_SCREEN_FLOW.md`
- 저장소 전역 구현 규칙과 문서 우선순위: `../../AGENTS.md`

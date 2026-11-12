# REBOOT 통신 허브 광장 레벨 디자인

작성일 2026-11-11. 작업 브랜치 `level-design/reboot-hub`, 기준 커밋 `origin/JH_Work` `5e4c0d0` (2026-11-05, 커밋·푸시 안 함).

> **적용 위치: 미리보기 씬 `Assets/Scenes/Main_RebootHub.unity`.** 팀 공용 `Main.unity`는 `JH_Work`와 동일하게 그대로 둔다(빌드 설정도 변경 없음). 미리보기 씬은 `Main.unity`를 파일 그대로 복사한 뒤 빌더를 적용한 것이라, 기존 오브젝트 3,460개의 내부 ID가 Main과 같고 차이는 이 문서 3절의 변경뿐이다. 채택이 결정되면 9절 절차로 최신 `Main.unity`에 다시 적용한다.

## 1. 기준 브랜치

| 브랜치 | 원격 tip | 관계 |
| --- | --- | --- |
| `JH_Work` | `5e4c0d0` | `WH_AttackHit`, `WH_Work`, `leehankil-branch`를 모두 포함. 로컬 `origin/JH_Work`(`32dcea4`)는 오래된 상태였음 |
| `main` | `b5956b7` | `JH_Work`와 공통 조상 없음. 9월 이후 갱신 없음 |
| `WH_AttackHit` | `1c47780` | `JH_Work`에 병합됨 |

`JH_Work`를 통합 기준으로 판단했다. 팀이 `main`으로 언제, 어떻게 합칠지는 아직 정해지지 않았다.

## 2. 디자인 의도

- **공간 정체성**: 광장 북쪽 통로 위에 걸친 높이 약 45m의 통신 허브 탑(`P_Reboot_CommHub`)을 세웠다. 시작 카메라(북쪽 방향)의 화면 상단에 `COMM HUB 07 / EMERGENCY REBOOT // HOLD 100s` 간판이 들어온다. 마우스로 올려다보면 데이터 링, 상태 화면, 접시 안테나가 보인다.
- **전투 동선**: 기존 팔각 광장(지름 약 40m)을 그대로 쓴다. 바닥에는 충돌체 없는 평면 가이드(시작 링, r=10m 호 4개)만 두었다. 적이 들어오는 축인 북쪽 통로와 남쪽 계단에는 붉은 쉐브론을 깔았다. 광장 안에 서 있는 장식은 추가하지 않았다.
- **명도 대비**: 흰 바닥(명도 약 0.9)을 청회색 중간 명도로 낮췄다. 플레이어(검은 옷)와 적(어두운 금속, 런타임에 붉은 틴트)이 실루엣으로 읽힌다. 발광은 청록(허브·가이드), 마젠타(보조 조명·광고), 빨강(보안 경고)으로만 쓴다.
- **분위기**: 야간 절차 하늘, 삼색 Ambient, 보라색 지수 안개(먼 도시의 디테일을 눌러 전투 공간에 시선이 모이게 함)를 적용했다. Bloom을 조금 강하게 하고, 실시간 포인트·스팟 조명 7개에는 그림자를 쓰지 않는다.
- **성능**: 추가 렌더러 108개, 메시 삼각형 3,970개, 고유 머티리얼 11개, 충돌체 0개다. 모두 Static Batching 대상이다.

## 3. 변경 내용

### Scene (`Assets/Scenes/Main_RebootHub.unity`, 미리보기)

1. **높이 정렬**: `Map`, `BackGround`, `Player` 루트를 +2m 올려 광장 바닥을 y=0으로 맞췄다. 기존 바닥 y=-2와 `EnemySettings.spawnHeight=0`이 어긋나 적이 2m 떠서 생성되던 문제를 고쳤다. 오브젝트 삭제는 0개다(3460 → 3561, 추가 101개).
2. **머티리얼 교체**: `Map` 아래 렌더러 733개의 `Mat_BasicMaterial_00`을 Scene 오버라이드로 `M_Reboot_KitNight`로 바꿨다. 셰이더는 같고 팔레트만 야간용이다. 중앙 기둥 큐브 2개의 기본 `Lit`도 `M_Reboot_DarkMetal`로 바꿨다. 원본 Prefab과 머티리얼 에셋은 수정하지 않았다.
3. **`Env_RebootHub` 루트 추가**: Landmark, Signage, FloorGuides, PerimeterBeacons(외곽 기둥 22개 위), Lighting(7개), DesignReference_NotUsedByRuntime(스폰 보정 후보용 기준점, 런타임 미사용).
4. **RenderSettings/Volume**: 하늘을 `M_Reboot_NightSky`로 바꾸고 Trilight Ambient, ExponentialSquared 안개(0.011)를 적용했다. Directional Light는 색과 세기만 바꿨다. Global Volume 프로필은 기존 프로필을 복제한 `VP_Reboot_Night`로 교체했다.

### 새 에셋 (`Assets/LevelDesign/RebootHub/`, 모두 Editor 스크립트로 생성)

- Prefabs: `P_Reboot_CommHub`, `P_Reboot_SecuritySign`, `P_Reboot_HoloBillboard`, `P_Reboot_ColumnBeacon`, `P_Reboot_FloorStartMark`, `P_Reboot_FloorChevrons`
- Meshes: 허브 다리·데크·코어·링·접시 등 절차 생성 Low-poly 메시 17개(`SM_Reboot_*`)
- Textures: `T_Reboot_KitPalette_Night`(기존 128×32 팔레트 재채색), `T_Reboot_HubScreen`, `T_Reboot_Hazard`, `T_Reboot_HoloPanel`
- Materials 12개(`M_Reboot_*`), Volume Profile `VP_Reboot_Night`

### 도구와 테스트

- `Assets/Editor/LevelDesign/RebootHubEnvironmentBuilder.cs`: 에셋은 같은 경로를 덮어써 GUID를 유지하고, 다시 실행하면 `Env_RebootHub`만 다시 만든다. 높이 정렬은 바닥이 이미 0이면 건너뛴다.
  - `Tools/REBOOT Level/Preview: Rebuild Main_RebootHub From Main`: 최신 `Main.unity`를 미리보기로 복사한 뒤 적용한다(미리보기의 GUID는 유지).
  - `Tools/REBOOT Level/Preview: Re-apply Environment Only`: 미리보기 씬에 환경만 다시 적용한다.
  - `Tools/REBOOT Level/Adopt: Apply To Main.unity`: 팀 합의 후 공용 `Main.unity`에 적용한다(확인창 있음).
- `Assets/Editor/LevelDesign/RebootLevelDiagnostics.cs`: 메뉴 `Tools/REBOOT Level/Run Diagnostics (no save)`. 기본 대상은 미리보기 씬이고 배치 인자 `-rebootScene main`으로 `Main.unity`를 볼 수 있다. Scene을 저장하지 않고 계층 요약, 스폰 위치 분석, 보정 후보 시뮬레이션, 고정 시점 캡처를 만든다.
- `Assets/Tests/PlayMode/RebootLevelSceneTests.cs`: 미리보기 씬을 Editor 전용 로더(`LoadSceneAsyncInPlayMode`)로 열어 바닥과 스폰 높이 일치, 장식의 충돌체·이동 공간 침범 여부, Battle에서 적의 접근, 동쪽 벽 충돌, Pause·Resume·Retry 후 배치 복원을 검사한다.

## 4. 에셋 출처

| 에셋 | 사용 | 출처·권한 |
| --- | --- | --- |
| `LowPolySciFiCity` (배경 도시) | 기존 배치 유지 | **미확인**. 저장소에 라이선스·출처 문서 없음 |
| `SciFi_Modular_Kit_Demo` (광장 바닥·벽·기둥) | 형태 유지, 팔레트 변형 머티리얼 적용 | **미확인**. 라이선스 문서 없음 |
| `MeshyImports` (플레이어·적 모델) | 수정 없음 | **미확인**. 파일명상 Meshy AI 생성물이며 사용 조건은 생성 계정의 요금제에 따름 |
| LiberationSans SDF (TextMesh Pro 기본) | 간판 글자 | TMP Essential Resources 포함 폰트(SIL OFL) |
| NanumGothic | 사용 안 함 | 저장소에 OFL 파일 포함 |
| Unity 내장 Cube/Quad/Sphere 메시 | 간판 틀, 경고등 | Unity 내장 |
| `Assets/LevelDesign/RebootHub/*` | 신규 | 이번 작업에서 스크립트로 직접 생성. 외부 자료 없음 |

## 5. 검증

| 항목 | 방법 | 결과 |
| --- | --- | --- |
| 컴파일 | Unity 6000.3.24f1 batchmode | 오류 0, 신규 스크립트 경고 0 |
| EditMode 테스트 | `-runTests -testPlatform EditMode` | 42/42 통과 |
| PlayMode 테스트 | `-runTests -testPlatform PlayMode` | 10/10 통과(기존 8개는 원래 `Main.unity`, 신규 2개는 미리보기 씬) |
| 시작 흐름, Dialogue Skip, Pause/Resume/Retry, Finishing timeScale 복구 | 기존 `CombatRoundSceneTests`, `PlayerSceneTests` | 통과 |
| 적 스폰·접근 | 신규 PlayMode 테스트와 `runtime_enemy_summary.txt` | 높이 정렬 후 모든 적이 y=0에서 플레이어에게 접근함. **광장 밖 생성은 남아 있음(6절)** |
| 화면 | 편집 시점 캡처, Play Mode 런타임 카메라 캡처 | `Screenshots/` 참고 |

스폰 분석(`diagnostics_before.md` → `diagnostics_after.md`, 플레이어가 갈 수 있는 모든 칸 기준):

| | 변경 전 | 변경 후 |
| --- | --- | --- |
| 생성 지점이 스폰 높이의 바닥 위 | 5.0% | 48.1% |
| 다른 높이(공중 또는 아래 도시) | 94.9% | 51.8% |

**FPS는 측정하지 않았다.** batchmode는 실제 Game View 프레임을 대표하지 않는다. 위의 렌더러·삼각형·조명 수는 정적 수치일 뿐 FPS가 아니다.

## 6. 남은 문제와 해결 후보 (디자인 범위 밖, 코드 미수정)

1. **광장 밖 공중 스폰** (`EnemyRuntime.SpawnOne`): 플레이어 기준 15~25m 원 위의 임의 지점에 생성하지만, 광장 반경은 약 20m이고 그 밖은 약 20m 아래 도시다. 변경 후에도 생성 지점의 약 52%가 광장 밖 공중이다. 런타임에서도 Retry 직후 초기 5명 중 2명이 광장 밖이었다. 적은 kinematic 상태로 Transform을 직접 옮기므로 벽을 통과해 들어온다.
   - 후보 A(권장): 같은 15~25m 분포에서 아래로 Raycast해 스폰 높이의 바닥이 있고 Collider와 겹치지 않는 지점만 채택한다. 시뮬레이션 결과 1회 시도 45.1%, 4회 90.2%, 10회 99.5%가 유효하다. 실패하면 `EntryGate_North`/`EntryGate_South` 기준점으로 보낸다.
   - 후보 B: `EnemySettings`의 거리를 줄인다(데이터만 변경). 플레이어가 벽 근처에 있으면 여전히 밖에 생긴다.
   - 후보 C: NavMesh(`com.unity.ai.navigation` 설치됨)의 `SamplePosition`으로 스폰과 추적을 함께 처리한다. 범위가 크다.
2. **카메라 충돌 없음** (`PlayerRuntime.UpdateCamera`): 벽이나 난간 가까이에서 카메라가 구조물 안으로 들어가 플레이어를 가린다(`11_runtime_east_wall_camera_clip.jpg`). 후보: 목표 지점에서 카메라 쪽으로 SphereCast해 거리를 줄인다.
3. **플레이어 중력 없음**: `CharacterController.Move`에 수직 이동이 없어 계단을 오르면 내려오지 못하고, 높이가 다른 층을 쓸 수 없다. 그래서 상층과 복층은 배경·연출로만 쓰고 전투는 1층 광장으로 한정했다.
4. **`Area_B`가 비어 있음**: 북쪽 통로는 빈 `Area_B`로 이어진다. 플레이어가 통로로 광장을 벗어날 수 있다. 막을지(보안 차단막), Area_B를 만들지 정해야 한다.
5. **중앙 안테나 기둥 2개**: 적은 회피 로직이 없어 기둥을 통과한다. 기존 팀원 배치라 유지했다.
6. **적 생성 위치가 풀 원점으로 되돌아가는 경쟁 상태** (`EnemyAgent.Activate`, `EnemyPrototype.prefab`)
   - 원인: Rigidbody가 kinematic + Interpolate이고 `autoSyncTransforms`가 꺼져 있다. 그런데 스크립트는 Transform만 옮긴다.
   - 증상: 고정 물리 스텝 전에 보간이 Transform을 이전 위치로 덮어써 초기 적이 `(0,0,0)`에 겹친다. 이동 속도도 설정값의 약 1/7로 떨어진다.
   - 테스트 영향: 레벨 PlayMode 테스트는 이 상황을 감지하면 판정 불가(Inconclusive)로 남긴다.
   - 후보: `Activate`에서 `_body.position` 함께 설정, 보간 끄기, 또는 `MovePosition` 사용. 자세한 내용은 `Docs/UiTheme/README.md` 마지막 절.

## 7. 팀 검토 요청

- Main Scene 루트를 +2m 옮긴 방식(대안: `EnemySettings.spawnHeight=-2`)에 동의하는지.
- 야간 톤과 명도. 실제 모니터에서 적과 플레이어가 충분히 구분되는지.
- 6절 후보 A를 적 담당자가 구현할지.
- `LowPolySciFiCity`, `SciFi_Modular_Kit_Demo`, Meshy 모델의 출처와 사용 권한(제출 전 확인 필요).
- 미리보기 씬은 복사 시점의 사본이다. 팀원이 `Main.unity`를 고치거나 팀 Editor 도구(`GameDemo*PrefabBuilder`)가 Main에 쓰면 미리보기에는 반영되지 않으므로, 미리보기에서는 게임 로직을 고치지 말고 오래되면 `Preview: Rebuild` 메뉴로 다시 만든다.

## 8. 재현 명령

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
$UNITY -batchmode -projectPath . -executeMethod RebootHubEnvironmentBuilder.BuildBatch -rebootRefreshPreview -logFile build.log
$UNITY -batchmode -projectPath . -executeMethod RebootLevelDiagnostics.RunAll -rebootOut ./diag -rebootTag check -logFile diag.log
REBOOT_CAPTURE_DIR=./runtime $UNITY -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

Unity를 실행하면 `ProjectSettings/TimeManager.asset`과 TMP `LiberationSans SDF.asset`이 형식 업그레이드로 다시 저장될 수 있다. 내용 변경이 아니므로 이번 작업에서는 되돌렸다.

Editor에서 수동으로 확인하는 절차: `Main_RebootHub.unity`를 열고 Play를 누른다. Start → Skip → 카운트다운 뒤 WASD로 광장 가장자리까지 이동한다. 마우스로 허브를 올려다보고, 남쪽 계단과 동·서 벽에서 카메라가 벽을 뚫는지 본다. ESC로 Pause → Resume, Pause → Retry → 확인 후 배치가 처음과 같은지 확인한다. Stats 창이나 Profiler로 FPS를 기록한다.

## 9. 채택 절차 (나중에 Main.unity로 합칠 때)

씬 파일끼리 병합하지 않는다. 미리보기의 변경은 모두 빌더가 다시 만들 수 있으므로 최신 Main에 다시 적용한다.

1. 팀원의 최신 `Main.unity` 변경을 먼저 받는다(`JH_Work` 등).
2. Editor에서 `Tools/REBOOT Level/Adopt: Apply To Main.unity` 실행, 또는 `BuildBatch -rebootScene main`.
3. EditMode/PlayMode 테스트를 실행한다. `RebootLevelSceneTests`의 `ScenePath`를 `Main.unity`로 바꾸고 일반 `SceneManager` 로더로 되돌린다.
4. 확인 후 `Main_RebootHub.unity`와 `.meta`를 삭제한다.

추가 오브젝트(`Env_RebootHub`)만 따로 옮기고 싶다면 `EditorSceneManager.MergeScenes`나 다중 씬 편집으로 옮길 수 있지만, 높이 정렬·머티리얼 교체·하늘/안개 설정은 기존 오브젝트와 씬 설정의 수정이라 옮겨지지 않는다.

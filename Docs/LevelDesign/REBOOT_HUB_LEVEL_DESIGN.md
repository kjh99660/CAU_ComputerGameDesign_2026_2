# REBOOT 통신 허브 광장 레벨 디자인

작성일 2026-11-11, 갱신 2026-11-13. 작업 브랜치 `level-design/reboot-hub`(푸시 안 함). 기준 맵은 `origin/JH_Work` `8e88d9e`를 이 브랜치에 병합(`766b5dc`)한 `Main.unity`다.

> **적용 위치: 미리보기 씬 `Assets/Scenes/Main_RebootHub.unity`.** 팀 공용 `Main.unity`는 `JH_Work`와 동일하게 그대로 둔다(빌드 설정도 변경 없음). 미리보기 씬은 `Main.unity`를 파일 그대로 복사한 뒤 빌더를 적용한 것이다. 그래서 기존 오브젝트의 내부 ID가 Main과 같고, Main에서 지우거나 옮긴 오브젝트는 0개이며, 차이는 이 문서 3절의 변경뿐이다. 채택이 결정되면 9절 절차로 최신 `Main.unity`에 다시 적용한다.

## 1. 기준 브랜치

| 브랜치 | 원격 tip | 관계 |
| --- | --- | --- |
| `JH_Work` | `8e88d9e` (2026-11-12) | 이 브랜치에 병합함. `WH_AttackHit`, `WH_Work`, `leehankil-branch`를 모두 포함. 이전 기준 `5e4c0d0` 이후 맵 수정(`96dd8bc`), BGM/VFX(`d64e10e`), 적 스폰·애니메이션 수정(`8e88d9e`)이 추가됨 |
| `main` | `b5956b7` | `JH_Work`와 공통 조상 없음. 9월 이후 갱신 없음 |

팀이 `main`으로 언제, 어떻게 합칠지는 아직 정해지지 않았다.

### 병합으로 바뀐 맵(`96dd8bc`)

- 맵 루트가 `Map`/`BackGround`에서 `MainMap`(광장, 위치는 이전 `Map`과 같음), `CenterMap`(광장 둘레 건물·펜스 297개), `BackGroundMap`(원경 건물 40개), 지하 바닥 `Cube`로 바뀌었다.
- 광장 바닥 높이(y=-2), 광장 중심 (-24, 48.2), 플레이어 시작점 (-24, -2, 42)은 그대로다.
- 북쪽 통로(빈 `Area_B`로 가던 길)가 상부 데크로 오르는 넓은 계단으로 바뀌었다. 남쪽 계단은 그대로이고, 그 양옆 경비탑 2개와 광장 둘레 건물·상점·소품이 추가됐다(이전에는 광장 밖이 약 20m 아래 도시였다).
- 중앙 안테나 기둥 2개에 BoxCollider 큐브가 붙었다.

## 2. 디자인 의도

- **공간 정체성**: 광장 북쪽 계단 너머 상부 데크에 높이 약 45m의 통신 허브 탑(`P_Reboot_CommHub`)을 세웠다. 시작 카메라(북쪽 방향)의 화면 상단에 `COMM HUB 07 / EMERGENCY REBOOT // HOLD 100s` 간판이 들어오고, 계단이 탑으로 시선을 이끈다. 마우스로 올려다보면 데이터 링, 상태 화면, 접시 안테나가 보인다.
- **전투 동선**: 팔각 광장(지름 약 40m)을 그대로 쓴다. 바닥에는 충돌체 없는 평면 가이드(시작 링, r=10m 호 4개)만 두었다. 적이 들어오는 축인 북쪽·남쪽 계단 첫 단 앞에는 광장 쪽을 가리키는 붉은 쉐브론을 깔았다. 광장 안에 서 있는 장식은 추가하지 않았다.
- **명도 대비**: 흰 바닥(명도 약 0.9)을 청회색 중간 명도로 낮췄다. 플레이어(검은 옷)와 적(어두운 금속, 런타임에 붉은 틴트)이 실루엣으로 읽힌다. 발광은 청록(허브·가이드), 마젠타(보조 조명·광고), 빨강(보안 경고)으로만 쓴다.
- **분위기**: 야간 절차 하늘, 삼색 Ambient, 보라색 지수 안개(먼 도시의 디테일을 눌러 전투 공간에 시선이 모이게 함)를 적용했다. Bloom을 조금 강하게 하고, 실시간 포인트·스팟 조명 7개에는 그림자를 쓰지 않는다.
- **성능**: 추가 렌더러 100개(그림자 투사 21개), 메시 삼각형 3,874개(TMP 글자 제외), 고유 머티리얼 11개, 충돌체 0개다. 조명을 뺀 모두가 Static Batching 대상이다.

## 3. 변경 내용

### Scene (`Assets/Scenes/Main_RebootHub.unity`, 미리보기)

1. **맵·플레이어 위치는 그대로**: 이전 버전은 광장 바닥을 적 스폰 높이(y=0)에 맞추려고 맵을 +2m 올렸다. `8e88d9e`에서 적 스폰 높이가 "라운드 시작 플레이어 높이 + `SpawnHeight`"로 바뀌어 더는 필요 없으므로 없앴다. 장식은 빌더가 잰 광장 바닥(y=-2) 기준으로 놓는다.
2. **머티리얼 교체**: `MainMap` 아래 렌더러 564개를 Scene 오버라이드로 바꿨다. `Mat_BasicMaterial_00`은 `M_Reboot_KitNight`로(셰이더는 같고 팔레트만 야간용), 중앙 기둥 큐브 2개의 기본 `Lit`은 `M_Reboot_DarkMetal`로 바꿨다. 원본 Prefab과 머티리얼 에셋은 수정하지 않았다. `CenterMap`, `BackGroundMap`, 지하 바닥 `Cube`는 건드리지 않았다.
3. **`Env_RebootHub` 루트 추가**: Landmark, Signage, FloorGuides, PerimeterBeacons(외곽 기둥 18개 위), Lighting(7개), DesignReference_NotUsedByRuntime(스폰 보정 후보용 기준점, 런타임 미사용).
   - 빌더가 맵을 재서 놓는 것: 계단 첫 단(남 z=27.95, 북 z=62.7) 앞의 쉐브론·경고등·출입구 기준점, 남쪽 계단 양옆 벽면(z=30.3)에 붙는 보안 표지판.
   - 서쪽 홀로그램 광고판은 새 건물과 겹치고 시작 카메라에서 71%만 보여 서쪽으로 옮겼다(지금은 100% 보임, 겹침 없음).
4. **RenderSettings/Volume**: 하늘을 `M_Reboot_NightSky`로 바꾸고 Trilight Ambient, ExponentialSquared 안개(0.011)를 적용했다. Directional Light는 색과 세기만 바꿨다. Global Volume 프로필은 기존 프로필을 복제한 `VP_Reboot_Night`로 교체했다.

### 새 에셋 (`Assets/LevelDesign/RebootHub/`, 모두 Editor 스크립트로 생성)

- Prefabs: `P_Reboot_CommHub`, `P_Reboot_SecuritySign`, `P_Reboot_HoloBillboard`, `P_Reboot_ColumnBeacon`, `P_Reboot_FloorStartMark`, `P_Reboot_FloorChevrons`
- Meshes: 허브 다리·데크·코어·링·접시 등 절차 생성 Low-poly 메시 17개(`SM_Reboot_*`)
- Textures: `T_Reboot_KitPalette_Night`(기존 128×32 팔레트 재채색), `T_Reboot_HubScreen`, `T_Reboot_Hazard`, `T_Reboot_HoloPanel`
- Materials 12개(`M_Reboot_*`), Volume Profile `VP_Reboot_Night`

병합 후 에셋 내용은 바뀌지 않았다(Scene만 다시 적용).

### 도구와 테스트

- `Assets/Editor/LevelDesign/RebootHubEnvironmentBuilder.cs`: 에셋은 같은 경로를 덮어써 GUID를 유지하고, 다시 실행하면 `Env_RebootHub`만 다시 만든다. 맵 루트는 `MainMap`(없으면 이전 이름 `Map`)을 찾는다. 맵을 옮기지 않고, 광장 바닥 높이·계단 첫 단·벽면을 Raycast로 재서 장식을 놓는다. 못 찾으면 이전 맵 값으로 두고 경고를 남긴다.
  - `Tools/REBOOT Level/Preview: Rebuild Main_RebootHub From Main`: 최신 `Main.unity`를 미리보기로 복사한 뒤 적용한다(미리보기의 GUID는 유지).
  - `Tools/REBOOT Level/Preview: Re-apply Environment Only`: 미리보기 씬에 환경만 다시 적용한다.
  - `Tools/REBOOT Level/Adopt: Apply To Main.unity`: 팀 합의 후 공용 `Main.unity`에 적용한다(확인창 있음).
  - 배치 인자 `-rebootSkipAssets`: 에셋을 다시 만들지 않고 Scene에만 적용한다. 허브 프리팹과 볼륨 프로필은 다시 만들 때마다 내용이 같아도 내부 ID가 바뀌므로, 에셋 코드를 고치지 않았다면 이 인자로 불필요한 diff를 막는다.
- `Assets/Editor/LevelDesign/RebootLevelDiagnostics.cs`: 메뉴 `Tools/REBOOT Level/Run Diagnostics (no save)`. 기본 대상은 미리보기 씬이고 배치 인자 `-rebootScene main`으로 `Main.unity`를 볼 수 있다. Scene을 저장하지 않고 다음을 만든다.
  - 계층 요약, 스폰 위치 분석(스폰 평면 = 시작 높이 + `SpawnHeight`), 보정 후보 시뮬레이션
  - Signage check: 보안 표지판 뒤에 벽이 있는지, 시작 카메라에서 광고판이 몇 % 보이는지, 광고판 틀이 건물과 겹치는지
  - 고정 시점 캡처(광고판 쪽을 올려다본 구도 포함)
- `Assets/Tests/PlayMode/RebootLevelSceneTests.cs`: 미리보기 씬을 Editor 전용 로더(`LoadSceneAsyncInPlayMode`)로 연다.
  - 플레이어가 광장 바닥 위에서 시작하고, 적이 "시작 높이 + `SpawnHeight`" 평면에 생성되는지 본다.
  - 장식이 충돌체를 더하거나 광장 이동 공간을 막지 않는지, 보안 표지판이 벽에 붙어 있는지(묻히거나 뜨지 않는지) 본다.
  - Battle에서 적의 접근, 동쪽 벽 충돌, Pause·Resume·Retry 후 배치 복원을 검사한다.

## 4. 에셋 출처

| 에셋 | 사용 | 출처·권한 |
| --- | --- | --- |
| `LowPolySciFiCity` (배경 도시, 팀이 배치한 광장 둘레 건물·소품) | 기존 배치 유지 | **미확인**. 저장소에 라이선스·출처 문서 없음 |
| `SciFi_Modular_Kit_Demo` (광장 바닥·벽·기둥) | 형태 유지, 팔레트 변형 머티리얼 적용 | **미확인**. 라이선스 문서 없음 |
| `MeshyImports` (플레이어·적 모델) | 수정 없음 | **미확인**. 파일명상 Meshy AI 생성물이며 사용 조건은 생성 계정의 요금제에 따름 |
| LiberationSans SDF (TextMesh Pro 기본) | 간판 글자 | TMP Essential Resources 포함 폰트(SIL OFL) |
| NanumGothic | 사용 안 함 | 저장소에 OFL 파일 포함 |
| Unity 내장 Cube/Quad/Sphere 메시 | 간판 틀, 경고등 | Unity 내장 |
| `Assets/LevelDesign/RebootHub/*` | 신규 | 이번 작업에서 스크립트로 직접 생성. 외부 자료 없음 |

병합으로 들어온 팀 에셋(BGM, Free Slash VFX 등)은 레벨 디자인에서 쓰지 않으며 출처를 확인하지 않았다.

## 5. 검증 (병합 후, 2026-11-13)

| 항목 | 방법 | 결과 |
| --- | --- | --- |
| 컴파일 | Unity 6000.3.24f1 batchmode | 오류 0, 경고 0 |
| EditMode 테스트 | `-runTests -testPlatform EditMode` | 52/52 통과(팀의 새 `EnemySpawnPositionTests` 등 포함) |
| PlayMode 테스트 | `-runTests -testPlatform PlayMode` | 14/14 통과. 레벨 테스트 2개는 미리보기 씬, 나머지 12개는 원래 `Main.unity`(팀의 새 `EnemySpawnSceneTests`, `EnemySkeletalSceneTests`, `CombatVfxTests` 포함) |
| 시작 흐름, Dialogue Skip, Pause/Resume/Retry, Finishing timeScale 복구 | 기존 `CombatRoundSceneTests`, `PlayerSceneTests` | 통과 |
| 적 스폰·접근 | 레벨 PlayMode 테스트와 `runtime_enemy_summary.txt` | 모든 적이 y=-2 평면에 생성되어 플레이어에게 접근함. **광장 밖 생성은 남아 있음(6절)** |
| 표지판·광고판 | 진단 보고서 Signage check | 보안 표지판 2개 모두 벽에 붙음. 광고판 2개 모두 시작 카메라에서 100% 보이고 건물과 겹치지 않음 |
| 미리보기와 Main의 차이 | 두 씬 파일의 YAML 문서 비교 | 삭제 0, 추가 93(`Env_RebootHub`), 변경은 머티리얼 오버라이드 564개·RenderSettings·Directional Light·Volume 프로필 참조뿐 |
| 화면 | 편집 시점 캡처, Play Mode 런타임 카메라 캡처 | `Screenshots/` 참고(01·03·05는 병합된 Main, 나머지는 미리보기) |

스폰 분석(`diagnostics_after.md`, 플레이어가 갈 수 있는 모든 칸 기준). 장식에는 충돌체가 없어 Main(`diagnostics_before.md`)과 같은 값이다.

| 생성 지점 | 비율 |
| --- | --- |
| 스폰 높이의 광장 바닥 위 | 37.0% |
| 다른 높이(건물·데크 안, 아래 도시 위 공중) | 62.0% |
| 바닥 없음 | 0.9% |
| 충돌체와 겹침 | 36.8% |

런타임(`runtime_enemy_summary.txt`): Ready 초기 5명 중 1명, 전투 2.5초 시점 15명 중 3명, Retry 직후 5명 중 2명이 광장 바닥 밖이었다.

**FPS는 측정하지 않았다.** batchmode는 실제 Game View 프레임을 대표하지 않는다. 위의 렌더러·삼각형·조명 수는 정적 수치일 뿐 FPS가 아니다.

## 6. 남은 문제와 해결 후보 (디자인 범위 밖, 코드 미수정)

1. **광장 밖 스폰** (`EnemyRuntime.SpawnOne`): 플레이어 기준 15~25m 원 위의 임의 지점에 생성하지만, 광장 반경은 약 20m이다. 새 맵에서는 광장 밖이 건물·상부 데크·아래 도시라, 생성 지점의 약 63%가 광장 바닥 밖이고 약 37%는 건물 같은 충돌체 안이다. 적은 kinematic 상태로 Transform을 직접 옮기므로 벽을 통과해 들어온다.
   - 후보 A(권장): 같은 15~25m 분포에서 아래로 Raycast해 스폰 높이의 바닥이 있고 Collider와 겹치지 않는 지점만 채택한다. 시뮬레이션 결과 1회 시도 34.3%, 4회 82.4%, 10회 98.3%가 유효하다(8~25m로 넓히면 10회 99.5%). 실패하면 `EntryGate_North`/`EntryGate_South` 기준점으로 보낸다.
   - 후보 B: `EnemySettings`의 거리를 줄인다(데이터만 변경). 플레이어가 벽 근처에 있으면 여전히 밖에 생긴다.
   - 후보 C: NavMesh(`com.unity.ai.navigation` 설치됨)의 `SamplePosition`으로 스폰과 추적을 함께 처리한다. 범위가 크다.
2. **카메라 충돌 없음** (`PlayerRuntime.UpdateCamera`): 벽이나 난간 가까이에서 카메라가 구조물 안으로 들어가 플레이어를 가린다(`11_runtime_east_wall_camera_clip.jpg`). 후보: 목표 지점에서 카메라 쪽으로 SphereCast해 거리를 줄인다.
3. **플레이어 중력 없음**: `CharacterController.Move`에 수직 이동이 없어 계단을 오르면 내려오지 못하고, 높이가 다른 층을 쓸 수 없다. 새 맵은 북쪽·남쪽 모두 계단이라 영향이 커졌다. 상부 데크는 배경·연출로만 쓰고 전투는 광장으로 한정했다.
4. **북쪽 계단과 상부 데크의 용도**: 이전의 빈 `Area_B` 통로가 상부 데크로 오르는 계단으로 바뀌었다(`Area_B`·통로 그룹은 비어 있음). 데크를 전투 공간으로 쓸지, 계단을 막을지(보안 차단막) 정해야 한다.
5. **중앙 안테나 기둥 2개**: `96dd8bc`에서 BoxCollider 큐브가 붙어 플레이어는 막힌다. 적은 Transform으로 이동하고 회피 로직이 없어 여전히 통과한다.
6. **적 이동 보간** (`EnemyAgent.RunAI`, `EnemyPrototype.prefab`)
   - 고쳐진 것: 초기 적이 풀 원점 `(0,0,0)`에 겹치던 경쟁 상태는 `8e88d9e`에서 수정됐다(`Activate`가 Rigidbody 위치도 설정).
   - 남은 것: 추적 이동은 이번 병합에서 바뀌지 않았고, 보간(Interpolate)을 켠 kinematic Rigidbody의 Transform을 직접 옮긴다. FPS가 높을수록 이동이 느려질 수 있다. 레벨 테스트는 60 FPS로 고정해 측정한다. 자세한 내용은 `Docs/UiTheme/README.md` 마지막 절.

## 7. 팀 검토 요청

- 야간 톤과 명도. 실제 모니터에서 적과 플레이어가 충분히 구분되는지.
- 허브 탑이 북쪽 계단 위 상부 데크의 건물 입구 앞에 선다. Editor에서 데크 위 구조물과 겹쳐 보이는 곳이 없는지(`07_after_landmark_lookup.jpg`, `04_after_overview.jpg`).
- 6절 후보 A를 적 담당자가 구현할지, 북쪽 계단·상부 데크를 어떻게 쓸지.
- `LowPolySciFiCity`, `SciFi_Modular_Kit_Demo`, Meshy 모델의 출처와 사용 권한(제출 전 확인 필요).
- 미리보기 씬은 복사 시점의 사본이다. 팀원이 `Main.unity`를 고치거나 팀 Editor 도구(`GameDemo*PrefabBuilder`)가 Main에 쓰면 미리보기에는 반영되지 않으므로, 미리보기에서는 게임 로직을 고치지 말고 오래되면 `Preview: Rebuild` 메뉴로 다시 만든다.

## 8. 재현 명령

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
$UNITY -batchmode -projectPath . -executeMethod RebootHubEnvironmentBuilder.BuildBatch -rebootRefreshPreview -rebootSkipAssets -logFile build.log
$UNITY -batchmode -projectPath . -executeMethod RebootLevelDiagnostics.RunAll -rebootOut ./diag -rebootTag check -logFile diag.log
REBOOT_CAPTURE_DIR=./runtime $UNITY -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

에셋 생성 코드를 고쳤을 때만 `-rebootSkipAssets`를 뺀다.

Unity를 실행하면 `ProjectSettings/TimeManager.asset`과 TMP `LiberationSans SDF.asset`이 형식 업그레이드로 다시 저장될 수 있다. 내용 변경이 아니므로 이번 작업에서는 되돌렸다.

Editor에서 수동으로 확인하는 절차: `Main_RebootHub.unity`를 열고 Play를 누른다. Start → Skip → 카운트다운 뒤 WASD로 광장 가장자리까지 이동한다. 마우스로 허브와 좌우 광고판을 올려다보고, 남쪽 계단과 동·서 벽에서 카메라가 벽을 뚫는지 본다. ESC로 Pause → Resume, Pause → Retry → 확인 후 배치가 처음과 같은지 확인한다. Stats 창이나 Profiler로 FPS를 기록한다.

## 9. 채택 절차 (나중에 Main.unity로 합칠 때)

씬 파일끼리 병합하지 않는다. 미리보기의 변경은 모두 빌더가 다시 만들 수 있으므로 최신 Main에 다시 적용한다.

1. 팀원의 최신 `Main.unity` 변경을 먼저 받는다(`JH_Work` 등).
2. Editor에서 `Tools/REBOOT Level/Adopt: Apply To Main.unity` 실행, 또는 `BuildBatch -rebootScene main -rebootSkipAssets`.
3. 빌더 로그의 계단·벽면 측정값과 진단 보고서의 Signage check를 확인한다. 맵이 크게 바뀌었으면 캡처로 허브와 광고판 위치를 다시 본다.
4. EditMode/PlayMode 테스트를 실행한다. `RebootLevelSceneTests`의 `ScenePath`를 `Main.unity`로 바꾸고 일반 `SceneManager` 로더로 되돌린다.
5. 확인 후 `Main_RebootHub.unity`와 `.meta`를 삭제한다.

추가 오브젝트(`Env_RebootHub`)만 따로 옮기고 싶다면 `EditorSceneManager.MergeScenes`나 다중 씬 편집으로 옮길 수 있다. 하지만 머티리얼 교체와 하늘/안개 설정은 기존 오브젝트와 씬 설정의 수정이라 옮겨지지 않는다.

# REBOOT UI 테마: 고스트 + 사선 스트립

작성일 2026-11-11. 작업 브랜치 `level-design/reboot-hub`(커밋 안 함). 시안 페이지 [REBOOT UI Lab](https://claude.ai/artifact/PQfu9NxqnQmRtRztFZVjea)에서 저장한 조합을 적용했다.

| 항목 | 선택 |
| --- | --- |
| 팔레트 | D 고스트: 흰색 `#E9EEF2`(정보), 청록 `#19F0E0`(강조·준비 완료), 빨강 `#FF3B3B`(경고·감점) |
| 프레임 | 사선 스트립(굵은 왼쪽 선, 옅은 아래 선, 오른쪽 위 경고 사선 조각) |
| 패널 바탕 | 시안은 "선만"이었으나 게임 화면과 구분되도록 반투명 검정 58~78%로 올림(Linear 색공간이라 체감은 더 옅음) |
| 특수 게이지 | 육각 윤곽 |
| 타이머 | 숫자(기존 `01:40` 형식 유지) |
| 서체 | Chakra Petch(영문·숫자 UI) + IBM Plex Sans KR(대화 한글) |
| 효과 | 없음. 시안의 제목 글리치와 발광 2는 이후 요청으로 제거(스프라이트 발광, 글자 발광·흐린 그림자 포함) |

## 바뀐 것

- **새 스프라이트 25장** `Assets/Sprites/UI_Reboot/`: 원본 팩(`Assets/Sprites/UI`)과 같은 이름·크기로 생성. 원본은 수정하지 않았다.
  - 선 주변 발광(번짐)은 넣지 않는다(`GlowAlpha = 0`). 다시 켜려면 0.32로 바꾼다.
- **서체**
  - `Assets/Fonts/ChakraPetch/`: Medium·Bold TTF, OFL.txt, 정적 TMP 폰트 에셋 2개(영문·숫자·기호 문자셋 고정, Medium의 굵게 = Bold 에셋).
  - `Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf`, `OFL-IBMPlexSansKR.txt`: 대화용 한글. SemiBold는 화자와 본문이 한 폰트를 공유해야 해서(기존 테스트) 쓰이지 않으므로 받지 않았다.
- **TMP 재질 1개** `Assets/Sprites/UI_Reboot/Materials/ChakraPetch SDF - Reboot Text`: 번짐 없는 얇은 어두운 외곽선(0.14). 흐린 밑그림자와 발광은 쓰지 않는다.
- **UI 프리팹 8개** `Assets/Prefabs/UI/*.prefab`: 이미지 스프라이트, 글자색, 글자 재질, 버튼 hover/press 색만 바꿨다. View 바인딩과 구조는 그대로다.
  - 시작 화면의 임시 제목 `CYBER ARENA`를 `REBOOT`로, 부제를 `COMM HUB 07 // 100s EMERGENCY REBOOT`로 바꿨다.
  - 조작 안내 문구는 바꾸지 않고, 줄바꿈 대신 자동 크기로 한 줄에 맞췄다.
  - 원본 스프라이트의 큰 투명 여백에 맞춰 화면 모서리에 붙어 있던 요소만 화면 안쪽으로 옮겼다(원래 빌더의 의도 위치).
    - `Battle/PauseButton` (40,-40), `Battle/ScorePanel` (148,-40), `Battle/TimerPanel` (-40,-40)
    - `Dialogue/DialogueBox` (0,32), `Dialogue/SkipButton` (-40,-36)
    - `Pause/MenuPanel`: 0.8배 축소 때문에 버튼과 제목이 패널 밖으로 나가 배율 1, 크기 520×620, 위치 (0,30)으로 맞추고 제목·버튼을 가운데(x=0)로 정렬
- **`Assets/Scripts/Runtime/UI/UiPalette.cs`**(신규): 색 정의를 한곳에 모았다.
- 제목 글리치(`UiGlitchText`)는 추가했다가 요청으로 제거했다. 프리팹에 남은 컴포넌트는 테마 적용 시 지우고, 스크립트도 삭제했다.
- **`DialogueView.cs`**: 대화 폰트를 `Fonts/IBMPlexSansKR-Regular`에서 먼저 찾고, 없으면 기존 나눔고딕을 쓴다(실행 중 TMP 에셋 생성 방식은 그대로).
- **`BattleView.cs`**: 특수 게이지 링을 원형에서 육각으로 바꿨다. 점수 증감, 게이지, 피격 플래시 색은 팔레트를 쓴다. 로직은 그대로다.
- **`CombatRoundSceneTests.cs` 1줄**: "가득 찬 게이지는 빨강 성분 > 파랑 성분(금색)" 확인을 "팔레트 강조색과 같다"로 바꿨다.

## 다시 만들기와 되돌리기

- Editor 메뉴 `Tools/REBOOT UI/Apply Theme (Ghost Skew)` 또는 `-executeMethod RebootUiThemeBuilder.ApplyBatch`. 같은 경로를 덮어써 GUID를 유지한다.
- 미리보기: `-executeMethod RebootUiThemeBuilder.CapturePreviews -rebootOut <dir> -rebootBackdrop <png>` (결과는 `Previews/`).
- 되돌리기: `git checkout -- Assets/Prefabs/UI Assets/Scripts/Runtime/UI/BattleView.cs Assets/Tests/PlayMode/CombatRoundSceneTests.cs` 후 `Assets/Sprites/UI_Reboot` 삭제.
- 팀 빌더 `Tools/Game Demo UI/Build All UI Prefabs`를 실행하면 원본 스프라이트로 프리팹을 새로 만들어 이 테마가 사라진다. 그 경우 이 테마 메뉴를 다시 실행하면 된다.

## 서체 출처

`github.com/google/fonts`(Google Fonts 공식 저장소)에서 2026-11-11에 받았다. 파일 형식과 OFL 1.1 문서를 확인했다.

| 파일 | 크기 | 라이선스 |
| --- | --- | --- |
| `ChakraPetch-Medium.ttf` | 78.6KB | OFL 1.1, Chakra Petch Project Authors |
| `ChakraPetch-Bold.ttf` | 78.4KB | OFL 1.1 |
| `IBMPlexSansKR-Regular.ttf` | 2.8MB | OFL 1.1, IBM Corp. (예약 글꼴 이름 "Plex": 수정본을 같은 이름으로 배포하지 말 것) |

Chakra Petch 정적 에셋에는 영문·숫자·기호만 들어 있다. 영문 UI에 한글 문구를 넣으려면 IBM Plex Sans KR을 대체(fallback) 폰트로 추가해야 한다.

## 검증

- 서체·블러 제거 후: EditMode 42/42, PlayMode 8 통과 + 1 실패(아래 팀 테스트) + 1 판정 불가(아래 적 문제). 대화 한글 폰트 테스트 통과.
- 서체 적용 전 기록: EditMode 42/42 통과.
- PlayMode 2회 실행
  - 1회차: 9 통과 + 1 판정 불가.
  - 2회차: 8 통과 + 1 실패 + 1 판정 불가.
  - 실패는 팀 테스트 `AcceptedHitUpdatesHudAndRetryClearsRound`. UI 변경 전 상태에서도 3회 중 2회 실패하고, 새 UI에서는 3회 모두 통과한 실행 순서·타이밍 의존 테스트다.
  - 판정 불가는 레벨 테스트가 아래 적 생성 경쟁 상태를 감지한 경우다.
- 화면별 미리보기는 `Previews/`에 있다. 배경은 실제 Play Mode 캡처이고, Edit Mode 렌더링이라 일시정지 버튼은 비활성(흐린) 상태로 보인다.
- 실제 Play에서 확인하지 못한 것: hover/press 색, 다른 해상도·Safe Area에서의 겹침, FPS.

## 피격 효과: H3 블록 깨짐 + 옅은 빨간 위험 신호

시안 페이지에서 고른 H3에, 요청대로 붉은 위험 신호 색을 살짝 섞었다. 플레이어가 맞으면 다음이 0.18초 동안 함께 지나간다.

- **3D 화면**: 화면을 18×10 블록으로 나눠, 세기에 비례한 일부 블록을 좌우로 어긋나게 한다. 가로 줄 찢김과 RGB 분리를 더한다. 어긋난 블록의 약 30%는 경고 빨강(가끔 청록)으로 물들고, 화면 가장자리에 붉은 기운이 옅게 깔린다.
- **세기 곡선**: 처음 15% 동안 올라갔다가 제곱 곡선으로 빠르게 사라진다. 최대 세기는 약 27ms만 유지된다.
- **HUD**: 최대 8px 흔들림(`hudShakePixels`).
- **가장자리 빨강**: 화면 전체를 덮던 18% 빨강 플래시를 가운데가 투명한 테두리로 바꿨다(`hitFlashOpacity` 그대로).
- **일시정지**: Battle에서만 진행되고 Paused에서는 그 프레임 그대로 멈춘다. Retry, 화면 전환, 비활성화 시 즉시 꺼진다. 기존 피격 피드백과 같은 갱신 경로를 쓴다.
- **빈도**: 피격 후 0.8초 무적이라 초당 최대 약 1.25회다. 화면을 크게 덮는 깜빡임을 초당 3회 미만으로 두라는 접근성 기준(Xbox XAG 118, WCAG 2.3.1) 안이다. 빨강은 테두리와 일부 블록으로 제한했다.

### 구성

| 파일 | 내용 |
| --- | --- |
| `Assets/Scripts/Runtime/UI/ScreenGlitchState.cs` | 현재 글리치 세기·시드(게임 시스템 어셈블리). BattleView가 쓰고 렌더러 기능이 읽는다 |
| `Assets/Scripts/Runtime/UI/BattleView.cs` | 피격 시 글리치 구동, HUD 흔들림, 가장자리 빨강. 조절값: `hitGlitchDuration` 0.18, `hitGlitchStrength` 1, `hudShakePixels` 8 |
| `Assets/Scripts/Rendering/RebootHitGlitch.shader` | `Hidden/Reboot/HitGlitch` 블록 깨짐 셰이더 |
| `Assets/Scripts/Rendering/RebootHitGlitchFeature.cs` | URP 렌더러 기능(RenderGraph Blit). 세기가 0이면 패스를 추가하지 않아 평소 비용이 없다 |
| `Assets/Editor/UiTheme/RebootHitGlitchSetup.cs` | `Tools/REBOOT UI/Install Hit Glitch Renderer Feature`(중복 등록 없음)와 글리치 프레임 캡처 |
| `Assets/Settings/PC_Renderer.asset` | **팀 공용 렌더러에 기능 1개 추가.** Unity가 다시 저장하면서 줄바꿈과 `m_AssetVersion 2→3` 형식 변경도 함께 생겼다 |

- `Mobile_Renderer`에는 등록하지 않았다(현재 품질 설정은 PC).
- 글리치는 후처리 뒤 3D 화면에만 적용된다. Screen Space Overlay UI에는 닿지 않으므로 HUD는 흔들림으로만 반응한다.

### 확인

- 글리치 프레임 캡처: `Previews/09_HitGlitch_0.jpg`, `_50.jpg`, `_100.jpg`(UI 제외, 3D 화면만).
- EditMode 42/42, PlayMode 9 통과 + 1 판정 불가(적 생성 문제). 피격 피드백이 일시정지 중 멈추고 Retry 후 사라지는지 확인하는 기존 테스트가 통과했다.
- 실제 Play에서 확인하지 못한 것: 움직이는 글리치의 체감 강도, 연속 피격 시 느낌, FPS 영향.

## 함께 발견한 적 시스템 문제(수정하지 않음)

`EnemyPrototype.prefab`의 Rigidbody 설정과 프로젝트 물리 설정이 다음과 같다.

- kinematic + **Interpolate**
- `DynamicsManager.autoSyncTransforms = false`

`EnemyAgent.Activate`와 `RunAI`는 Transform만 옮긴다. 그래서 고정 물리 스텝이 없는 프레임에서는 보간이 Rigidbody의 이전 위치로 Transform을 덮어쓴다.

배치 테스트에서 관찰한 현상:

- Ready 직후 초기 적 5명이 Transform과 Rigidbody 모두 `(0,0,0)`(풀 위치)에 겹쳐 있었다.
- 전투 중 이동 속도가 설정값 3.5m/s의 약 1/7이었다.

실제 플레이에서도 프레임 타이밍에 따라 생기거나, FPS가 높을수록 적이 느리거나 떨릴 수 있다.

해결 후보(적 담당):

- `Activate`에서 `_body.position`·`_body.rotation`도 함께 설정한다.
- 또는 kinematic 이동에는 보간을 끈다(`Interpolate None`).
- 또는 이동을 `Rigidbody.MovePosition`으로 바꾼다.

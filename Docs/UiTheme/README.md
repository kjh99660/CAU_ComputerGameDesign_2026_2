# REBOOT UI 테마: 고스트 + 사선 스트립

작성일 2026-11-11, 갱신 2026-11-22(화면 연출 추가). 테마는 브랜치 `level-design/reboot-hub`(원격에 올림), 대화 연출은 그 위의 `ui/dialogue`(원격에 올림), 화면 연출은 그 위의 `ui/screen-effects`. 시안 페이지 [REBOOT UI Lab](https://claude.ai/artifact/PQfu9NxqnQmRtRztFZVjea)에서 저장한 조합을 적용했다.

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
- 미리보기: `-executeMethod RebootUiThemeBuilder.CapturePreviews -rebootOut <dir> -rebootBackdrop <png>` (결과는 `Previews/`). 화면별 기본 모습 8장에 이어 연출 도중의 한 순간 4장(`ui_Countdown_Pop` 등)도 찍는다.
- 연출 조절값을 프리팹에 다시 적기: `-executeMethod RebootUiThemeBuilder.SaveViewSettingsBatch` (Countdown·Battle·Result 프리팹만 저장한다).
- 종료 화면의 시간 표시(00:00)만 다시 넣기: `-executeMethod RebootUiThemeBuilder.ApplyFinishingTimerBatch` (Finishing 프리팹만 저장한다). 전투 HUD의 TimerPanel을 복사해 오른쪽 위에 두며, `Apply Theme`에도 들어 있다. 이미 있으면 아무것도 바꾸지 않는다.
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

- `JH_Work` `8e88d9e` 병합 후(2026-11-13): EditMode 52/52, PlayMode 14/14 통과. 아래의 판정 불가(적 원점 겹침)는 팀 수정으로 사라졌고, 팀 테스트 `AcceptedHitUpdatesHudAndRetryClearsRound`도 이번 실행에서 통과했다.
- 서체·블러 제거 후: EditMode 42/42, PlayMode 8 통과 + 1 실패(아래 팀 테스트) + 1 판정 불가(아래 적 문제). 대화 한글 폰트 테스트 통과.
- 서체 적용 전 기록: EditMode 42/42 통과.
- PlayMode 2회 실행
  - 1회차: 9 통과 + 1 판정 불가.
  - 2회차: 8 통과 + 1 실패 + 1 판정 불가.
  - 실패는 팀 테스트 `AcceptedHitUpdatesHudAndRetryClearsRound`. UI 변경 전 상태에서도 3회 중 2회 실패하고, 새 UI에서는 3회 모두 통과한 실행 순서·타이밍 의존 테스트다.
  - 판정 불가는 레벨 테스트가 아래 적 생성 경쟁 상태를 감지한 경우다.
- 화면별 미리보기는 `Previews/`에 있다. 배경은 병합 전 맵의 실제 Play Mode 캡처이고(UI 확인용이라 다시 찍지 않음), Edit Mode 렌더링이라 일시정지 버튼은 비활성(흐린) 상태로 보인다.
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

## 대화 연출

기획서(10/25) 2-7·2-8의 대사와 UI 설계 6절의 대화 화면을 연결했다. 브랜치 `ui/dialogue`.

- **대사**: 임시 문구 3줄을 도입 6줄, 마무리 4줄로 바꿨다(`Assets/Resources/DialogueSequence.asset`). 노아는 왼쪽, K-7은 오른쪽이다.
- **타이핑**: 대사가 한 글자씩 나온다(초당 40자, unscaled time). TMP 문자열은 그대로 두고 그리는 글자 수만 바꿔 매 프레임 할당이 없다.
- **넘기기**: Space, Enter, 화면 아무 곳 클릭으로 넘긴다.
  - 타이핑 중이면 그 줄을 한 번에 다 보여 주고, 다 보인 줄이면 다음 줄로 넘어간다.
  - 마지막 줄을 넘기거나 Skip을 누르면 같은 `DialogueCompleteRequest`를 한 번만 보낸다.
- **열린 직후 입력 무시**: 대화가 열린 뒤 0.35초 동안은 넘기기 입력을 받지 않는다. 전투 끝까지 누르던 공격 클릭이 마무리 대사 첫 줄을 넘기지 않게 하려는 것이다. Skip에는 적용하지 않는다.
- **초상화와 화자 강조**
  - 대화 프리팹에 초상화가 아예 없어서 강조 코드가 아무 효과가 없었다(처음 커밋부터 없었음).
  - 노아·K-7 초상화를 대사 상자 뒤에 배치했다. 위치와 크기(420×630)는 팀 원래 빌더(`GameDemoUiPrefabBuilder`)의 값이다.
  - 말하는 쪽은 원래 밝기·크기, 듣는 쪽은 어둡게, 0.96배, 12px 낮게 둔다. 전환은 0.15초다. 대화가 새로 시작되면 첫 화자는 전환 없이 바로 강조한다.
- **넘김 표시**: 줄이 다 보이면 대사 상자 오른쪽 아래의 청록 마름모가 초당 1.2회 깜빡인다. 글꼴에 ▼ 글자가 있는지에 기대지 않도록 도형으로 그렸다.
- **Retry**: 도입 대화는 매번 첫 줄, 글자 0개부터 다시 시작한다. 코루틴 없이 기존 UI 갱신 루프에서 처리해 Reset 뒤에 남는 연출이 없다.

### 조절값(`DialogueView`, 모두 임시값)

| 값 | 기본 | 뜻 |
| --- | --- | --- |
| `charactersPerSecond` | 40 | 타이핑 속도. 0이면 줄 전체를 바로 보여 준다 |
| `inputGraceSeconds` | 0.35 | 대화가 열린 직후 넘기기 입력을 무시하는 시간 |
| `speakerTransitionSeconds` | 0.15 | 화자 강조가 옮겨 가는 시간 |
| `listenerTint` | (0.42, 0.47, 0.52) | 듣는 쪽 초상화 색 |
| `listenerScale`, `listenerOffsetY` | 0.96, -12 | 듣는 쪽 크기와 높이 |
| `nextIndicatorBlinkHz` | 1.2 | 넘김 표시 깜빡임 횟수(초당) |

### 구성

| 파일 | 내용 |
| --- | --- |
| `Assets/Scripts/Runtime/UI/DialogueProgress.cs` | 진행 규칙(현재 줄, 타이핑, 넘김, 완료·Skip). Unity 오브젝트 없이 EditMode에서 검증한다 |
| `Assets/Scripts/Runtime/UI/DialoguePresenter.cs` | 진행 규칙을 화면에 연결하고, 열린 직후 입력을 무시하고, 완료 요청을 보낸다 |
| `Assets/Scripts/Runtime/UI/DialogueView.cs` | 보이는 글자 수, 화자 강조, 넘김 표시, 전체 화면 클릭 영역(`AdvanceArea`, 실행 중 생성) |
| `Assets/Scripts/Runtime/UI/UiLayer.cs` | Space·Enter 키. ESC와 같은 임시 키 처리 방식이다 |
| `Assets/Editor/UiTheme/RebootUiThemeBuilder.cs` | 초상화·넘김 표시 배치. `Apply Theme`에 포함되고, 대화 프리팹만 적용하는 `ApplyDialogueLayoutBatch`도 있다 |
| `Assets/Prefabs/UI/Dialogue.prefab` | `LeftPortrait`, `RightPortrait`, `DialogueBox/NextIndicator` 추가 |

### 확인

- EditMode 새 테스트 10개
  - `DialogueProgressTests` 8개: 타이핑 속도, 줄 완성 후 넘김, 완료·Skip은 한 번만, 다시 시작, 빈 대사
  - `DialogueSequenceDataTests` 2개: 대사가 비어 있지 않음, 인물마다 초상화 쪽이 한결같음
- PlayMode `DialogueAdvancesSkipsAndRestartsAfterRetry`를 고쳤다. 열린 직후 입력 무시, `timeScale`이 0이어도 타이핑, 줄 완성 후 넘김, 넘김 표시, 초상화 강조와 전환, Retry 뒤 첫 줄부터 다시 타이핑, Skip, 마무리 대화 뒤 Result를 확인한다.
- 전체 결과: EditMode 62/62, PlayMode 14개 중 13개 통과.
  - 실패한 1개는 팀 테스트 `AcceptedHitUpdatesHudAndRetryClearsRound`(공격이 4초 안에 근처 적을 맞히는지)다. 대화는 요청으로 바로 건너뛰고 전투 구간만 보는 테스트다.
  - 같은 시간대에 번갈아 4번씩 돌렸을 때 대화 작업 전 커밋(`472a98b`)은 2번, 이 브랜치는 1번 실패했다. 이날 돌린 것을 모두 합치면 8번 중 3번, 9번 중 5번이다. 실행 시점(시스템 부하)에 따라 흔들리는 기존 테스트로 본다.
- 미리보기 `Previews/08_Dialogue.jpg`를 다시 찍었다(Edit Mode 렌더링, 노아가 말하는 장면).
- 자동으로 확인하지 못한 것: 실제 Space·Enter 키 입력(입력 장치 시뮬레이션 테스트를 구성하지 않음), 타이핑 속도와 강조의 체감, 다른 해상도에서 초상화와 대사 상자의 겹침.
- 초상화 이미지 두 장의 출처와 사용 조건은 확인되지 않았다(팀 원본 스프라이트 팩 `Assets/Sprites/UI/Dialogue`).
- 수동 확인 절차: `Main.unity`에서 Play → Start → 대사가 한 글자씩 나오는지, Space·Enter·클릭으로 넘어가는지, 화자가 바뀔 때 초상화 밝기가 옮겨 가는지 본다. Skip을 누르고, Pause → Retry 뒤 다시 첫 줄부터 나오는지 본다.
- 이번에 하지 않은 것: 점수별 노아 평가 대사(팀 합의 후), 대사 넘김 효과음(사운드 담당과 합의 후).

## 화면 연출: 카운트다운, HUD 강조, 결과 숫자

2026-11-22. UI 설계(`Docs/Design/02_UI_AND_SCREEN_FLOW.md`) 7·8·10·15절의 연출 가운데 다른 담당의 결정 없이 할 수 있는 네 가지를 넣었다. 코드에 TODO로만 남아 있던 부분이다.

- **카운트다운(7절 "숫자는 확대 후 축소")**
  - 숫자: 1.55배로 나타나 0.22초 동안 제자리 크기로 줄어든다. 단계가 끝나기 0.22초 전부터 옅어지며 0.9배로 조금 더 작아진다. 육각 틀도 숫자가 바뀔 때마다 살짝(1.06배) 튄다.
  - START: 글자가 1.5배에서 튀어나오고, 육각 틀은 1.35배로 커지며 사라지고, 화면 전체에 청록색이 0.35초 동안 옅게 번쩍인다.
  - 번쩍임 세기는 0.06이다. 처음에 0.2로 두었더니 화면 전체가 청록으로 덮였다(Linear 색공간에서는 어두운 화면 위의 밝은 색이 수치보다 훨씬 강하게 보인다). 0으로 두면 번쩍임을 끈다.
  - START가 끝나는 프레임에 완료 요청을 보내는 흐름과 단계 시간(0.8초, START 0.55초)은 그대로다.
- **타이머 경고(8절 "10초 이하 Warning Color")**: 표시되는 남은 시간이 10초 이하가 되면 숫자가 경고 빨강으로 바뀌고, 초가 바뀔 때마다 1.14배에서 0.25초 동안 줄어들며 한 번 튄다. 초가 바뀔 때만 계산하므로 평소 프레임 비용은 없다.
- **게이지 준비 표시(10절 "Ready 시 Glow/Pulse")**: 게이지가 가득 차 있는 동안 육각 틀이 초당 1.4회, 최대 1.08배로 맥동한다. 게이지 틀만 움직여 화면 전체가 번쩍이지 않는다. 쓰거나 초기화되면 바로 원래 크기로 돌아간다. 맥동이 가운데를 기준으로 커지도록 게이지 틀의 기준점을 아래에서 중앙으로 옮겼다(화면에서의 위치는 같다).
- **결과 화면(15절)**: 최종 점수가 0에서 0.8초 동안 올라가고, 통계 네 칸은 0.4초 뒤부터 왼쪽부터 0.12초 간격으로 0.45초씩 따라 오른다(전체 약 1.2초). 처음에 빠르고 끝에서 느려진다. 값 자체는 고정이고 표시만 올라간다. 다 오르면 Retry 버튼이 초당 1.2회, 최대 1.04배로 맥동한다. Retry는 숫자가 오르는 중에도 눌린다.
- **공통**
  - 모두 unscaled time으로 재생한다. 코루틴 없이 기존 UI 갱신 루프(Presenter의 `Update`)에서 처리해 Reset 뒤에 남는 연출이 없다.
  - HUD 강조는 Battle에서만 갱신되어 Paused에서는 그 모습 그대로 멈춘다.
  - 크기는 Transform으로, 불투명도는 CanvasRenderer로 바꿔 글자 메시를 다시 만들지 않는다.
  - 결과 화면이 보이지 않는 동안에는 통계가 바뀌어도 글자를 다시 만들지 않는다(이전에는 적을 잡을 때마다 숨은 결과 화면의 글자 다섯 칸을 새로 만들었다).
  - 상태 전환은 건드리지 않았다. UI는 지금처럼 요청만 보낸다.

### 조절값(모두 임시값, 프리팹에 기록)

| View(프리팹) | 값 | 기본 | 뜻 |
| --- | --- | --- | --- |
| `CountdownView` | `numberPopScale`, `numberPopSeconds` | 1.55, 0.22 | 숫자가 나타날 때의 크기와 제자리로 줄어드는 시간 |
| | `numberFadeOutSeconds`, `numberEndAlpha` | 0.22, 0.2 | 단계 끝에서 옅어지는 시간과 마지막 불투명도 |
| | `startPopScale`, `startPopSeconds` | 1.5, 0.2 | START 글자가 나타날 때의 크기와 시간 |
| | `startFlashSeconds`, `startFlashOpacity` | 0.35, 0.06 | 화면 번쩍임의 시간과 세기. 세기가 0이면 끈다 |
| `BattleView` | `timerWarningSeconds` | 10 | 이 초 이하로 표시되면 경고색. 0이면 끈다 |
| | `timerTickScale`, `timerTickSeconds` | 1.14, 0.25 | 경고 중 초마다 튀는 크기와 시간 |
| | `gaugePulseHz`, `gaugePulseScale` | 1.4, 1.08 | 가득 찬 게이지의 맥동 횟수(초당)와 최대 크기 |
| `ResultView` | `scoreCountSeconds` | 0.8 | 최종 점수가 오르는 시간. 0이면 바로 보여 준다 |
| | `statCountSeconds`, `statDelaySeconds`, `statStaggerSeconds` | 0.45, 0.4, 0.12 | 통계 한 칸이 오르는 시간, 시작 지연, 칸 사이 간격 |
| | `retryPulseHz`, `retryPulseScale` | 1.2, 1.04 | Retry 버튼의 맥동 횟수(초당)와 최대 크기 |

값은 `Countdown.prefab`, `Battle.prefab`, `Result.prefab`에 적혀 있다. **조절은 프리팹(Inspector)에서 한다.** 프리팹에 값이 없으면 Unity는 그 프리팹을 가져온 시점의 코드 기본값을 쓰고, 나중에 코드 기본값만 바꿔도 이미 가져온 컴퓨터에는 반영되지 않는다(이번에 번쩍임 세기를 낮췄을 때 실제로 겪었다). 그래서 값을 프리팹에 적었다. 이때 이전부터 있던 피격 글리치 값 3개(`hitGlitchDuration` 등)도 같은 값으로 함께 적혔다.

### 구성

| 파일 | 내용 |
| --- | --- |
| `Assets/Scripts/Runtime/UI/UiMotion.cs` | 연출 계산식(튀어나오기, 사라지기, 맥동 등). Unity 오브젝트 없이 EditMode에서 검증한다 |
| `Assets/Scripts/Runtime/UI/ResultCountUp.cs` | 결과 숫자 올리기의 진행 규칙(순서, 최종값 도착, 다시 시작) |
| `CountdownView.cs`, `CountdownPresenter.cs` | 단계가 시작된 뒤 지난 시간으로 자세를 정한다. 번쩍임 이미지(`StartFlash`)는 실행 중에 만들고 입력을 막지 않는다 |
| `BattleView.cs`, `BattlePresenter.cs` | 타이머 경고와 게이지 맥동(`UpdateHud`). 기존 피격·점수 피드백 경로는 그대로다 |
| `ResultView.cs`, `ResultPresenter.cs` | 결과 화면이 열릴 때 0부터 올리기 시작하고, 값이 바뀐 칸만 글자를 다시 만든다 |
| `Assets/Prefabs/UI/Countdown.prefab`, `Battle.prefab`, `Result.prefab` | 조절값 추가(줄 추가만 23개). `Countdown`은 육각 틀 참조(`numberFrame`)도 연결했다 |
| `Assets/Editor/UiTheme/RebootUiThemeBuilder.cs` | 연출 미리보기 4장과 `SaveViewSettingsBatch` |

### 확인

- EditMode 새 테스트 14개
  - `UiMotionTests` 6개: 시작값과 끝값, 범위 밖 입력, 한 방향으로만 변하는지
  - `ResultCountUpTests` 8개: 0에서 시작, 줄어들지 않고 최종값에 정확히 도착, 점수 먼저·통계는 왼쪽부터, 시간 0이면 즉시, 도중에 값이 바뀌는 경우, 다시 시작
- PlayMode 새 테스트 4개(`UiEffectsSceneTests`, 실제 `Main.unity`)
  - 카운트다운: `timeScale`이 0이어도 3 → 2 → 1로 넘어가며 숫자가 움직이고, START에서 번쩍임이 켜졌다 꺼지고, Battle로 넘어간다. Retry 뒤에는 3부터 다시 시작하고 이전 START 자세가 남지 않는다.
  - 타이머: 11초는 보통 색, 10초부터 경고색이고 초마다 튄다. 12초 라운드에서 실제 시계가 10초에 닿으면 바뀌고, Pause 동안 유지되며, Retry로 100초 라운드가 시작하면 보통 색으로 돌아간다.
  - 게이지: 가득 차면 맥동하고, Pause에서 멈추고, 특수 공격으로 쓰면 바로 원래 크기가 되며, 가득 찬 채 Retry해도 남지 않는다.
  - 결과: `timeScale`이 0이어도 0에서 최종값까지 줄어들지 않고 오른다. 다 오르면 Retry가 맥동한다. Retry는 확인 없이 Ready로 가고 숫자와 강조가 초기화된다.
- 전체 결과(2026-11-22): EditMode 76/76. PlayMode는 두 번 돌려 18/18, 17/18이었다.
  - 실패한 1개는 팀 테스트 `AcceptedHitUpdatesHudAndRetryClearsRound`로, HUD를 보기 전 단계인 "공격이 4초 안에 근처 적을 맞히는지"에서 실패했다. 이 테스트만 네 번 다시 돌리면 세 번 통과했다. 이전부터 흔들리던 테스트다(위 "대화 연출"의 확인 참고).
  - 컴파일 오류와 경고는 0이다.
- 미리보기(Edit Mode 렌더링, 실제 실행과 같은 View 메서드로 만든 한 순간): `Previews/10_Countdown_Pop.jpg`, `11_Countdown_Start.jpg`, `12_Battle_Warning.jpg`, `13_Result_Counting.jpg`. 배경은 미리보기 씬의 편집 시점 캡처다.
- 확인하지 못한 것: 실제 Play에서의 움직임과 체감(크기, 속도, 번쩍임 세기), 다른 해상도에서의 겹침, FPS.
- 수동 확인 절차: `Main.unity`에서 Play → Start → Skip → 숫자가 커졌다 줄어드는지와 START에서 화면이 한 번 옅게 번쩍이는지 본다. 전투에서 적을 13번 잡아 게이지를 채우고 육각 틀이 맥동하는지, E로 쓰면 멈추는지 본다. 남은 시간이 10초가 되면 타이머가 빨갛게 바뀌는지 본다(기다리기 어려우면 `CombatSettings`의 `roundDuration`을 15로 줄인다). 결과 화면에서 숫자가 오른 뒤 Retry가 맥동하는지 본다.
- 이번에 하지 않은 것: 게이지의 키 안내(8절 "Skill Input Icon 또는 Label"). 특수 공격 키가 정해지지 않았다. 지금 코드는 E(범위 공격, 게이지 소모)이고, `WH_Dodge` 브랜치의 Q 스킬은 게이지를 쓰지 않는다. 키와 게이지 연결이 정해진 뒤에 넣는다.

## 함께 발견한 적 시스템 문제(수정하지 않음)

`EnemyPrototype.prefab`의 Rigidbody 설정과 프로젝트 물리 설정이 다음과 같다.

- kinematic + **Interpolate**
- `DynamicsManager.autoSyncTransforms = false`

보간을 켠 Rigidbody는 물리 시스템이 Transform을 관리하므로, 스크립트가 Transform만 옮기면 고정 물리 스텝이 없는 프레임의 이동은 보간이 덮어쓴다.

**`JH_Work` `8e88d9e`에서 고쳐진 것**(이 브랜치에 병합): `EnemyAgent.Activate`가 Rigidbody의 위치·회전도 함께 옮겨, Ready 직후 초기 적이 `(0,0,0)`(풀 위치)에 겹치던 문제가 사라졌다. 스폰 높이도 "라운드 시작 플레이어 높이 + `spawnHeight`"로 바뀌었다(`Docs/EnemySpawnFixValidation.md`).

**남은 것**: 추적 이동(`EnemyAgent.RunAI`)은 이번 병합에서 바뀌지 않았고 여전히 Transform만 옮긴다. 병합 전 배치 테스트(수백 FPS)에서 이동 속도가 설정값 3.5m/s의 약 1/7로 관찰됐다. 실제 플레이에서도 FPS가 높을수록 적이 느리거나 떨릴 수 있다. 레벨 PlayMode 테스트는 실제 플레이에 가까운 60 FPS로 고정해 측정한다.

해결 후보(적 담당):

- kinematic 이동에는 보간을 끈다(`Interpolate None`).
- 또는 이동을 `Rigidbody.MovePosition`으로 바꾼다.

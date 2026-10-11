# Cyberpunk UI Complete Asset Pack v2

기존 화면 시안을 Unity UI로 재구성하기 위한 투명 PNG 에셋 팩입니다.

## Naming Convention

모든 PNG는 `ScreenType_ResourceType.png` 형식을 사용합니다.

예시:

- `Battle_ScorePanel.png`
- `Pause_ButtonDanger.png`
- `RetryConfirmation_WarningIcon.png`

## Start

- `Start_TitleFrame.png`: 게임 제목과 부제 텍스트를 배치하는 프레임
- `Start_StartButton.png`: Start 버튼 프레임
- `Start_ControlHintPanel.png`: 조작법 안내 텍스트 패널

## Intro / Outro Dialogue

- `Dialogue_TextBox.png`: 화자 이름과 대사 본문을 배치하는 하단 패널
- `Dialogue_LeftPortraitFrame.png`: 좌측 Portrait 프레임
- `Dialogue_RightPortraitFrame.png`: 우측 Portrait 프레임
- `Dialogue_LeftCharacterPortrait.png`: 좌측 캐릭터 예시 Portrait
- `Dialogue_RightCharacterPortrait.png`: 우측 캐릭터 예시 Portrait
- `Dialogue_SpeakerNameUnderline.png`: 화자 이름 하단 장식선
- `Dialogue_SkipButton.png`: Skip 버튼 프레임
- `Dialogue_ContinuePrompt.png`: 입력 키와 Continue 문구를 배치하는 프레임

화자 강조와 비화자 어둡게 처리는 별도 PNG를 사용하지 않고 `CanvasGroup.alpha`, `Image.color` 또는 UI Material로 처리하는 것을 권장합니다.

## Countdown

- `Countdown_NumberFrame.png`: 3, 2, 1 숫자 주변 원형 프레임
- `Countdown_ReadyFrame.png`: READY / START 텍스트 장식 프레임
- `Countdown_StartFlash.png`: START 전환 시 Scale/Fade로 재생하는 원형 플래시

숫자와 READY / START 문구는 TextMeshPro로 표시합니다.

## Battle HUD

- `Battle_PauseButton.png`: Pause 버튼
- `Battle_ScorePanel.png`: Score와 증가량 표시 패널
- `Battle_TimerPanel.png`: 남은 시간 표시 패널
- `Battle_SpecialGaugeFrame.png`: Special Gauge 외곽 프레임
- `Battle_SpecialGaugeFill.png`: `Image Type: Filled`, `Radial 360`으로 사용하는 게이지 Fill
- `Battle_SpecialGaugeIcon.png`: 게이지 중앙 아이콘
- `Battle_KeycapFrame.png`: 입력 키 표시 프레임

## Pause

- `Pause_MenuPanel.png`: Pause 메뉴 컨테이너
- `Pause_HeaderFrame.png`: PAUSED 제목 프레임
- `Pause_ButtonPrimary.png`: Hover / Selected 버튼
- `Pause_ButtonSecondary.png`: 기본 버튼
- `Pause_ButtonDanger.png`: Quit처럼 주의가 필요한 버튼

## Retry Confirmation

- `RetryConfirmation_ModalPanel.png`: 확인창 컨테이너
- `RetryConfirmation_WarningIcon.png`: 경고 아이콘
- `RetryConfirmation_ConfirmButton.png`: Restart 확인 버튼
- `RetryConfirmation_CancelButton.png`: Cancel 버튼

Quit은 확인창 없이 Start 화면으로 복귀하므로 별도 Quit 확인 리소스는 포함하지 않았습니다.

## Finishing

- `Finishing_TimeUpFrame.png`: TIME UP 중앙 프레임
- `Finishing_SlowMotionBadge.png`: SLOW MOTION x0.5 문구 프레임
- `Finishing_ScorePanel.png`: Finishing 중 계속 갱신되는 Score 패널
- `Finishing_TimerPanel.png`: 00:00 Timer 패널
- `Finishing_VignetteOverlay.png`: 1920x1080 화면 외곽 슬로모션 연출 Overlay

## Result

- `Result_TitleFrame.png`: RESULT 제목 프레임
- `Result_FinalScorePanel.png`: 최종 점수 패널
- `Result_StatTile.png`: Kills, Max Combo, Hits Taken, Special Use에 재사용하는 통계 타일
- `Result_RetryButton.png`: Retry 버튼
- `Result_SeparatorLine.png`: Result 영역 구분선

## Unity Import Settings

- `Texture Type`: Sprite (2D and UI)
- `Sprite Mode`: Single
- `Alpha Is Transparency`: On
- `Generate Mip Maps`: Off
- 화면 크기에 따라 늘어나는 패널과 버튼은 Sprite Editor에서 모서리 장식 안쪽에 Border를 설정하고 `Image Type: Sliced`를 사용합니다.
- Character Portrait는 `Mesh Type: Tight`, UI 프레임과 패널은 `Mesh Type: Full Rect`를 권장합니다.
- 배경 암전은 별도 PNG 대신 전체 화면 `Image`에 검정색과 Alpha를 지정하세요.
- 점수, 시간, 대사, 버튼명, 카운트다운 숫자는 TextMeshPro로 구성합니다.

설계에 따라 Damage Number, HP Bar, Aim Assist, Lock-on Indicator는 포함하지 않았습니다.

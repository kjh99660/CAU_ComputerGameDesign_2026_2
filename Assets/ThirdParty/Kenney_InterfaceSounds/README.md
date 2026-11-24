# Kenney Interface Sounds 일부 (CC0)

화면과 진행 효과음에 쓰는 외부 소리다. 팩 전체(100개)가 아니라 쓰는 파일 9개만 넣었다.

## 출처와 라이선스

- 팩: Interface Sounds 1.0, 만든 곳 Kenney (www.kenney.nl)
- 페이지: https://kenney.nl/assets/interface-sounds (License 칸 "Creative Commons CC0")
- 받은 파일: `kenney_interface-sounds.zip` (834,536바이트), 2026-11-24 공식 페이지에서 받음
- 팩에 든 `License.txt`를 그대로 함께 넣었다: "License: (Creative Commons Zero, CC0)"
- CC0라 출처 표기 의무는 없지만, 출처를 남기려고 이 문서를 둔다.

## 넣은 것과 쓰는 곳

| 파일 | 쓰는 곳 (`SfxCue`) |
| --- | --- |
| `tick_004.ogg` | `CountdownTick`: 3, 2, 1 |
| `confirmation_001.ogg` | `CountdownGo`: START |
| `maximize_008.ogg` | `GaugeFull`: 게이지가 가득 찼을 때 |
| `confirmation_004.ogg` | `Result`: 결과 화면 |
| `back_003.ogg` | `UiClick`: 버튼 |
| `select_004.ogg` | `UiConfirm`: 시작, 계속, Retry 확인 |
| `back_002`, `back_004.ogg` | `UiBack`: 취소, 나가기 |
| `tick_002.ogg` | `DialogueNext`: 대사 넘김 |

원본 파일은 고치지 않았다. 음량, 높낮이, 동시 재생 수는 `Assets/Resources/SfxSettings.asset`에서 정한다.

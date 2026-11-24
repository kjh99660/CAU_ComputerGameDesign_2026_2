# Kenney Interface Sounds 일부 (CC0)

화면과 진행 효과음, 적의 공격 소리에 쓰는 외부 소리다. 팩 전체(100개)가 아니라 쓰는 파일 13개만 넣었다.

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
| `open_002`, `open_003.ogg` | `EnemyWindup`: 적이 노랗게 빛나며 공격을 준비할 때 올라가는 기계음 (적이 선 자리에서 재생) |
| `close_002`, `close_003.ogg` | `EnemyStrike`: 적의 공격이 휘둘러지는 순간 내려가는 쉭 소리 (적이 선 자리에서 재생) |

적 공격 소리는 파일 분석(길이, 주파수 변화, 음색 중심)으로 골랐다. `open_002`·`003`은 0.31초 동안 주된 주파수가 약 0.7 kHz에서 5~10 kHz까지 올라가며 점점 커지고 잡음이 섞인 기계적인 소리이고, `close_002`·`003`은 그 반대로 바로 시작해 내려가는 소리다. 음색 중심이 약 7~9 kHz라 주인공의 무거운 적중음(음색 중심 약 0.1~0.2 kHz)과 겹치지 않는다.

원본 파일은 고치지 않았다. 음량, 높낮이, 동시 재생 수는 `Assets/Resources/SfxSettings.asset`에서 정한다.

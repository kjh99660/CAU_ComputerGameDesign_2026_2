# Kenney Sci-fi Sounds 일부 (CC0)

전투 효과음과 배경 소음(앰비언스)에 쓰는 외부 소리다. 팩 전체(70개)가 아니라 쓰는 파일 9개만 넣었다.

## 출처와 라이선스

- 팩: Sci-Fi Sounds 1.0, 만든 곳 Kenney (www.kenney.nl)
- 페이지: https://kenney.nl/assets/sci-fi-sounds (License 칸 "Creative Commons CC0")
- 받은 파일: `kenney_sci-fi-sounds.zip` (5,875,104바이트), 2026-11-24 공식 페이지에서 받음
- 팩에 든 `License.txt`를 그대로 함께 넣었다: "License: (Creative Commons Zero, CC0)"
- CC0라 출처 표기 의무는 없지만, 출처를 남기려고 이 문서를 둔다.

## 넣은 것과 쓰는 곳

| 파일 | 쓰는 곳 (`SfxCue`) |
| --- | --- |
| `impactMetal_000`, `003`, `004.ogg` | `HitMetal`: 적중 때 겹치는 로봇 장갑의 금속음 |
| `explosionCrunch_000`, `001`, `003.ogg` | `EnemyDown`: 로봇이 부서지는 소리 |
| `lowFrequency_explosion_000`, `001.ogg` | `Special`: 필살기의 큰 폭발 |
| `spaceEngineSmall_002.ogg` | 배경 소음(앰비언스)의 원본. 음악 밑에 아주 작게 까는 기계 웅웅거림이다. `RebootSfxBuilder`가 이 소리의 끝부분을 앞부분에 크로스페이드해 끊김 없이 반복되는 `Assets/Sound/Ambience/ambience_city_hum.wav`를 만들고, 게임은 그 파일을 재생한다 |

`spaceEngineSmall_002`는 같은 팩의 5초짜리 엔진 소리들을 분석해 골랐다. 거칠기(샘플 간 급한 변화)가 작고 음량 흔들림이 1 dB 안쪽이며, 에너지의 약 90%가 120~500 Hz에 있어 적중음의 저음(60~120 Hz)과 음악의 베이스 밑으로 파고들지 않는다.

원본 파일은 고치지 않았다. 음량, 높낮이, 동시 재생 수는 `Assets/Resources/SfxSettings.asset`에서 정한다.

# CC0 소품 강조 캡처 (2026-11-18)

미리보기 씬 `Assets/Scenes/Main_RebootHub.unity`에 시험 배치한 CC0 소품 20개(`Env_RebootHub/ArenaProps_CC0`)를 연두색 테두리와 번호로 표시한 화면이다.

- 다른 물체에 가린 부분은 표시하지 않는다(실제 깊이로 판정).
- 벽 간판(16~20번)은 위에서 보면 두께가 없어 평면도(01)에 나오지 않는다.
- 연두색은 표시용이며 게임 화면에는 없다.

| 화면 | 시점 |
| --- | --- |
| `01_plan.jpg` | 정사영 평면도(위가 북쪽). 노란 원 15m, 빨간 원 25m는 적 스폰 거리 |
| `02_start_view.jpg` | 시작 화면(북쪽, 허브 방향) |
| `03_overview_from_south.jpg` | 남쪽 위에서 내려다본 조감 |
| `04_overview_from_north.jpg` | 북쪽 위에서 내려다본 조감(앞쪽은 허브 링) |
| `05_south_view.jpg` | 시작 위치에서 남쪽 |
| `06_east_view.jpg` | 시작 위치에서 동쪽 |
| `07_west_view.jpg` | 시작 위치에서 서쪽 |

| 번호 | 소품 | 원본 프리팹(팩) | 위치 | 보이는 화면 |
| --- | --- | --- | --- | --- |
| 1, 2 | 방패 바리케이드 | `BarierP_4_A` (Blue Base) | 북쪽 계단 위 데크, 허브 입구 양옆 | 01, 02, 03 |
| 3 | 컨테이너(파랑) | `ContainerC_B` (Cargo) | 북동 옥상 | 01~04, 06 |
| 4, 5 | 작은 탱크 | `WallPP_1_1_A` (Blue Base) | 북동 옥상, 컨테이너 옆 | 01~04, 06 |
| 6 | 컨테이너(주황 줄무늬) | `ContainerC_A` (Cargo) | 서쪽 옥상 | 01, 03, 04, 07 |
| 7 | 큰 탱크 | `PoleP_1_A` (Blue Base) | 서쪽 옥상 | 01, 03, 04, 07 |
| 8, 9 | 낮은 바리케이드 | `BarierP_5_A_001` (Blue Base) | 남쪽 계단 위 통로 양쪽 | 01, 03, 04, 05 |
| 10, 11 | 조명 기둥 | `PilarP_1_A` (Blue Base) | 남쪽 계단 꼭대기 양옆 | 01, 03, 04, 05 |
| 12, 13, 14 | 조명 기둥 | `PilarP_1_A` (Blue Base) | 광장 동쪽·북동 가장자리 턱 | 01, 04, 06 (13·14는 03에도) |
| 15 | 조명 기둥 | `PilarP_1_A` (Blue Base) | 광장 북서 가장자리 턱 | 01, 03, 04, 07 |
| 16 | 세로 홀로그램 간판 | `Neon_3` (Blue Base) | 북서 건물 동쪽 벽 | 02, 03, 04 |
| 17 | 세로 홀로그램 간판 | `Neon_3` (Blue Base) | 북동 건물 서쪽 벽 | 02, 03, 04, 06 |
| 18 | 가로 홀로그램 간판 | `Neon_1` (Blue Base) | 북서 건물 남쪽 벽 | 02, 03, 07 |
| 19, 20 | 홀로그램 패널 | `Neon_2` (Blue Base) | 남쪽 통로 양쪽 낮은 벽 | 03, 04, 05 |

다시 찍는 명령(프로젝트 폴더에서):

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -executeMethod RebootLevelDiagnostics.RunAll -rebootOut ./diag -rebootTag cc0hl -rebootHighlight Env_RebootHub/ArenaProps_CC0 -logFile diag.log
```

결과는 `./diag/cc0hl_*.png`와 번호·좌표·보이는 화면 목록 `./diag/cc0hl_highlight.md`다.

# 실습실·시연 PC 성능 측정 방법

과목의 설계 제한은 하나다. **실습실 PC나 시연 PC에서 최소 20 fps, 권장 60 fps.** 이 문서는 그 PC에서 성능 오버레이와 벤치마크 모드로 FPS를 재고 기록하는 방법을 적는다.

> **이 Mac(개발 기기)에서 잰 숫자는 시연 PC 숫자가 아니다.** 발표 자료에는 시연 PC(또는 실습실 PC)에서 플레이어 빌드로 잰 값만 쓰고, 어느 기기에서 잰 값인지 함께 적는다. Unity Editor 안에서 잰 값은 Editor 자체의 부하가 섞이므로 쓰지 않는다.

## 1. 준비: 플레이어 빌드

빌드는 Unity Editor 메뉴나 명령으로 만든다. 둘 다 `File > Build Profiles`에서 켜 둔 씬(2026-11-25부터 `Assets/Scenes/Main_NeonCity.unity` 하나. 복층 도시라 평지 아레나보다 조명·그림자가 무겁다)을 개발 빌드가 아닌 일반(Release) 빌드로 만든다.

| | 메뉴 | 명령(배치) | 결과 |
| --- | --- | --- | --- |
| Windows | `Tools > REBOOT Build > Build Windows (REBOOT.exe)` | `Unity -batchmode -projectPath <프로젝트> -executeMethod RebootBuild.BuildWindowsBatch -quit` | `Builds/Windows/REBOOT.exe`, `REBOOT_Data` 폴더, `run_benchmark.bat` |
| Mac | `Tools > REBOOT Build > Build Mac (REBOOT.app)` | `Unity -batchmode -projectPath <프로젝트> -executeMethod RebootBuild.BuildMacBatch -quit` | `Builds/Mac/REBOOT.app`, `run_benchmark.command` |

- Windows 빌드에는 이 Editor 버전(6000.3.24f1)에 **Windows Build Support (Mono)** 모듈이 있어야 한다(Unity Hub의 `Installs > 6000.3.24f1 > Add modules`). 모듈이 없으면 빌드를 시작하지 않고 그 사실과 설치 방법을 알린 뒤 끝난다(배치에서는 종료 코드 1).
- 2026-11-24에 확인했을 때 개발용 Mac의 6000.3.24f1에는 이 모듈이 설치되어 있었고, 이 Mac에서 Windows 빌드(`REBOOT.exe`, x86-64)가 만들어졌다. 다른 컴퓨터에서 빌드할 때는 모듈이 있는지 먼저 확인한다.
- 명령으로 빌드하면 실패할 때 종료 코드 1로 끝나고, 성공하면 로그에 결과 경로와 크기를 적는다.
- `Builds/` 폴더는 git에 올리지 않는다(`.gitignore`). 시연 PC로는 `Builds/Windows` 폴더를 통째로 복사한다. `REBOOT.exe`만 따로 옮기면 실행되지 않는다. 함께 생기는 `CGD_BurstDebugInformation_DoNotShip` 폴더는 옮기지 않아도 된다.
- 빌드하면 Unity가 설정 파일 몇 개를 빌드 대상에 맞춰 다시 쓴다. 이 Mac에서 Mac 빌드를 했을 때는 `Assets/Settings/PC_RPAsset.asset`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/UnityConnectSettings.asset`이 바뀌었다. 빌드 때문에 생긴 변경이므로 커밋하지 말고 되돌린다(`git checkout -- <파일>`).

## 2. 화면에서 바로 보기: 성능 오버레이

게임 중 언제든 **F3**을 누르면 화면 왼쪽 가운데에 성능 표시가 나타나고, 다시 누르면 사라진다. 처음에는 숨겨져 있다. 게임 상태, 점수, 입력, 적이 나오는 방식에는 영향을 주지 않으며, 표시 영역은 클릭을 받지 않는다. 그래서 오버레이를 켠 채 잰 값도 평소 판의 값이다.

- Mac 노트북 키보드에서는 F3~F5가 밝기·미션 컨트롤 같은 기능 키로 먼저 동작한다. **fn 키를 누른 채** F3, F4, F5를 누른다.

| 줄 | 뜻 |
| --- | --- |
| `FPS 143.2  (last 1 s)` | 최근 1초의 평균 FPS(그 1초의 프레임 수 ÷ 시간) |
| `Frame 6.98 ms avg, 12.40 ms worst` | 최근 1초의 평균 프레임 시간과 가장 긴 프레임 시간 |
| `Enemies 87 / 100 cap` | 지금 활성 적 수 / 적용 중인 적 상한. 실험용 상한이면 `experiment`가 붙는다 |
| `Neighbour search: spatial grid` | 적이 주변 적을 찾는 방법. `spatial grid`(공간 분할, 기본) 또는 `brute force`(전수, 비교용) |
| `Candidates 1885 per frame (18.9 per enemy)` | 한 프레임에 적들이 살펴본 주변 적 후보 수와 적 한 명당 후보 수(0.25초마다 평균) |
| `AI update 0.226 ms per frame` | 적 AI 갱신에 걸린 시간(오버레이가 보이는 동안만 잰다. 시간을 재는 일은 게임 진행을 바꾸지 않는다) |
| `Draw calls 412` | 최근 몇 프레임의 평균 Draw Call 수. Unity가 이 값을 주지 않는 빌드에서는 `n/a` |
| `State Battle` | 게임 상태. 벤치마크 중이면 `BENCHMARK`가 붙는다 |
| `Round recording on, 1234 frames` | 라운드 기록 상태(`on` 기록 중, `held` 일시정지 등으로 쉼, `written` 저장함, `NOT SAVED` 파일을 쓰지 못함, `off` 판 밖, `disabled` 이 실행에서는 기록하지 않음)와 모은 프레임 수 |
| `Last round: avg ..., 1% low ..., min ... FPS` (두 줄) | 이 실행에서 마지막으로 끝난 판의 요약(FPS가 100 이상이면 소수점 없이). 파일을 쓰지 못했으면 둘째 줄이 `NOT SAVED (see the log)`다 |

오버레이가 보일 때만 다음 두 키가 동작한다(모르고 눌러 설정이 바뀌는 일을 막기 위해서다).

- **F4**: 적 이웃 검색을 공간 분할 ↔ 전수로 바꾼다. 판은 멈추지 않고 이어진다.
- **F5**: 적 상한을 100 → 200 → 300 → 100 순서로 바꾼다. 200명과 300명은 성능 실험에서만 쓰는 설정이다(일반 플레이는 100명, AGENTS.md 13절). 상한을 낮춰도 이미 나와 있는 적은 그대로 두고 새로 나오지 않을 뿐이다. 2026-11-24의 적 생성 설정(처음 5명, 그 뒤 1초에 5명씩)으로는 300명이 다 차려면 판이 시작되고 약 60초가 걸린다. 생성 규칙이 바뀌면 이 시간도 달라진다.
- 실험용 상한(200·300명)을 건 판은 성능 실험 전용 구성이라, 적이 나오는 방식이 일반 판과 다를 수 있다(상한까지 채우는 것이 목적이다). 그래서 200·300명 결과는 같은 상한끼리(공간 분할 대 전수)만 견주고, 20/60 fps 판정에는 100명 결과만 쓴다.
- Retry, Pause의 Quit, 결과 화면의 Retry를 하면 공간 분할과 상한 100명으로 돌아간다.

## 3. 라운드 기록 파일

전투(Battle)와 마무리(Finishing) 동안 매 프레임의 실제 시간과 활성 적 수를 모은다. 일시정지 동안은 쉬고, Retry나 Quit으로 판을 다시 시작하면 그 판의 기록은 버린다. 판이 결과 화면(Result)에 닿으면 파일 두 개를 쓴다. 플레이어를 판 도중에 닫으면 거기까지의 기록을 쓴다(Editor에서 Play Mode를 끝낼 때는 쓰지 않는다).

| OS | 폴더 |
| --- | --- |
| Windows | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CGD\perf\` |
| Mac | `~/Library/Application Support/DefaultCompany/CGD/perf/` |

폴더 이름의 `DefaultCompany`와 `CGD`는 Player Settings의 회사 이름과 제품 이름이다. 바꾸면 폴더도 바뀐다(빌드할 때 만드는 실행 파일은 그 빌드의 이름을 따라간다).

| 파일 | 내용 |
| --- | --- |
| `round_20261124_215703.txt` | 사람이 읽는 요약. 평균·1% low·최저 FPS, 프레임 수, 적 수, 기기 정보, 주의 사항 |
| `round_20261124_215703.csv` | 같은 내용의 머리줄 + 한 줄. 여러 번 잰 파일을 표 프로그램에서 이어 붙이기 좋다 |

- **평균 FPS** = 기록한 프레임 수 ÷ 그 프레임들의 시간 합.
- **1% low FPS** = 가장 느린 1% 프레임(150프레임이면 2프레임처럼 올림, 최소 1프레임)의 수 ÷ 그 프레임들의 시간 합. 끊김이 얼마나 심한지 본다.
- **최저 FPS** = 가장 긴 프레임 하나의 FPS. 첫 등장 효과의 셰이더 준비 같은 한 번의 끊김에 크게 흔들리므로 참고만 한다.
- 기록에는 기기 모델, CPU, GPU, 메모리, OS, 화면 크기와 창 모드, 주사율, 품질 단계, 수직 동기, 앱 버전, Unity 버전, Editor 여부를 함께 적는다. 컴퓨터 이름, 사용자 이름, 파일 경로는 적지 않는다.
- 요약 끝의 `Note:` 줄은 그 기록을 그대로 쓰면 안 되는 까닭을 알려 준다. 창이 포커스를 잃었음(`lost focus`), Editor에서 잼, 실험용 적 상한을 씀, 판 도중에 이웃 검색 방식을 바꿈(`search_mode`가 `mixed`) 같은 경우다.
- 폴더를 만들거나 파일을 쓰지 못하면(권한, 디스크 공간 등) 오버레이에 `NOT SAVED`가 나온다. 그 판의 요약과 실패 이유는 플레이어 로그(Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CGD\Player.log`, Mac `~/Library/Logs/DefaultCompany/CGD/Player.log`)에 남는다.
- A/B 측정 도구(`-rebootEnemyAb`, 7절)로 실행한 플레이어는 라운드 기록을 하지 않는다(오버레이에 `disabled`). 그 도구가 돌리는 판은 게임 판이 아니기 때문이다.

## 4. 벤치마크 모드로 재기 (권장)

사람이 플레이하면 판마다 적의 움직임이 달라진다. 비교할 수치는 벤치마크 모드로 잰다.

1. 빌드 폴더의 `run_benchmark.bat`(Windows)을 두 번 누른다. Mac은 `run_benchmark.command`.
2. 게임이 뜨면 시작 화면, 도입 대화, 카운트다운을 게임과 같은 요청(Request)으로 스스로 넘긴다. 오버레이가 켜진 채로 100초 전투와 2.5초 마무리가 진행된다. 플레이어는 움직이지 않고 적이 몰려든다.
3. 결과 화면에 닿으면 기록을 쓰고 게임이 스스로 닫힌다. 실행 창(명령 창 또는 터미널)에 방금 쓴 요약이 나온다. 이번 실행에서 새 기록이 생기지 않았으면(전투가 시작되기 전에 창을 닫았거나 기록을 쓰지 못한 경우 등) 예전 요약을 보여 주지 않고 `No new round recording was written by this run.`이라고 알린다. 그때는 다시 잰다. 전투 도중에 창을 닫으면 그때까지의 기록이 쓰이지만 요약에 `closed in the middle of the round`가 붙는 반쪽 판이므로 그 값도 적지 않는다.
4. 끝날 때까지 키보드와 마우스를 건드리지 않고 게임 창을 앞에 둔다. 창이 포커스를 잃어도 판은 끝까지 가지만 요약에 그 사실이 남는다.

선택 인자를 뒤에 붙이면 실험 설정으로 잰다.

```text
run_benchmark.bat -rebootBenchmarkCap 300
run_benchmark.bat -rebootBenchmarkCap 300 -rebootBenchmarkSearch brute
REBOOT.exe -rebootBenchmark -rebootBenchmarkCap 200          (실행 파일을 직접 실행할 때)
open -W -n REBOOT.app --args -rebootBenchmark -rebootBenchmarkCap 200      (Mac)
```

- `-rebootBenchmarkCap <수>`: 실험용 적 상한(200 또는 300). 게임 설정(100명)보다 클 때만 쓰이고 300명을 넘지 않는다.
- `-rebootBenchmarkSearch brute`: 적 이웃 검색을 전수 방식으로 돌린다(기본은 공간 분할).
- 인자 없이 실행한 게임은 평소와 같다. `-rebootBenchmark`는 Editor에서는 쓰지 않는다.
- 플레이어가 서 있기만 하므로 점수와 처치 수는 의미가 없다. 성능만 본다.

## 5. 무엇을 적나

PC마다 아래를 적는다. 요약 파일(`round_*.txt`)에 대부분 들어 있으니 그대로 옮기고, 장소와 자리 번호는 직접 적는다.

| 항목 | 예시 | 어디서 |
| --- | --- | --- |
| 장소·자리(또는 시연 PC 이름) | 실습실 ○○○호 7번 | 직접 |
| CPU / 스레드 수 | Intel Core i5-12400 / 12 | `Device:` 줄 |
| GPU / 그래픽 API | NVIDIA GeForce RTX 3060 / Direct3D11 | `GPU:` 줄 |
| 메모리 | 16384 MB | `Device:` 줄 |
| OS | Windows 11 | `OS:` 줄 |
| 화면 크기, 창 모드, 주사율 | 1920x1080 FullScreenWindow, 60 Hz | `Screen:` 줄 |
| 품질 단계, 수직 동기 | PC, vSync 0 | `Screen:` 줄 |
| 평균 / 1% low / 최저 FPS | | `Average FPS`, `1% low FPS`, `Minimum FPS` |
| 프레임 수, 적 평균 / 최대 | | `Frames`, `Enemies` |

측정 순서:

1. **기본(필수)**: 적 100명, 공간 분할. `run_benchmark.bat`를 **세 번** 실행해 세 판의 값을 모두 적는다(첫 판은 셰이더 준비 때문에 최저 FPS가 낮게 나올 수 있다).
2. **선택**: 적 200명과 300명을 공간 분할과 전수로 한 번씩 잰다(`-rebootBenchmarkCap 200`, `-rebootBenchmarkCap 200 -rebootBenchmarkSearch brute`, 300도 같은 방식).

기록 표 예시:

| 장소·PC | 적 상한 | 이웃 검색 | 평균 FPS | 1% low FPS | 최저 FPS | 적 평균/최대 | 비고 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | 100 | 공간 분할 | | | | | 1회 |
| | 100 | 공간 분할 | | | | | 2회 |
| | 100 | 공간 분할 | | | | | 3회 |
| | 300 | 공간 분할 | | | | | 선택 |
| | 300 | 전수 | | | | | 선택 |

판정: 적 100명에서 **평균 FPS와 1% low FPS가 모두 20 이상이면 하한을 만족**하고, 60 이상이면 권장을 만족한다. 최저 FPS는 끊김이 있었는지 보는 참고 값이다.

## 6. 발표에서 공간 분할 효과를 바로 보여 주기

1. 판을 시작하고 F3으로 오버레이를 켠 뒤 F5를 두 번 눌러 상한을 300으로 둔다.
2. 적이 다 찰 때까지(약 60초) 기다린다. `Candidates`와 `AI update`가 적 수에 따라 늘어난다.
3. F4로 전수 방식으로 바꾸면 후보 수가 적 수의 제곱(300명이면 프레임당 90,000)으로 뛰고 `AI update` 시간이 늘어난다. 다시 F4를 누르면 공간 분할로 돌아온다.
4. 후보 수는 세는 값이라 기기와 상관없이 같다. AI 시간과 FPS는 기기마다 다르므로, 발표에 숫자로 쓸 때는 그 PC에서 잰 값이라고 밝힌다.

## 7. 주의할 점

- 노트북은 전원을 연결하고 고성능 전원 모드로 둔다. 다른 프로그램(브라우저, 화면 녹화 등)은 닫는다. 화면 녹화를 켜고 재면 그 사실을 비고에 적는다.
- 품질 단계 `PC`는 수직 동기가 꺼져 있다(`vSync 0`). 그래서 FPS가 모니터 주사율(60 Hz 등)보다 높게 나올 수 있다. 화면에 실제로 보이는 장면 수는 주사율을 넘지 않는다.
- 발표와 같은 화면 크기와 창 모드로 잰다. 빌드는 처음 실행할 때 전체 화면으로 뜬다.
- 같은 PC에서도 실행마다 수치가 조금씩 다르다. 세 번 잰 범위를 적는다.
- 개발용 Mac 수치, Editor 수치, 배치 모드(`-batchmode`) 수치는 시연 PC 수치로 쓰지 않는다. 배치 모드는 화면에 그리지 않으므로 FPS 자체가 의미 없다.
- 공간 분할 A/B 비교를 한 판 안에서 프레임 단위까지 자세히 재는 도구는 따로 있다(`Docs/Perf/EnemyAb/README.md`, 인자 `-rebootEnemyAb`). 두 인자를 함께 주면 `-rebootBenchmark`는 무시되고, 그 실행에서는 라운드 기록도 하지 않는다.

## 8. 이 Mac에서 확인한 것

- 자동 테스트: EditMode(통계 정의, 글 버퍼, 기록 파일 모양, 인자, 실행 파일 내용, 오버레이의 AI 시간 측정 스위치, 빌드 메뉴 경로)와 PlayMode(실제 씬에서 오버레이, F3~F5 키 입력, 검색 방식 전환과 Reset, 상한 순환과 Reset, 라운드 기록 쓰기와 버리기, 기록을 끈 실행, 쓰지 못한 기록의 표시, 벤치마크 한 판). PlayMode 테스트의 벤치마크는 라운드 길이를 줄여 Editor에서 돌린 것이다. 오버레이와 벤치마크가 A/B 측정 판 표시(`MeasureTime`, `ShadowVerify`)와 실험용 상한을 켜지 않는다는 것도 확인한다.
- `run_benchmark.command`의 "이번 실행이 새로 쓴 요약만 보여 주기"는 가짜 `open`과 임시 홈 폴더로 세 경우(결과 폴더가 없음, 예전 요약만 있고 새 기록이 없음, 새 기록이 생김)를 돌려 봤다. 게임은 실행하지 않았다. `run_benchmark.bat`는 Windows가 없어 내용만 확인했다.
- `RebootBuild.BuildMacBatch`로 Mac 플레이어(Intel과 Apple silicon 공용 앱, 약 236MB)와 `run_benchmark.command`가 만들어지는 것. 만든 앱과 `run_benchmark.command`는 실행해 보지 않았다.
- `RebootBuild.BuildWindowsBatch`로 Windows 플레이어(`REBOOT.exe`와 같은 폴더의 파일들, 약 219MB)와 CRLF 줄 끝의 `run_benchmark.bat`가 만들어지는 것. 모듈이 없을 때의 경로(설치 방법을 알리고 아무것도 빌드하지 않은 채 실패로 끝남)는 모듈이 없다고 가정한 확인용 코드로 따로 돌려 봤다.
- **Windows에서 `REBOOT.exe`와 `run_benchmark.bat`를 실행해 보지는 못했다.** 시연 PC에서 처음 실행할 때 결과 폴더에 `round_*.txt`가 생기는지 먼저 확인한다.

## 9. 관련 코드

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Runtime/Perf/PerfRuntime.cs` | 오버레이 키(F3~F5), 라운드 기록, 벤치마크 진행. `CoreBootstrap`이 붙인다 |
| `Assets/Scripts/Runtime/Perf/PerfOverlayView.cs` | 실행 중에 만드는 오버레이 Canvas와 글자 |
| `Assets/Scripts/Runtime/Perf/RoundFrameStats.cs` | 평균·1% low·최저 FPS 계산(Unity와 무관한 순수 코드) |
| `Assets/Scripts/Runtime/Perf/RoundPerfRecord.cs` | 기록 CSV와 요약 글, 결과 폴더(`PerfOutput`) |
| `Assets/Scripts/Runtime/Perf/PerfBenchmark.cs` | 벤치마크 인자와 `run_benchmark` 실행 파일 내용 |
| `Assets/Scripts/Runtime/Perf/PerfTextBuffer.cs` | 오버레이 글을 문자열 할당 없이 쓰는 버퍼 |
| `Assets/Editor/Build/RebootBuild.cs` | 빌드 메뉴와 배치 명령 |

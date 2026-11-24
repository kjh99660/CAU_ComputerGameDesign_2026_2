# RG Poly Sci-Fi Arena 조각 (CC0)

레벨 장식에 쓰는 외부 에셋이다. 팩 전체가 아니라 장식에 쓰는 프리팹과 그 프리팹이 참조하는 파일만 넣었다.

## 출처와 라이선스

| 폴더 | 원본 팩 | 판매 페이지 | 라이선스 표시 |
| --- | --- | --- | --- |
| `BlueBase/` | Sci-Fi Arena Battle - Blue Base (RG Poly) | https://rg-poly.itch.io/sci-fi-arena-blue-base | 설명에 "CC0 Licensed. Free for personal and commercial use" |
| `Cargo/` | Sci-Fi Battle Arena - Cargo - Unity URP (RG Poly) | https://rg-poly.itch.io/sci-fi-battle-arena-cargo | "Asset license" 칸에 Creative Commons Zero v1.0 Universal |

- 받은 경로: itch.io "Creator Bundle: Volume 1"(RG Poly, EmaceArt)로 산 파일. 원본 파일 이름은 `Sci-Fi+Battle+Arena+-+Blue+Base_6000.1.0f1.unitypackage`, `SciFiBattleArenaCargo-2025-29-5--19-10-06.unitypackage`.
- 확인한 날: 2026-11-18. 라이선스는 판매 페이지 표시 기준이며 판매자에게 따로 묻지 않았다.
- 같은 판매자의 다른 팩에 든 `License.txt`는 CC0라고 쓰면서 "단독 재배포 금지" 문장을 덧붙인 경우가 있다. 그래서 팩을 통째로 넣지 않고 레벨에 쓰는 조각만 넣었다.
- CC0라 출처 표기 의무는 없지만, 출처를 남기려고 이 문서를 둔다.

## 넣은 것

- 프리팹 16개
  - Blue Base 14개: `BarierP_4_A`, `BarierP_5_A_001`, `BarierP_Round_1_A`, `Long_Barier`, `PilarP_1_A`, `PoleP_1_A`, `VentP_1_A`, `WallPP_1_1_A`, `Neon_1`~`Neon_4`, `Neon_Circle_1`, `Neon_Circle_2`
  - Cargo 2개: `ContainerC_A`, `ContainerC_B`
- 위 프리팹이 참조하는 메시, 머티리얼, 텍스처, 셰이더 그래프. 모두 합쳐 파일 52개, 약 12MB다.
- 원본 `.meta`를 그대로 써서 GUID와 내부 참조를 유지했다. 폴더 `.meta`는 Unity가 새로 만들었다.
- 바꾼 것은 하나다. FBX 10개의 가져오기 설정 `Generate Colliders`를 껐다(`addColliders: 1 → 0`). 장식이라 충돌체를 두지 않는 레벨 규칙 때문이며, 레벨 빌더(`RebootHubEnvironmentBuilder.ArenaProps.cs`)가 실행할 때마다 확인한다.

## 쓰는 곳

`Assets/Scenes/Main_RebootHub.unity`(미리보기 씬)의 `Env_RebootHub/ArenaProps_CC0`에 20개를 놓는다. 위치와 규칙은 `Docs/LevelDesign/REBOOT_HUB_LEVEL_DESIGN.md` 3절에 있다.

## 뺀 것

- 데모 씬과 조명 데이터.
- 스크립트(`Rotator.cs`, `CustomRotator.cs`).
- VFX Graph 파일(`.vfx`). 프로젝트에 VFX Graph 패키지가 없다.
- 팩의 `Settings` 폴더. Blue Base의 `Settings` 폴더 GUID가 프로젝트의 `Assets/Settings`와 같아, 그대로 가져오면 데모용 설정 파일이 프로젝트 설정 폴더에 섞인다.

두 팩에 같은 GUID로 들어 있는 파일은 Blue Base 쪽 하나만 넣었다. 그중 `Neons.mat`, `HoloShield_A.mat`은 두 팩의 내용이 달라 Blue Base 판을 썼다.

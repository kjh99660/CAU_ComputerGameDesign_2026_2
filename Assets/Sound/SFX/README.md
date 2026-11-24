# 효과음 (SFX)

효과음은 대부분 녹음된 CC0 소리이고, 이 폴더에는 코드로 만든 두 겹만 있다.

| 어디 | 무엇 | 출처 |
| --- | --- | --- |
| `Assets/ThirdParty/Kenney_ImpactSounds/` | 둔탁한 적중, 피격, TIME UP 종소리 | Kenney, CC0 (폴더 README) |
| `Assets/ThirdParty/Kenney_SciFiSounds/` | 로봇 장갑 금속음, 로봇이 부서지는 소리, 필살기 폭발 | Kenney, CC0 (폴더 README) |
| `Assets/ThirdParty/Kenney_InterfaceSounds/` | 버튼, 카운트다운, 게이지, 결과 | Kenney, CC0 (폴더 README) |
| `Assets/ThirdParty/artisticdude_Swishes/` | 휘두르기, 회피, 스킬 돌진 | artisticdude, CC0 (폴더 README) |
| 이 폴더 `sfx_hit_sub_*.wav` | 무거운 공격과 필살기 밑에 까는 깊은 저음 "쿵" | 저장소 코드로 만듦 |
| 이 폴더 `sfx_player_glitch_*.wav` | 주인공 피격 밑에 작게 까는 디지털 글리치 | 저장소 코드로 만듦 |

- 만드는 코드: `Assets/Editor/Audio/RebootSfxSynth.cs` (두 겹의 레시피), `Assets/Editor/Audio/RebootSfxBuilder.cs` (어떤 소리를 어디에 쓸지, 임포트 설정, 설정 연결)
- 다시 만들기: Unity 메뉴 `Tools/REBOOT Audio/Generate Sound Effects`. 고정 시드를 써서 다시 만들어도 같은 소리가 나온다(바뀐 파일만 다시 쓴다).
- 음량, 높낮이, 동시 재생 수: `Assets/Resources/SfxSettings.asset`. 값은 모두 플레이 테스트로 맞출 임시값이다.
- 재생: `Assets/Scripts/Runtime/Audio/SfxRuntime.cs`. 적중은 둔탁한 타격 + 금속음 + 부서지는 소리를 겹치고, 무거운 공격에는 저음을 더한다. 전투 소리는 Battle·Finishing에서만 나고, Pause에서는 멈췄다가 이어지며, Retry와 대화 화면에서는 끊긴다.

다른 소리로 바꾸려면 `SfxSettings`에서 해당 항목의 clip을 바꾼다. 새 파일을 넣을 때는 CC0처럼 공개 저장소에 둘 수 있는 라이선스인지 먼저 확인하고 `Assets/ThirdParty/<이름>/README.md`에 출처를 적는다.

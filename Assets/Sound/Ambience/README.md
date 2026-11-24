# 배경 소음 (앰비언스)

음악 밑에 아주 작게 까는 도시의 기계 웅웅거림이다.

| 파일 | 무엇 | 출처 |
| --- | --- | --- |
| `ambience_city_hum.wav` | 끊김 없이 반복되는 약 4.5초짜리 웅웅거림 | Kenney Sci-Fi Sounds의 `spaceEngineSmall_002.ogg` (CC0, `Assets/ThirdParty/Kenney_SciFiSounds/`)를 저장소 코드로 가공 |

- 만드는 코드: `Assets/Editor/Audio/RebootSfxBuilder.cs`(`BuildAmbience`)와 `Assets/Editor/Audio/RebootSfxSynth.cs`(`MakeLoop`). 원본의 끝 0.5초를 처음 부분에 크로스페이드한다. 이어 붙이는 지점은 끝부분이 처음 부분과 가장 닮은 곳(상관이 가장 큰 곳)으로 고르고, 크로스페이드 이득을 그 상관에 맞춰 보정해 겹치는 동안 소리가 줄거나 커지지 않게 한다. 그래서 반복 지점에서 딸깍 소리가 나지 않는다.
- 다시 만들기: Unity 메뉴 `Tools/REBOOT Audio/Generate Sound Effects`. 같은 원본에서는 같은 파일이 나온다(바뀌었을 때만 다시 쓴다).
- 임포트: 압축하지 않은 PCM, Decompress On Load. 압축하면 반복 지점이 샘플 단위로 맞지 않을 수 있다.
- 재생: `Assets/Scripts/Runtime/Audio/SfxAmbience.cs`(`SfxRuntime`이 만든다). Battle·Finishing에서 소리를 내고, Ready·대화·카운트다운·결과에서는 그 절반, Pause에서는 그 자리에 멈췄다가 이어지며, Retry하면 처음부터 다시(소리 없이 시작해 서서히 커지며) 시작한다.
- 음량: `Assets/Resources/SfxSettings.asset`의 `ambienceVolume`(임시값 0.12, 마스터 음량 0.7을 곱함). 파일 분석으로 잰 평균 음량(RMS)이 Battle에서 약 -34 dBFS로, 배경 음악(씬의 `Sound` 오브젝트, 음량 0.5, 처음 60초 평균 약 -17 dBFS)보다 약 17 dB 작다. 값은 플레이 테스트로 맞출 임시값이다.

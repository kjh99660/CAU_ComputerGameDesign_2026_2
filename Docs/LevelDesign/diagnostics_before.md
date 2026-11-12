# REBOOT level diagnostics (before)
Scene: Assets/Scenes/Main.unity
Unity: 6000.3.24f1, Graphics: Metal

## Root objects
- Main Camera active=True children=0 renderers=0 colliders=0 bounds=-
- Directional Light active=True children=0 renderers=0 colliders=0 bounds=-
- Global Volume active=True children=0 renderers=0 colliders=0 bounds=-
- Map active=True children=1 renderers=733 colliders=739 bounds=(-45.3, -2.3, 12)..(-2.7, 2, 111.9)
  - Center_Map active=True children=4 colliders=739 bounds=(-45.3, -2.3, 12)..(-2.7, 2, 111.9)
    - Area_A children=1 colliders=628 bounds=(-45.3, -2.3, 12)..(-2.7, 2, 109.3)
    - Area_Passage_A_to_B children=2 colliders=111 bounds=(-25.3, -2.3, 68)..(-22.7, 0, 111.9)
    - Area_B children=2 colliders=0 bounds=-
    - PlayerSpawnPoint children=0 colliders=0 bounds=-
- Manager active=True children=5 renderers=0 colliders=0 bounds=-
- Sample active=False children=1 renderers=83 colliders=93 bounds=(3.1, -14.5, 24)..(34.7, -9.9, 40.8)
- UICanvas active=True children=8 renderers=0 colliders=0 bounds=-
- EventSystem active=True children=0 renderers=0 colliders=0 bounds=-
- Player active=True children=6 renderers=6 colliders=1 bounds=(-24.8, -2.3, 41.5)..(-23.1, -0.2, 42.7)
- BackGround active=True children=942 renderers=1246 colliders=1096 bounds=(-301.4, -28.6, -310.3)..(193.5, 286.3, 291.7)

## Rendering
Skybox: M_LP_SciFi_Skybox, ambient mode Skybox, ambient RGBA(0.212, 0.227, 0.259, 1.000), intensity 1
Fog: False ExponentialSquared color RGBA(0.500, 0.500, 0.500, 1.000) density 0.01 start 0 end 300
Pipeline: PC_RPAsset
- Light Directional Light Directional color RGBA(1.000, 1.000, 1.000, 1.000) intensity 2 shadows Soft rot (50, 330, 0)
- Volume Global Volume global=True profile=Assets/Settings/SampleSceneProfile.asset
  - Tonemapping active=True
  - Bloom active=True
  - MotionBlur active=False
  - Vignette active=True
- Camera Camera (3) fov 60 near 0.3 far 1000 clear Skybox
- Camera Camera (2) fov 60 near 0.3 far 1000 clear Skybox
- Camera Camera fov 60 near 0.3 far 1000 clear Skybox
- Camera Camera (1) fov 60 near 0.3 far 1000 clear Skybox
- Camera Main Camera fov 60 near 0.3 far 1000 clear Skybox

## Spawn analysis (enemy spawn 15-25m, absolute y=0)
PlayerSpawnPoint world: (-24, -2, 42) yaw 0
Floor under player spawn: -0.51
Player-reachable sample cells (1.5m, collision only, no gravity): 546, bounds (-43.5, -2, 28.5) .. (-4.5, -2, 66)
### From PlayerSpawnPoint: samples 64
- on floor at spawn height: 7 (10.9%)
- no floor (outside map): 0 (0.0%)
- floor at another height (other level / under geometry): 57 (89.1%)
- overlapping solid collider at spawn: 7 (10.9%)
- straight path to player crosses solid collider (enemy clips through, AI has no avoidance): 7 (10.9%)
### From all reachable cells: samples 17472
- on floor at spawn height: 880 (5.0%)
- no floor (outside map): 16 (0.1%)
- floor at another height (other level / under geometry): 16576 (94.9%)
- overlapping solid collider at spawn: 1595 (9.1%)
- straight path to player crosses solid collider (enemy clips through, AI has no avoidance): 2352 (13.5%)

Captures written with tag 'before'.

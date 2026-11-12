# Enemy spawn position fix

## Reproduced causes

1. EnemyAgent.Activate wrote Transform.position but did not teleport the interpolated
   Rigidbody. Main requested (-5.706, -2, 27.445) relative to the player at
   (-24, -2, 42), but Rigidbody.position remained (0, 0, 0). By Ready, all initial
   enemy roots and bodies were at the world origin, about 48.374 units from the
   player in XZ. The same stale pose can be a previous elevated knockback/death
   position when an instance is reused. This is a pose synchronization defect,
   not a change to the configured random spawn radius or a mesh offset.
2. SpawnOne added the player's entire current position, including Y, to the spawn
   offset. Moving a test target from Y=5 to Y=85 caused the next wave to spawn at
   Y=85 (87.5 with spawnHeight=2.5). Kinematic chasing enemies never fall back down.

The temporary trace logs were removed from source after diagnosis. Reproductions
are recorded in Logs/enemy-spawn-trace.log and Logs/spawn-before-fix.xml.

## Changes

- EnemyAgent.Activate synchronizes Rigidbody.position/rotation with the requested
  Transform pose after animation initialization. Both fresh and reused bodies
  start at the new pose, preventing later interpolation from restoring an old pose.
- EnemyRuntime captures the round spawn Y once, after player initialization/reset,
  as the restored player's Y + EnemySettings.spawnHeight. Waves follow current
  player XZ only. Reset captures the new restored baseline again.
- EnemySettings documents spawnHeight's baseline in its Inspector tooltip.
- Minimum/maximum spawn radius remains 15/25 in the existing settings asset.
  RNG draw order, spawn interval, count, player targeting, Tick validation, pool
  ownership, physics pause ownership and the earlier animation/VFX changes remain.

The fixed spawn plane matches the existing planar AI. This change does not add
multi-floor navigation, raycasts to building rooftops, or a terrain grounding system.

## Automated validation

EnemySpawnPositionTests reproduces the elevation bug before the fix (both cases
failed with actual Y=85/87.5). After the fix it verifies non-origin owner/target
positions, radius around the moving player, height offsets, and Reset/pool reuse.

EnemySpawnSceneTests loads Main and checks the actual rigged prefab:

- Initial Ready enemies are within the configured XZ radius and spawn height.
- New waves follow a moved player XZ without inheriting an 80-unit Y change.
- The renderer remains near its root; Animator/Physics do not relocate the root
  during later frames.
- An enemy killed at a 100-unit elevated Rigidbody position is pooled and reused
  at the correct new spawn pose, including after a FixedUpdate.

Commands (Unity 6000.3.24f1):

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/enemy-spawn-editmode.xml -logFile Logs/enemy-spawn-editmode.log
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults Logs/enemy-spawn-playmode.xml -logFile Logs/enemy-spawn-playmode.log
```

Final results: Edit Mode 52/52 passed; Play Mode 12/12 passed. No C# compiler
errors or warnings were reported. git diff --check passed. The unrelated Unity
TimeManager serialization migration was restored after the tests.

## Manual confirmation

Play Main and inspect several waves while moving and stepping onto raised objects.
At Ready, enemies should form the configured ring around the player instead of
clustering at the world origin. After a strong knockback, wait for despawn and a
new wave; pooled enemies must reappear at the round spawn plane. Pause/Retry and
repeat to confirm the baseline is recaptured at the restored start position.
Visual gameplay confirmation and multi-floor navigation are not automated here.

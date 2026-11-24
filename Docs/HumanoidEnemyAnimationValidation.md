# Humanoid enemy skeletal animations

The gameplay EnemyPrototype now uses the rigged EmenyHumanoid FBX skin. The existing
prefab GUID, root EnemyAgent/Rigidbody/BoxCollider, root scale, scene spawn reference,
and enemy gameplay rules are retained. The teammate's EnemyHumanoid prefab and the
source FBX/import settings are unchanged.

## Motions

Five clips and HumanoidEnemy.controller are in Assets/Animations/Enemy/Humanoid:

- Idle: relaxed A-pose arms with breathing/spine and head motion (loop).
- Chase: alternating legs, knee/foot flexion, opposite arm swing and torso lean (loop).
- Attack: right-hand windup, forward strike at 50%, and recovery.
- Knockback: spine/head recoil, raised arms and bent legs; existing Rigidbody handles motion.
- Dying: hip rotation and lowering, folded legs and slumped upper body.

These are custom Transform bone curves authored against this model's rest pose,
not downloaded motion capture or Humanoid muscle clips. The root Animator has no
Avatar; the imported FBX retains its valid Humanoid Avatar for future retargeting.
Only one Animator drives the entire skin, with root motion disabled. Every clip
keys all Mixamo bone rotations/positions to avoid state and pooled-pose leakage.

Attack, knockback and death lengths are read from EnemySettings when generating.
Gameplay timing is unchanged. Idle duration (2s), gait duration (0.65s), joint angles
and death lowering are provisional animation styling in the builder. Regenerate
after changing durations in EnemySettings. Existing EnemyAgent state changes,
Pause/Resume and Rebind on pool return drive the new controller unchanged.

## Rebuild and preview

Tools > Game Demo Enemy > Build Humanoid Enemy regenerates assets in place without
replacing existing GUIDs. Build Prototype Enemy now selects this rigged model when
available. Validate Humanoid Enemy checks skin/bind poses, one Animator and curve
paths. Export Humanoid Pose Preview writes Logs/humanoid-enemy-poses.png; columns
are Idle, Chase, Attack, Knockback, Dying, with front and side views. The preview
was rendered and inspected; the original A-pose arm offsets were corrected.

Batch commands (Unity 6000.3.24f1, project closed in the Editor):

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -executeMethod GameDemoHumanoidEnemyBuilder.BuildAll -quit -logFile Logs/humanoid-enemy-build.log
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/humanoid-enemy-editmode.xml -logFile Logs/humanoid-enemy-editmode.log
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults Logs/humanoid-enemy-playmode.xml -logFile Logs/humanoid-enemy-playmode.log
```

Edit Mode: 50/50 passed, including actual baked-mesh deformation for each clip,
loop seams, curve targets, weighted skin and source Avatar validity. Play Mode adds
actual leg motion, Pause/Resume and restoration to Idle after death/pool reuse.
Final Play Mode run: 11/11 passed. During an earlier full run the existing
AcceptedHitUpdatesHudAndRetryClearsRound attack test missed its real-time deadline;
it passed in isolation and in the final complete run. No attack gameplay code was
changed to conceal this intermittent failure. Build/preview and test compilation
reported no C# compiler errors or warnings. git diff --check passed. Unity's
unrelated ProjectSettings serialization updates were restored after validation.
The existing VFX integration test counted all bootstrap children; its assertion
now counts Impact instances only, preserving duplicate-hit validation.

## Manual checks still required

Play Main: inspect locomotion cadence/foot sliding and punch presentation from the
Game camera, hit several enemies, pause/resume mid-motion, then Retry and verify
idle poses. Inspect floor contact on slopes and raised platforms. These provisional
procedural motions may need art tuning; no 100-enemy performance measurement or
full interactive gameplay review is claimed.

## Update 2026-11-24 (branch `polish/game-feel`)

The clips described above were regenerated with a different method; see `Docs/Design/07_PLAYTEST_AUDIT.md` section 3.

- The feet are placed first (planted during stance) and the leg joints are solved from them (`Assets/Editor/EnemyGait.cs`). Chase is now a run authored for `EnemySettings.runClipSpeed`, and a sixth clip, Walk, is authored for `walkClipSpeed`. The runtime plays them at real speed / authored speed.
- The gameplay prefab's model was 0.223 m above its root because it had been fitted to padded renderer bounds. `Tools > Game Demo Enemy > Ground Humanoid Enemy Model` corrects that one local height.
- `Tools > Game Demo Enemy > Rebuild Humanoid Enemy Clips` regenerates clips and controller in place without rebuilding the prefab hierarchy.
- Run duration is 0.6 s and walk 0.8 s (provisional). The statements above about a 0.65 s gait and five clips describe the earlier version.


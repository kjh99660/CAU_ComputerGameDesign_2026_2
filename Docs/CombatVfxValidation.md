# Player slash and enemy impact VFX

Main's CoreBootstrap creates CombatVfxRuntime and loads
`Assets/Resources/CombatVfxSettings.asset`. Its references point to the original
Free Slash VFX `Slash Fire VFX` and `Impact` prefabs; no scene or player prefab
assignment is required.

- Attack1, Attack2 and Charge_Release already send HitStart animation events.
  PlayerAttackHitbox now plays one slash at that point, including on a miss.
- The slash uses the player's horizontal facing at HitStart and remains in world
  space. Charge range scale also scales the slash.
- EnemyRuntime emits HitPresented only after ApplyHit accepts a hit. Impact is
  positioned at the enemy root plus a configurable height, independent of enemy
  knockback and pool return. Special hits use the same accepted-hit path.
- Runtime particle instances are reused, with a configurable cap per effect.
  Pause freezes particles explicitly; Battle resumes them; Finishing uses scaled
  time; Reset and noncombat states clear active instances. Round Tick rejects stale
  slash calls. Source prefabs are unchanged.

## Adjustable provisional values

Select CombatVfxSettings in Resources. Slash Offset defaults to (0, 1, 1.25) in
player facing space, and Impact Offset to (0, 1, 0) in world space. Both scales
default to 1 and both Euler offsets to zero. Tune these against the character and
camera in Play Mode, then copy values back after exiting Play Mode. The safety
timeout is 3 scaled seconds, and the pool cap is 128 instances per effect.

## Verification performed

Roslyn compilation against this project's generated Unity references succeeded
for GameDemo.Core, GameDemo.Systems, Assembly-CSharp and GameDemo.PlayMode.Tests.
No new compiler warnings were reported; the imported demo's unused
ParticleAnimationEvent.particleContainer field produces existing warning CS0169.
`git diff --check` passed for the changed source files.

Unity batch execution was attempted with:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testFilter CombatVfxTests -testResults Logs/combat-vfx-playmode.xml -logFile Logs/combat-vfx-playmode.log
```

The restricted attempt failed during Windows licensing system information access.
The unrestricted retry reported another Editor using this project. Tests did not
execute; no test pass or visual verification is claimed.

## Editor checks

1. In Window > General > Test Runner > PlayMode, run CombatVfxTests. Tests cover
   facing/placement, stale Tick rejection, pause/resume, pool reuse, accepted-hit
   placement, duplicate hit rejection, and Retry cleanup.
2. Play Main and reach Battle. Attack1, Attack2 and charged release must each show
   Slash Fire in front of the player at the swing event, also when no enemy is hit.
   Turn 90/180 degrees and repeat to check orientation. Tune Slash Euler Offset if
   a different visual swing plane is desired.
3. Hit one enemy, then several enemies in the same swing. Each accepted target
   must show one Impact at torso height; rejected/repeated hits must not add one.
4. Pause during visible particles. They must freeze and continue on Resume.
   Retry and Quit must clear all effects before Ready; start again and attack.
5. Check late hits during Finishing and absence of effects in Outro/Result.
   Check transparency, distortion and visual size with the actual Game camera;
   these rendering details cannot be validated by a headless compile.

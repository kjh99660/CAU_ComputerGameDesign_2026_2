# Player Prefab integration validation

The gameplay prefab is `Assets/Prefabs/Character/Player.prefab`. Its existing GUID,
model hierarchy, two Animators, controller, animation clips, sword/sheath references,
and Animation Event receivers are preserved. `Main` now references this prefab.
The deleted `Player 1.prefab` is not recreated.

`PlayerRuntime` owns gameplay input, movement, camera and Animator state. The legacy
input components are disabled on the prefab. `CharacterTest` overrides those two
components to enabled and PlayerRuntime to disabled, preserving the animation
author's standalone workflow. Do not apply those Scene overrides to the prefab.

The sword event relay still receives Draw/Sheathe events. Its presentation lifecycle
stops blend progression outside Battle/Finishing and resets drawn/handWeight on
Retry. Reference rig synchronization and the original prop offsets remain intact.

## Automated checks

Use Unity 6000.3.24f1. Close the project in other Editor instances before batch runs.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/player-merge-editmode.xml -logFile Logs/player-merge-editmode.log
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.com' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults Logs/player-merge-playmode.xml -logFile Logs/player-merge-playmode.log
```

Alternatively run both suites in Window > General > Test Runner. Added checks cover:

- Gameplay prefab references, root Animator selection, reference rig and sword links.
- CharacterTest standalone input overrides.
- Initial state delivery before PlayerRuntime.Awake, followed by Animator resume.
- Main player placement/target connection and exactly one PlayerRuntime.
- Animator/sword blend pause, Finishing animation, and sword reset.

Executed during this change: separate Roslyn compilation against installed Unity
references for Core, Systems, Assembly-CSharp, Editor, EditMode tests and PlayMode
tests succeeded. This is a compilation check, not a Unity test run. The compiler
reported existing CS0649 serialized-field warnings; the final recheck suppressed
that warning as Unity normally does.

Serialized structure checks also passed: 313 of 316 existing prefab object/component
blocks are identical; only the root and two legacy enabled flags changed, with one
Runtime component added. Every local prefab fileID resolves. Main differs only by
16 source GUID substitutions, and all referenced source IDs resolve in Player.

Unity batch tests could not start: the restricted attempt exited during WMI startup;
the console retry reported another Unity instance using this project. No Unity test
pass or successful asset import is claimed.

## Manual playback checks

1. Open Main and run Tools > Game Demo Player > Validate Player Layer. Expect no
   missing prefab/script/reference errors. This command opens Main.
2. Play Main: Ready, Intro and Countdown must keep movement/camera/animation idle.
   Start, Skip and Battle must enable the existing movement and combo animations.
3. Exercise Attack1, Attack2 and charge/release. Compare Draw/Sheathe timing, reference
   rig synchronization and sword/sheath alignment with CharacterTest.
4. Pause mid-draw. Character animation and sword blend must stop. Resume must
   continue; Retry must restore the spawn point, Idle and fully sheathed sword.
5. Pause Retry cancellation must stay Paused. Confirmed Retry and Quit must return
   to Ready. Starting again must replay Intro and allow Skip.
6. Open CharacterTest and play. Existing standalone movement, attack and orbit camera
   must work, without PlayerRuntime trying to require a round spawn point.

Visual playback, complete Retry/UI flow, and actual Unity test execution remain to
be verified in the Editor. No gameplay balance values were added or changed.

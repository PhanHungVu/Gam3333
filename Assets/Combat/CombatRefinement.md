# M6 — Combat evaluation and refinement

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode and click the Game view. The current direction remains continuous horde pressure: ordinary melee and ranged enemies attack whenever their own weapon, facing and target checks allow. There is no new shared ordinary attack budget.

## Changes

**Compact melee pursuit.** Basic melee now uses body-sized crowd clearance throughout pursuit, including when approaching past ranged actors outside the local melee area. Previously the wider approach spacing could hold melee back before it reached the local system. A standard 0.6-radius body uses 1.45 units of preferred clearance instead of the earlier 3.5-unit approach spacing. The existing swept body collision still prevents overlap.

Local goals continue to approach from each enemy's current side with a small lateral bias. Experiments with forced circling and cooldown yielding reduced pressure in the crowded test, so those behaviors are not enabled. Dense rear actors can still be blocked by other bodies; this does not promise that every enemy surrounds or attacks simultaneously.

**Overhead axe engagement.** The enemy's AI attack distance is 3.2 units, up from 2.2, so it can use its existing reach from behind the closest bodies. The enemy prefab and roster regeneration defaults agree. Shared part data, hitboxes, damage, cooldown and committed windup are unchanged.

**Stationary ranged turning.** A crossbow or heavy holding a useful firing position now turns toward the player during approach/cooldown. Previously Hold suppressed facing updates, so circling within its firing band could leave it unable to shoot again. The attack's committed windup still uses its existing facing and snapshotted aim; this fix does not add tracking during a committed shot.

## Review controls

| Key | Action |
| --- | --- |
| Page Up / Page Down | Select an A–G encounter |
| F11 | Start the selected encounter |
| F12 | Replay the saved encounter layout and starting pose |
| Home | Compare compact pursuit with the earlier wide approach spacing |
| End | Log a combat snapshot, also available in the Inspector's M6 foldout |
| F9 | Clear arena enemies and enemy shots |

After changing Home, restart the encounter for a fair comparison. Both settings retain continuous melee pressure and independent attacks. The comparison changes pursuit spacing only; it does not restore the earlier reserved-slot attack budget. The same controls are available as Inspector buttons/context menus, with no player HUD overlay.

The snapshot groups actors by role and reports contribution, releases, active attacks, cooldowns and ordinary reservations. Spinner contact and rope orbit/impact activity count as special contributions. Planning counters reset when the arena is cleared. `Lithomachia.AI.PressurePosition` is also available in the Unity Profiler. Its reported milliseconds measure actual local plan calls, excluding cached results; they are not a frame-rate measurement.

## Validation and remaining review

`EnemyBehaviorChecks.RunRefinementBatch` runs only in a disposable Unity project. It compares the same seeded thirty-actor encounter using both spacing settings, then checks camera clearance, stationary ranged turning, firing after turning, committed melee dodging, axe engagement/reach, cleanup and the review shortcuts. The fixture uses increased player health and scripted player movement for repeatability; the normal arena preserves the player's existing health, energy and equipment behavior.

Before the camera follow-up, the M6 run passed **17 checks** in headless Unity 6000.5.8f1. In the same seeded 25-second stationary encounter, both comparison settings used the revised axe range:

| Measurement | Earlier wide approach | Compact pursuit |
| --- | ---: | ---: |
| Ordinary melee releases | 99 | 112 |
| Individual melee actors with a release | 10 / 18 | 11 / 18 |
| Axe releases | 5 | 11 |
| Peak simultaneous ordinary attacks | 7 | 7 |
| Longest ready-but-idle interval | 0.033 s | 0.033 s |
| Smallest observed melee center spacing | 1.253 | 1.250 |
| Mean measured local plan time | 0.043 ms | 0.045 ms |

All thirty graphs remained functional. Both axes contributed with compact pursuit; seven other melee actors still had no release against the stationary player. Attack starts covered at least three quadrants in this full-circle encounter, which does not establish deliberate surrounding from a one-sided spawn. Tighter pursuit improves access without adding another queue, while dense stationary crowds remain a limitation. The ranged rotation cases resumed shooting without translating out of their useful firing position. A stationary target took the existing axe damage at the revised engagement distance, while sword and axe commitment checks allowed escape before release without taking damage.

Compatibility passed **18 pressure**, **30 ranged/shortcut**, **26 special**, **35 encounter** and **10 saved-arena** checks using current enemy prefabs, followed by **135 full-roster checks**. Full-roster setup regenerated prototype assets only inside the disposable project. The ten-sword pressure comparison retained seven simultaneous attackers and 102 attack starts in twenty seconds, compared with two simultaneous attackers and 20 starts in the earlier reserved mode. The larger encounter's moving-player leg recorded releases from every ordinary weapon role while spinner and rope coordination limits remained effective.

For hands-on review, compare B (crossbows), D (heavy ranged), C (spinner), F/G (each rope elite) and E (large mixed horde). Move across the crowd, dash out of committed attacks and circle shooters. Judge warning visibility, escape routes and perceived pressure in the Game view. Automated correctness and local planning timings do not establish visual readability, combat feel or a rendered frame-rate target.

The initial rendered verification on 2026-11-03 completed successfully in Unity 6000.5.8f1 using Direct3D 11. Both the original capture pass and a clean capture/diagnostic pass passed all **17 M6 checks**, with no further out-of-memory failure. Seven 1280×720 captures covered the horde overview, close-up and player view, plus crossbow/heavy windups from close-up and player cameras. Models, shadows and orange warning lanes rendered correctly. Clean captures temporarily hid HUD canvases and the existing debug melee-box previews; health bars remained visible. That initial review used unchanged gameplay and renderer runtime source and identified the camera issue below.

**Crowd camera correction — implemented:** the original thirty-enemy capture put the camera only **0.01 units** from the player root, inside the player model. A failing regression identified Default-layer enemy colliders such as `MaceArm 1` and `Golem1Head` as the camera obstacles. Cinemachine's tag-only filter treated these untagged character parts as scenery.

`CameraControlThirdPerson` now adds `MechCameraObstacles` to its generated Cinemachine rig. Camera sweeps ignore the tracked player's children and moving character hierarchies, including enemy heads and equipped weapons on Default or custom layers. Scenery still blocks the camera, including health-bearing structures. Both rig segments retain sphere clearance, immediate obstruction correction, smooth return and near-plane protection. Crowded queries expand their hit buffer so ignored limbs cannot hide a real wall. Actor collider settings, layers, combat, movement and camera framing values are preserved.

Tune **Obstacle Avoidance** on `CameraControlThirdPerson` as before. Its generated Third Person Follow has built-in avoidance disabled because `MechCameraObstacles` performs the filtered queries; the follow settings still hold the configured radius, mask and damping, and `CurrentObstacle` still reports the blocking world collider. The avoidance toggle and immediate target rebinding are covered by tests. Loose MechPart pickups retain their existing exclusion.

The corrected Direct3D 11 encounter passed **18 M6 checks**. Minimum player-to-camera distance after the initial two seconds was **13.08 units** throughout each 25-second horde comparison; the configured boom remains 13 units. The player-view capture was inspected and shows the crowd from the normal third-person view. **29 live equipment-camera checks** passed. The broader **965 foundation checks** passed, including **36 camera physics checks** covering character filtering, a wall behind 80 ignored colliders, initially touching walls, six real-platform directions, custom masks, smooth recovery and wide near-plane clearance. A legacy crossbow fixture was updated to read its authored release delay instead of assuming the pre-slowdown timing; enemy tuning was not changed.

The remaining verification ran inside an ignored `.camera-review` workspace folder after the system Temp copy was cleared. Only the child test processes used this private temporary location. Cached packages, one job/import worker and disabled domain reload were test-copy settings; working-project package, graphics and play-mode settings were not changed. Review outputs are retained in `.camera-review/Results`. The malformed GUID in this notes file's metadata was also corrected. Hands-on combat feel and rendered frame-rate targets remain separate reviews.

**Nearby side-target camera stability — implemented:** a separate regression reproduced repeated vertical movement as a nearby enemy crossed the camera's 60-degree side-angle cutoff. That cutoff toggled steep target-clearance pitch and melee framing, including in soft lock, whose focus otherwise follows manual aim. Before the fix, the fixed-player fixture measured 53–58 degrees of soft-lock pitch variation and 35–38 degrees in hard lock.

Soft lock now keeps its base pitch and bounded manual vertical adjustment independent of the selected enemy. Extra target-clearance pitch and committed melee framing apply only in hard lock, without a bearing cutoff. Hard lock still turns toward the enemy; free aim and manual look remain available. No prefab camera values were changed.

`MechCameraStabilityChecks` adds **18 checks** covering close enemies on either side, idle and melee framing, soft/hard lock, rapid cross-side tracking, free aim and manual vertical look. The corrected fixed-player fixture measured zero soft-lock pitch/height variation and at most **0.023 degrees / 0.001 units** in hard lock. The broader suite passed **983 foundation checks** (including these 18), followed by **29 live equipment-camera checks** in isolated Unity 6000.5.8f1. These measurements isolate target bearing at a fixed distance/height; deliberate pitch changes when a hard-locked target changes distance or elevation remain supported.

A final Direct3D 11 check used the real player and sword-enemy prefabs with normal camera/targeting frame updates and obstacle avoidance enabled. Soft idle on either side, soft melee and hard melee crossing sides all retained the target for **240/240 measured frames** per case and recorded zero pitch/height variation to four decimal places. Hard lock still turned through **711 degrees** of cumulative yaw. Four player-camera captures and the before/after metrics are retained in `.camera-review/Results`; the test-only scripted enemy movement did not change the runtime AI or prefab assets.

Production spawning integration remains the next separate phase after this combat review. `SpawnDirector`, production wave weights (including the temporary orbit-only configuration), player movement/combat and the empty pre-mission gameplay hook are unchanged.

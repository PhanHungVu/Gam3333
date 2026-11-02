# Small and elite enemy Behavior Graphs

The nine prototype prefabs use Unity Behavior 1.0.16 for decisions. Open `Scenes/EnemyBehaviorPlaytest.unity` for an isolated combat arena. Existing gameplay scenes keep their existing directors, objective logic, navigation and player rig; their references to the upgraded enemy prefabs and wave data pick up the new behavior.

## Playtesting

**Temporary orbit-only waves:** `WaveData_Roaming`, `WaveData_Survival` and `WaveData_KOTH` currently give `Enemy_Elite_RockRope` weight **100**, with all eight other entries at **0**. Restart Play Mode (or stop/restart the wave) to rebuild its cached weights. In ZooScene, enable **Elite Enemies**; the Small Enemies switch alone has no eligible enemies in this configuration. Existing scene-placed enemies and manually selected Inspector spawns are independent of wave weights.

To restore the mixed roster, set Roaming's first four small entries to **13.5**, spinner/crossbow to **18**, and all three elites to **3.3333333**. For Survival/KOTH use **10.5**, **14** and **10**, respectively. All prefab entries remain in place; spawn timing, resources and population caps retain their normal settings. The normal roster/probability checks below describe the mixed configuration.

1. Enter Play Mode in `EnemyBehaviorPlaytest` and select the **Enemy Behavior Playtest** object.
2. Choose **Selected Role** and press **Spawn Selected Enemy**, or use **Start Roaming Wave** / **Start Survival Wave**.
3. **Stop Wave** stops further spawning. **Clear Arena Enemies** also removes the current arena enemies. These controls are Inspector buttons and component context menus, with no debug overlay on the player HUD.
4. The arena initially contains one sword enemy and an obstacle to test shared pathfinding. The player uses the existing controls and damage/health systems. Death still deactivates the player; restart Play Mode for another attempt.
5. Select a running enemy and open its **Behavior Graph Agent** graph to inspect its live branch. **Enemy Graph Controller / Current Action** and the special attack's stage are useful alongside the graph debugger.

The Inspector lists **Rock Rope - Orbit** (the original rope elite, updated) and **Rock Rope - Throw and Pull** (the preserved duplicate). Both are also included in the random-wave presets and Zoo elite switch.

The setup menus are `Tools > Mech > Enemies > Set Up Small and Elite Behavior Roster` and `Create Behavior Playtest Arena`. Assets are already included; these menus are regeneration tools. Roster setup reapplies prototype tuning and wave composition, so commit designer changes first. Existing graph assets and an existing playtest scene are preserved. The editor authoring adapter checks package version because Unity Behavior's graph-authoring API is internal; runtime AI uses public APIs without reflection.

## Decisions and execution

Graphs are in `AI/Behavior/Graphs`. They share **Acquire and Approach**, **Telegraphed Weapon Attack**, **Recover**, and **Wander Until Detection** subgraphs. Type 1 variants share the same overhead graph. Crossbow and heavy ranged have their own graph assets for later design changes. Spinner and both rope graphs order windup, execution and recovery; the preserved throw-and-pull graph includes the separate pull action.

`EnemyAgent.ExternalDecisions` disables the legacy state-machine decisions. Graph actions submit a movement intention each frame; a missing/stopped graph holds position rather than reverting to old AI. The existing flow field, separation, obstacle collision, facing and ground following still move the actor. Committed charges and pulls use that same collision motor with a fixed direction and bypass flocking forces for that stage.

`EnemyAttack.ExternalRequests` disables automatic attack initiation. Graphs request attacks, while the existing weapon lanes own `PartData` multipliers, animation release, cooldowns, melee obstruction checks and travelling projectiles. Type 1 throwing axe deliberately uses the common overhead contact attack, as requested; its player weapon data is unchanged. Melee swings recheck stop range at release and retain their committed facing. Ranged shots snapshot aim rather than homing.

The spinner's swept contact box and rope's landing area use `CombatBlast` and `CombatHealth`, including team, owner, wall and compound-collider handling. They do not introduce another damage system. Disabling a graph/actor cancels pending special damage and restores its presentation. Already released ordinary weapon projectiles retain their existing independent lifetime.

## Prototype roster and tuning

| Role | Prefab | HP | Move speed | Attack distance | Cost |
| --- | --- | ---: | ---: | ---: | ---: |
| Sword | `Small Enemies/Enemy_sword` | 100 | 6 | 2 | 1 |
| Mace | `Small Enemies/Enemy_mace` | 120 | 6 | 2.2 | 1 |
| Hammer | `Small Enemies/Enemy_hammer` | 160 | 4.5 | 2.3 | 1 |
| Throwing axe | `Small Enemies/Enemy_throwingAxe` | 90 | 6 | 2.2 | 1 |
| Spinner | `Small Enemies/Enemy_spinner` | 120 | 4 (12 charging) | 8 | 2 |
| Crossbow | `Small Enemies/Enemy_crossbow` | 80 | 5 | 5–8 | 2 |
| Heavy ranged | `Elite Enemies/Enemy_Elite_Cannon` | 320 | 3.5 | 11–18 | 10 |
| Orbiting rock | `Elite Enemies/Enemy_Elite_RockRope` | 400 | 3 approach / 9 orbit charge | 4.5–6.5 | 10 provisional |
| Rock throw and pull | `Elite Enemies/Enemy_Elite_RockRopePull` | 400 | 3 (8 pulling) | 11–18 | 10 provisional |

Distances and speeds are world units. Tune awareness, speed and stop distance on **Enemy Agent**; ranged minimum distance is on **Enemy Graph Controller**. Tune health and cost on their existing components. `EnemyAttack` multipliers scale each equipped weapon's existing `PartData`; changing the global player part assets is unnecessary.

| Weapon role | Damage multiplier | Cooldown multiplier | Fire duration / release fraction |
| --- | ---: | ---: | --- |
| Sword | 0.15 | 1.5 | 0.7 s / 0.6 |
| Mace | 0.25 | 1.5 | 0.7 s / 0.6 |
| Hammer (mace data proxy) | 0.35 | 2 | 1.1 s / 0.7 |
| Throwing axe | 0.3 | 3 | 0.7 s / 0.6 |
| Crossbow | 0.2 | 2 | 0.7 s / 0.6 |
| Heavy shoulder cannon | 0.45 | 1.5 | 1.4 s / 0.7 |

Crossbow uses a 12-degree aim cone and 0.6 projectile-speed multiplier. Heavy ranged uses the existing explosive shoulder weapon data, 0.5 projectile-speed multiplier and a longer windup. Both retreat when inside their preferred minimum distance, then face and fire through the existing attack checks.

Spinner hops its visual root while approaching, visibly accelerates its spin for 0.9 seconds, then charges along the announced direction for up to 0.75 seconds. It stops on a damaging contact, a blocking wall or duration limit, with 18 contact damage, a 3.5-second start-to-start cooldown and at least 0.8 seconds of recovery. The root remains owned by `EnemyAgent`; hopping and spinning affect only its visual child.

**Orbiting rock (original prefab):** approach using the shared flow field, announce the circular path during a 1.1-second windup, then throw toward the player's committed bearing over 0.7 seconds. The rock lands on the sampled floor, **rests for 1.2 seconds**, and **lifts over 0.35 seconds** before spinning. These setup stages hold the enemy in place. During the **3-second spin**, it charges the live player at **9 units/second** with **30 units/second² acceleration**, following the existing flow-field chase direction through the shared collision/ground motor. It brakes toward the **2.5-unit** stopping clearance instead of running through the target. The normal approach remains 3 units/second; only the spin uses the faster charge. The orbit moves with the enemy at a **6-unit radius** and **1.1-unit height**. Spin speed is now **360 degrees/second** (twice the earlier speed), accelerating over 0.4 seconds. The rock passes through scenery during its throw, lift and orbit; terrain contact does not interrupt this variant. The body still collides with terrain normally. Moving rock contact deals **35 damage once per target per attack**, shared across throw, lift and orbit, using the shared team/wall/damage rules. The grounded rock, rope and warning circle do not deal standing contact damage. Fast frames subdivide the arc to avoid skipping targets or cutting across the center. Recovery lasts at least 0.8 seconds, with the existing 7-second minimum start-to-start cooldown. Tune **Orbit Degrees Per Second**, **Orbit Ground Pause Duration**, **Orbit Lift Duration** and **Orbit Chase Stop Distance** on **Enemy Rope Attack**; spin charge speed/acceleration are exposed as **Orbit Charge Speed** and **Orbit Charge Acceleration**; ordinary approach speed still comes from **Enemy Agent / Max Speed**.

The orbit graph is **Elite Type 2 - Orbiting Rock**: windup → execute (throw, ground pause, lift and orbit) → recover. It has no pull action. The original role remains serialized as index 7; the preserved **RockRopePull** role is appended as index 8. Both share the corrected attachment, rock fitting, obstruction and cancellation code.

The body turns to keep its rope mount on the rock's side while walking. **Orbit Body Turn Speed** is **720 degrees/second**. The graph keeps normal navigation active while the orbit controls facing; the rock and warning ring follow the motor's final position each frame. The preserved throw-and-pull prefab keeps its existing attack, collision and pull behavior.

**Preserved throw-and-pull variant:** snapshot the player's location, show a landing circle during a 1.1-second windup, throw along a 0.95-second arc, apply one 35-damage landing area with radius 2.5, then pull the elite toward the fixed landed rock. Moving the player after landing does not redirect the pull. Pulling stops within 2 units, at a wall, or after 2 seconds. The rock returns to the held position during recovery; cooldown is 6 seconds with at least 0.8 seconds of recovery. Pull movement itself does not apply an additional hit. A wall can intercept the airborne rock early. This duplicate keeps **Elite Type 2 - Rock Rope**, the existing graph containing the pull action.

The rope now attaches to the proxy rig's **right shoulder mount**, with **Rope Anchor** exposed for the art team's eventual hand attachment. It follows the animated rig after pose updates. The centered throwable has a 1.2-unit maximum diameter and cancels the elite root's non-uniform scale; its mesh and rope endpoint therefore follow the same position used by the attack. The rope has light slack while held and becomes taut during the pull. Recovery returns to the held offset beside the mount rather than swallowing the rock into the attachment point. Roster setup reapplies this fitted proxy without changing the source rock model.

Landing samples the target's actual floor, including raised platforms. A wall interception preserves its contact height instead of snapping the rock down to the floor. If the rock already overlaps scenery at launch, it stops there; an empty initial-overlap contact point is never interpreted as world origin. The landed rock stays fixed while pulling, and the final movement step is limited to the stopping clearance. Lost targets during recovery, stopped graphs and death cancel the presentation; a cancelled rope stays hidden instead of being re-enabled by `LateUpdate`.

The hammer uses two replaceable block heads on the existing animated mace rigs. Spinner reuses the current small golem art; the rope elite reuses the current heavy golem and a map-rock proxy with a simple rope. These are gameplay prototypes, not final character art. Source models, shared player `PartData` and animation clips are unchanged. Replace visual references on these new prefabs when authored assets arrive. Threat lines are lightweight, replaceable world-space presentation without damage or collision. New special-attack audio slots are intentionally empty for authored clips later; existing weapon audio remains its existing system.

## Waves

### ZooScene exhibit

`Scenes/ZooScene.unity` uses its existing two floor switches and spawn-model locations. Step onto a switch and press **F** to toggle **Small Enemies** (sword, mace, hammer, throwing axe, spinner, crossbow) or **Elite Enemies** (heavy ranged, orbiting rock, rock throw and pull). Both groups start off. Both can be enabled together. The retired `EnemySpawnerManager_Mace` / `EnemySpawnerManager_Crossbow` prefab references have been replaced by scene group objects, preserving the original switch references.

One **SpawnDirector** and **ZooEnemyWaves** component on the **Enemy** object use the shared roster and the zoo's existing navigation/spawn points. The showcase makes a temporary filtered wave: one spawn every 2 seconds, 50 resource per cycle, 15-second refill delay and a scene-wide limit of 8 active enemies. Tune these showcase values on **Zoo Enemy Waves**; the saved Roaming/Survival presets are unchanged. An active group permits its roster and spawn location; with both enabled, the director uses both locations. Turning a switch off stops further spawning from that group; already spawned enemies remain until defeated or the scene is restarted.

`ZooEnemyChecks.RunBatch` checks a disposable copy of the saved scene: no missing prefabs/scripts, intact switch bindings, startup-off state, shared navigation, actual spawns of all six small/three elite roles, running graphs, combined roster, population/resource limits and stopping the wave. The environment, player and camera retain their existing scene setup.

### Roaming and Survival

| Setting | Roaming | Survival |
| --- | ---: | ---: |
| Data asset | `WaveData_Roaming` | `WaveData_Survival` |
| Spawn interval | 0.5 s | 0.2 s |
| Reset after exhausting resource | 120 s | 30 s |
| Resource per cycle | 50 | 100 |
| Aggregate small weight | 90% | 70% |
| Aggregate elite weight | 10% | 30% |
| Prototype active-enemy cap | 24 | 32 |

`WaveData_KOTH` uses the same Survival settings to fit the existing KOTH objective integration. `WaveType.KOTH` is retained for the standalone Survival preset too; no new mission controller or scene transition was introduced. Within each small group, Type 1 variants split 60% of its weight and spinner/crossbow each receive 20%. The three elites split their group's weight equally, retaining the existing total **10% roaming / 30% survival** elite probability. Probabilities apply to affordable entries: near the end of a resource cycle, an unaffordable elite is excluded by the existing director.

The optional **Max Concurrent Enemies** setting counts active `EnemyAgent` actors, including other loaded scenes. Zero preserves unlimited legacy behavior. While capped, the director waits without spending resource; it resumes when a slot opens. The configured 24/32 caps are initial tuning values, not advanced coordination or limits on simultaneous attackers. Resource and timer ownership remain with `WaveCycle`/`SpawnDirector`. The rope cost was undefined in the plan, so it starts at 10 and can be tuned independently.

## Validation and remaining review

The faster **9-unit/second orbit charge** passed **64 focused rope checks**, followed by **123 full-roster checks** including the added stopping-clearance case. Checks cover rapid acceleration, configured top speed, redirection during the same spin, braking before the player, moving orbit/rope alignment, body collision, cancellation and the unchanged throw-and-pull behavior. `Orbit Charge Speed` and `Orbit Charge Acceleration` apply only during the spin.

The faster, moving orbit update passed **64 focused rope checks and 122 full-roster checks** in a separate headless Unity 6000.5.8f1 project. Coverage includes more than two revolutions in 2.25 seconds, live-player pursuit and redirection, constant radius and rope attachment while walking, uninterrupted lift/spin through scenery, retained enemy-body collision, cancellation/death and the preserved throw-and-pull variant. Mixed-roster fixtures run with their normal wave weights inside the test project; the working project's temporary orbit-only waves remain enabled.

The nine-enemy rope-variant update passed **185 checks** in isolated Unity 6000.5.8f1: **51** focused rope/shared-damage checks, **109** full roster/integration checks, **10** saved-arena checks and **15** saved-Zoo checks. They cover the preserved throw/pull behavior, a full orbit at fixed radius, stationary center and upright body turn, contact-only damage with per-attack hit history, long-frame wall interception, return/cancellation/death, both Inspector selections, all nine actual wave-spawn roles and unchanged aggregate elite probabilities. Rendered orbit and recovery stages were reviewed. Source rock art, the Zoo scene and empty pre-mission gameplay hook are unchanged.

`Tests/Editor/EnemyBehaviorChecks.RunBatch` runs the real graphs and prefabs in a disposable project. It covers awareness and reacquisition, overhead release/cooldown, disabled graph/death cleanup, separation and obstacle routing, every roster role, crossbow repositioning/inaccuracy, committed spinner dodging and wall contact, rope landing/pull/cancellation, wave budget/refill/caps, and a twenty-second mixed-roster encounter. Rendered captures cover the attack stages. Existing combat and movement regressions run separately. Deterministic damage fixtures turn off the crossbow's random cone only inside those fixtures.

`EnemyBehaviorChecks.RunRopeBatch` isolates the rope regression using the real prefab and graph: centered geometry and attachment while walking/turning, the complete attack, a relocated player during the pull, recovery target loss, interruption in every attack stage, actor reactivation, a high wall interception, raised-floor targeting, death during flight and scenery overlapping the launch point. Like the other runners, it refuses to run outside a disposable temporary project.

The earlier rope repair passed **21 focused rope checks and 78 existing roster/integration checks** in isolated Unity 6000.5.8f1. Rendered held, windup, flight, pull, recovery, wall-contact and raised-floor stages were reviewed. The initial-overlap fixture first reproduced the world-origin teleport before the fix; its final run stops at the actual launch position. Those pull checks now exercise the preserved duplicate, alongside the new orbit cases.

Validated in Unity 6000.5.8f1: 78 roster/integration checks, 8 saved-arena checks (`EnemyBehaviorChecks.RunArenaBatch`), 933 existing foundation checks, and 9 live Merge Test combat checks passed. Registry resolution of Behavior 1.0.16 was also verified. The mixed encounter's editor timing is diagnostic only; it is not a target-device performance benchmark.

This is functional prototype tuning. Normal interactive combat review is still needed to judge telegraph readability from the gameplay camera, fun/difficulty, final enemy art, authored audio and target-device performance. Bosses and advanced group tactics are outside this implementation. The pre-mission launch function remains empty for the gameplay team; there is no mission-to-`JhMapScene` handoff added here.

# Small and elite enemy Behavior Graphs

The eight prototype prefabs use Unity Behavior 1.0.16 for decisions. Open `Scenes/EnemyBehaviorPlaytest.unity` for an isolated combat arena. Existing gameplay scenes keep their existing directors, objective logic, navigation and player rig; their references to the upgraded enemy prefabs and wave data pick up the new behavior.

## Playtesting

1. Enter Play Mode in `EnemyBehaviorPlaytest` and select the **Enemy Behavior Playtest** object.
2. Choose **Selected Role** and press **Spawn Selected Enemy**, or use **Start Roaming Wave** / **Start Survival Wave**.
3. **Stop Wave** stops further spawning. **Clear Arena Enemies** also removes the current arena enemies. These controls are Inspector buttons and component context menus, with no debug overlay on the player HUD.
4. The arena initially contains one sword enemy and an obstacle to test shared pathfinding. The player uses the existing controls and damage/health systems. Death still deactivates the player; restart Play Mode for another attempt.
5. Select a running enemy and open its **Behavior Graph Agent** graph to inspect its live branch. **Enemy Graph Controller / Current Action** and the special attack's stage are useful alongside the graph debugger.

The setup menus are `Tools > Mech > Enemies > Set Up Small and Elite Behavior Roster` and `Create Behavior Playtest Arena`. Assets are already included; these menus are regeneration tools. Roster setup reapplies prototype tuning and wave composition, so commit designer changes first. Existing graph assets and an existing playtest scene are preserved. The editor authoring adapter checks package version because Unity Behavior's graph-authoring API is internal; runtime AI uses public APIs without reflection.

## Decisions and execution

Graphs are in `AI/Behavior/Graphs`. They share **Acquire and Approach**, **Telegraphed Weapon Attack**, **Recover**, and **Wander Until Detection** subgraphs. Type 1 variants share the same overhead graph. Crossbow and heavy ranged have their own graph assets for later design changes. Spinner and rock-rope graphs explicitly order their separate windup, execution, pull (rope only), and recovery actions.

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
| Rock on a rope | `Elite Enemies/Enemy_Elite_RockRope` | 400 | 3 (8 pulling) | 11–18 | 10 provisional |

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

**Rock-rope movement follows the chosen design:** snapshot the player's location, show a landing circle during a 1.1-second windup, throw along a 0.95-second arc, apply one 35-damage landing area with radius 2.5, then pull the elite toward the fixed landed rock. Moving the player after landing does not redirect the pull. Pulling stops within 2 units, at a wall, or after 2 seconds. The rock returns to the hand during recovery; cooldown is 6 seconds with at least 0.8 seconds of recovery. Pull movement itself does not apply an additional hit. A wall can intercept the airborne rock early.

The hammer uses two replaceable block heads on the existing animated mace rigs. Spinner reuses the current small golem art; the rope elite reuses the current heavy golem and a map-rock proxy with a simple rope. These are gameplay prototypes, not final character art. Source models, shared player `PartData` and animation clips are unchanged. Replace visual references on these new prefabs when authored assets arrive. Threat lines are lightweight, replaceable world-space presentation without damage or collision. New special-attack audio slots are intentionally empty for authored clips later; existing weapon audio remains its existing system.

## Waves

| Setting | Roaming | Survival |
| --- | ---: | ---: |
| Data asset | `WaveData_Roaming` | `WaveData_Survival` |
| Spawn interval | 0.5 s | 0.2 s |
| Reset after exhausting resource | 120 s | 30 s |
| Resource per cycle | 50 | 100 |
| Aggregate small weight | 90% | 70% |
| Aggregate elite weight | 10% | 30% |
| Prototype active-enemy cap | 24 | 32 |

`WaveData_KOTH` uses the same Survival settings to fit the existing KOTH objective integration. `WaveType.KOTH` is retained for the standalone Survival preset too; no new mission controller or scene transition was introduced. Within each small group, Type 1 variants split 60% of its weight and spinner/crossbow each receive 20%. Elites split their group's weight equally. Probabilities apply to affordable entries: near the end of a resource cycle, an unaffordable elite is excluded by the existing director.

The optional **Max Concurrent Enemies** setting counts active `EnemyAgent` actors, including other loaded scenes. Zero preserves unlimited legacy behavior. While capped, the director waits without spending resource; it resumes when a slot opens. The configured 24/32 caps are initial tuning values, not advanced coordination or limits on simultaneous attackers. Resource and timer ownership remain with `WaveCycle`/`SpawnDirector`. The rope cost was undefined in the plan, so it starts at 10 and can be tuned independently.

## Validation and remaining review

`Tests/Editor/EnemyBehaviorChecks.RunBatch` runs the real graphs and prefabs in a disposable project. It covers awareness and reacquisition, overhead release/cooldown, disabled graph/death cleanup, separation and obstacle routing, every roster role, crossbow repositioning/inaccuracy, committed spinner dodging and wall contact, rope landing/pull/cancellation, wave budget/refill/caps, and a twenty-second mixed-roster encounter. Rendered captures cover the attack stages. Existing combat and movement regressions run separately. Deterministic damage fixtures turn off the crossbow's random cone only inside those fixtures.

Validated in Unity 6000.5.8f1: 78 roster/integration checks, 8 saved-arena checks (`EnemyBehaviorChecks.RunArenaBatch`), 933 existing foundation checks, and 9 live Merge Test combat checks passed. Registry resolution of Behavior 1.0.16 was also verified. The mixed encounter's editor timing is diagnostic only; it is not a target-device performance benchmark.

This is functional prototype tuning. Normal interactive combat review is still needed to judge telegraph readability from the gameplay camera, fun/difficulty, final enemy art, authored audio and target-device performance. Bosses and advanced group tactics are outside this implementation. The pre-mission launch function remains empty for the gameplay team; there is no mission-to-`JhMapScene` handoff added here.

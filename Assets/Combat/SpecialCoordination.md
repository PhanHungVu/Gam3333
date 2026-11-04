# M4 — Special enemies alongside the pressure horde

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode, click the Game view and press **F10**. The seeded encounter contains **6 swords, 2 small crossbows, 3 spinners, 1 orbiting-rock elite and 1 throw/pull elite**. F10 replaces the current encounter; **F9** stops spawning and clears the arena. F5–F8 retain their existing functions. Inspector controls remain available without a combat HUD.

## Spinner

Before starting its wind-up, the spinner checks a body-width lane toward the player and walkable support along that lane. Scenery, an initial scenery overlap or a ground gap prevents the attack from starting or consuming cooldown. Healthy actors are excluded from this scenery check, preserving the existing charge/crowd behavior. If the lane is blocked, the coordinated spinner seeks a reachable side angle or follows the existing flow field.

Its existing 0.9-second wind-up commits the direction. Moving after that warning begins can dodge the charge. Scenery entering the lane afterward still stops movement through the existing motor. Charge speed, duration, damage and recovery are unchanged: 12 units/second, up to 0.75 seconds, 18 damage, a 3.5-second start-to-start cooldown and at least 0.8 seconds of recovery.

## Special coordination

The arena's **Enemy Special Coordinator** controls only explicitly bound Spinner, Rock Rope - Orbit and Rock Rope - Throw and Pull actors:

- **Maximum Spinner Attacks:** 1 nearby spinner wind-up/charge per target by default. Recovery does not occupy this capacity.
- **Maximum Orbit Attacks:** 1 nearby orbit attack per target, including its wind-up, throw, ground pause, lift and active spin. Its recovery releases the capacity.
- **Special Start Spacing:** 0.55 seconds between nearby special wind-up starts against the same target. Spinner and rope phases may otherwise overlap. Throw/pull retains its independent execution after this start spacing.
- **Local Radius:** 24 units. Separate targets have independent admission; distant encounters do not share a global quota.

Ready requests keep their order. A stale, obstructed or currently capacity-blocked request cannot prevent another eligible special from starting. No grant is held while awaiting another graph tick: admission and the existing BeginWindup happen together. Damage, animation and cooldown remain owned by the existing attacks. There is no ordinary melee or ranged attack budget in this system.

Specials seek local goals while pending, and after their recovery pose finishes if cooldown remains. Goals try the current side and ±25, ±50 and ±80 degrees around the player. They favour shorter movement, space between actors/goals and angles away from the close melee pack. Spinners additionally require a usable charge lane from the goal. Throw/pull goals prefer clear lines toward the target. A ready special can start from its current useful position; reaching an ideal flank is not a prerequisite.

## Existing rope attacks

The orbit elite retains its throw → 1.2-second ground pause → lift → 3-second spin, 360-degree/second rotation and 9-unit/second charge. The full 6-unit spin circle still deals 35 damage once per target per attack. The cap prevents overlapping orbit setups/areas around the same nearby target; it does not add an attack or change the rock's terrain pass-through behavior.

The preserved throw/pull elite retains its committed landing target, 35-damage impact, movement toward the landed rock and wall interception. It can contribute disruption while an orbit or spinner is active. No new attacks or movement abilities were added, and authored audio slots remain empty.

## Integration and debugging

M4 admission remains an opt-in combat-arena prototype. Arena encounter and selected-enemy spawning bind supported actors through `EnemyGraphController.BindSpecialCoordinator`. Production SpawnDirector and wave weights are unchanged. Spinner lane validation also protects a standalone spinner. Ordinary melee pressure, the slower small-crossbow tuning, player controls and the empty pre-mission mission-start hook are preserved.

Select **Enemy Behavior Playtest** to see active/pending special counts. Select **Enemy Special Coordinator** for cyan suggested-goal Gizmos and its tuning fields. Individual **Enemy Graph Controller / Current Action** and **Special Attack / Current Stage** distinguish navigation, wind-up, execution and recovery.

`EnemyLocalPositionQuery` shares the existing ground and direct-route validation between ranged and special local planners. The ranged angle candidates, distance bands and scoring remain unchanged.

## Validation and next milestone

`EnemyBehaviorChecks.RunSpecialCoordinationBatch` runs actual saved prefabs and graphs in a disposable Unity project. It covers F10/F9, deterministic composition, blocked and unsupported charge lanes, committed dodging, flanking suggestions, special capacity/start spacing, ordinary melee overlap, both rope variants, graph/death/pooling/target cleanup, independent targets and the full horde encounter.

Headless Unity 6000.5.8f1 passed **209 checks**: **26 M4**, **30 ranged/shortcut**, **18 melee-pressure** and **135 full-roster** checks. The first three runs used the working project's current saved prefabs, including slower crossbows; full-roster setup regenerated prototype assets in the disposable project only. Three spinners began 6, 6 and 5 attacks in the twenty-second coordination fixture, while ordinary melee peaked at six simultaneous attacks. The thirty-second F10 fixture recorded 8, 7 and 7 spinner contacts, three orbit deployments and three throw/pull impacts; all thirteen graphs remained active. The separate melee pressure comparison retained 97 starts and seven simultaneous attackers over twenty seconds.

These checks establish execution, cleanup and pressure continuity. Use the Game view to assess warning clarity and escape opportunities while moving and fighting.

**M5 — Controlled encounter testing** is implemented in [EncounterTesting.md](EncounterTesting.md), adding seven editable presets, explicit placements and snapshot replay while retaining F6–F10. **M6 — Combat evaluation/refinement** is next. Production wave integration and player combat mechanics remain later work.

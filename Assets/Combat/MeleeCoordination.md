# M1 — Sword horde positioning

**Current direction:** [Continuous melee pressure](MeleePressure.md). Use **Start Pressure Swarm** in the arena Inspector. The reserved positions and M2 budget described below are retained as earlier comparison modes, not the default melee behavior. Their controls are under **Earlier behavior comparisons**.

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode, and select **Enemy Behavior Playtest** in the Hierarchy.

1. Press **Start Baseline Swarm** to restart with ten swords using their ordinary Behavior Graph approach.
2. Press **Start Positioning Only Swarm** to restart the same seeded spawn layout with M1 local positioning enabled.
3. Press **Start Coordinated Swarm (M2)** to include shared melee attack permissions; see [MeleeAttackPermissions.md](MeleeAttackPermissions.md).
4. Toggle **Use Coordination** during a fight to compare movement without respawning.
5. Use **Clear Arena Enemies** to stop the encounter and remove its enemies, including defeated swarm actors.

The comparison buttons stop the arena's manually started wave, replace its enemies, and optionally return the player to the position and rotation captured at Play Mode startup. They do not reset player health, energy, equipment or movement state. Restart Play Mode for a completely fresh player. Count, seed, spawn arc and spawn distance are editable on the playtest component. Blocked spawn cells are skipped with a warning rather than moving a spawn to an arbitrary position.

The comparison spawns actual sword prefabs directly. It does not change `SpawnDirector`, wave probabilities, costs or objective spawning. The current orbit-only wave presets remain intact. The existing single-enemy and wave buttons retain their original AI behavior; participation is attached only to swords created by the comparison controls.

## What changed

- Behavior Graphs still own each actor's decisions. The optional `EnemyCoordinationMember` asks the scene's `EnemyHordeCoordinator` for a position; the coordinator never moves a transform or requests an attack.
- Far away, swords use the existing shared flow field. Inside the configurable local radius, they choose exclusive, slightly irregular positions around the player. Four inner positions and eight outer positions are the initial settings.
- Assignments last for a bounded lease and rotate, with a short delay before a previous inner occupant can claim another inner position. In Positioning Only mode these remain spatial assignments; M2 adds a separate permission pool. Existing weapon cooldowns and release timing still apply.
- Short arc waypoints avoid walking straight through the player to reach the other side. Geometry checks reject blocked/off-grid positions, missing ground and obstructed local routes. Rejected positions use ordinary navigation; lack of progress also triggers reassignment.
- If an actor stands in conservative grid padding beside a wall, bounded recovery can step it onto a nearby reachable cell using the existing field and physical clearance checks. With a blocked local approach, it follows nearby flow-field waypoints rather than the optional zigzag demo steering. This prevents pursuit becoming trapped against that wall.
- Position goals retain the agent's force steering, obstacle avoidance, ground following and speed limit. Local body clearance replaces the wider separation radius while positioning/holding a position, and a swept crowd check prevents exchanging actors passing through one another. Leaving positioning restores ordinary crowd behavior.
- Committed attacks retain their facing and movement ownership. Swords can reposition during their existing weapon cooldown. A fast player escape drops stale local assignments and resumes ordinary chase.
- Death, member disable, coordinator disable, stopped graphs and target changes clear assignments. M1 starts with swords; M2 also accepts explicitly bound Mace, Hammer and overhead Throwing Axe members. Crossbows and special enemies keep independent decisions.

## Tuning and diagnostics

Select **Enemy Behavior Playtest** with Scene Gizmos enabled. Orange points show inner melee destinations; cyan points show outer destinations. Lines show the next steering waypoint. Select an individual sword's **Enemy Coordination Member** to inspect its status, including fallback reasons.

Tune positions, waiting radius, local radius, lease, reuse delay and planning interval on **Enemy Horde Coordinator**. Position geometry is evaluated at the planning interval; ordinary movement and collision remain per frame. This is local steering over the existing grid, not a pathfinder to arbitrary destinations. Slopes, narrow generated roads and larger groups need the later terrain/performance review.

`EnemyBehaviorChecks.RunCoordinationBatch` runs saved-arena integration checks in a disposable project. It covers seeded baseline comparison, actual graph-driven attacks, position uniqueness, approach angles, spacing, toggling, death/disable cleanup, player escape, blocked-route fallback and exclusion of ranged roles. Existing full-roster/rope checks remain separate regression coverage.

The final M1 run passed **28 checks** in headless Unity 6000.5.8f1. During the 25-second stationary-player swarm, actual attacks came from all four directions, six different swords attacked, and minimum centre spacing was 1.27 units against their combined 1.2-unit body radii. The saved arena is ready for a hands-on comparison of combat feel.

The final source also passed **123 existing full-roster regression checks**, including both rope variants, weapon release timing, graph ownership, wave population/resource limits and a 20-second mixed-roster encounter. Shared wave assets and enemy prefabs were not changed in the working project.

## Next milestone

M2 is implemented in [MeleeAttackPermissions.md](MeleeAttackPermissions.md). **M3 — Ranged firing positions** is next. Special coordination, full encounter presets and combat refinement follow; production wave integration remains later work.

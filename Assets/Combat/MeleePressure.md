# Continuous melee pressure

This is the current melee direction. Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode, click the Game view and press **F6**, or select **Enemy Behavior Playtest** and press **Start Pressure Swarm**. The default is ten swords, using the existing seeded layout and actual enemy prefabs. See [RangedPositioning.md](RangedPositioning.md) for the F5–F9 test shortcuts and M3 ranged behavior.

## Behavior

- A sword, mace, hammer or overhead axe attacks as soon as its target is reachable, facing checks pass and its weapon is ready. It does not wait for a shared permission, assigned angle or inner position.
- Enemies keep pursuing the player during their weapon cooldown. Once an attack has started, its existing committed wind-up and hit timing still apply.
- Basic melee uses compact body clearance throughout pursuit and the existing swept crowd check. Bodies still block one another; a four-position admission limit no longer holds the rest of the group outside combat. M6's Home comparison restores the earlier wider approach spacing outside the local melee area.
- Local approach goals follow each enemy's current side with a small lateral bias. There is no outer waiting ring, forced rotation or spatial lease in this mode. Blocked approaches use the existing navigation recovery.
- Basic enemies without a coordination member also pursue during cooldown and retry reachable attacks promptly. The arena's disabled-coordinator baseline remains available for comparisons.

Weapon damage, authored cooldowns, wind-up duration and movement speed are unchanged by melee pressure. M3 improves ranged positions separately; the rope orbit's active circle now applies area damage. Player controls, production waves and the pre-mission handoff are unchanged.

## Inspector

**Enemy Horde Coordinator → Melee Style** defaults to **Continuous Pressure**. Shared attack reservations are inactive in this style, regardless of the earlier comparison settings. Orange Scene Gizmos show local approach goals, not places enemies must wait to occupy before attacking.

The earlier M1/M2 experiments remain under the arena Inspector's collapsed **Earlier behavior comparisons** foldout. Reserved positions and the two-attacker budget are comparison tools; **Start Pressure Swarm** restores the current direction.

Count, seed, arc and spawn distance remain on **Enemy Behavior Playtest**. Encounter starts do not refill player health or energy; restart Play Mode for a fresh player.

## Verification

`EnemyBehaviorChecks.RunPressureBatch` runs in a disposable Unity project using the working project's current enemy prefab tuning, without regenerating or retuning that roster. It compares the old reserved behavior against continuous pressure on the same seeded ten-sword encounter. Checks cover simultaneous attacks, total attack starts, participation, delay after readiness, unchanged cooldown/damage, pursuit during cooldown, immediate attacks without a slot/token, cleanup and unchanged ranged/special roles.

The pressure run passed **18 checks**. In the same 20-second encounter, attack starts rose from **20 to 97**, peak simultaneous attacks rose from **2 to 7**, and the longest observed ready-but-idle interval fell from **1.433 to 0.033 seconds**. Nine of ten swords attacked in pressure mode. Weapon damage and authored cooldowns matched their source prefabs. These are headless correctness/behavior measurements; use the saved arena to judge the feel while moving and fighting.

Compatibility passed **28 positioning checks**, **41 reservation-comparison checks**, and **123 full-roster checks**, including both rope variants. The mixed-roster fixture now uses a fixed steering seed and logs per-role position/action/attack progress, keeping its existing 20-second requirement repeatable after the crowd behavior change. Shared enemy prefabs and wave assets were not edited.

## Roadmap adjustment

Navigation, separation and complementary enemy roles remain the coordination foundation. Ordinary melee applies continuous pressure. [M3 ranged positions](RangedPositioning.md), [M4 special coordination](SpecialCoordination.md), [M5 encounter tools](EncounterTesting.md) and [M6 combat refinement](CombatRefinement.md) are implemented. Limits on overlapping major special attacks remain separate from ordinary melee pressure. Production spawning and player combat changes remain later work.

# M3 — Ranged firing positions

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode, and click the Game view. The arena now accepts Input System keyboard shortcuts:

| Key | Action |
| --- | --- |
| F5 | Spawn the Inspector's Selected Role once |
| F6 | Replace the encounter with the seeded melee pressure swarm |
| F7 | Replace the encounter with four crossbows and one heavy ranged enemy |
| F8 | Replace the encounter with six swords, three crossbows and one heavy |
| F9 | Stop further wave spawning and clear arena enemies |
| F10 | M4: six swords, two crossbows, three spinners and both rope elites |

Holding a key does not repeat spawns. Disable **Enable Keyboard Shortcuts** on **Enemy Behavior Playtest** if needed. Inspector buttons still work. No combat HUD or player input bindings were added. Starts retain the existing pose-reset option and do not refill health or energy; restart Play Mode for a fresh player.

## Behavior

Crossbows retain their 5–8 unit firing-distance band. They seek ground positions with clear shots and spread away from nearby shooters during cooldown. Another enemy's body can block a firing line, prompting a sidestep. A ready shot takes priority over better spacing: reaching a reserved slot is never required.

Small crossbow readability tuning: cooldown multiplier **3.2** (previously 2.4), a **1-second attack animation** releasing at **0.7 seconds** (previously 0.42), and projectile-speed multiplier **0.45** (previously 0.6). With the current arm's 0.6 Fire Rate, the gap between attack starts is approximately **5.33 seconds**, up from 4 seconds. Bolts travel 25% slower. Both alternating arms use these settings; player weapon data and heavy ranged tuning are unchanged. Roster setup preserves these defaults when regenerating.

The slower tuning passed all **30 existing ranged/shortcut checks** with the current saved prefabs. Each crossbow released four shots in the twenty-second mixed encounter, down from five in the earlier run, while spacing, navigation and heavy firing remained functional.

Heavy ranged enemies retain their 11–18 unit band and hold effective firing positions. They reposition when too close, out of reach or blocked, rather than continuously following small player movements. Their existing long wind-up and powerful projectile remain unchanged.

Both roles face the player while holding position. Position planning uses the existing NavGrid, ground/obstacle checks, body motor and flow-field fallback. Crowd and other firing goals are soft spacing costs, not exclusive reservations. There is no shared ranged attack budget.

The Behavior Graph still orders approach, attack, release and recovery. `EnemyAttack` remains responsible for weapon data, aim commitment, line checks, projectiles, damage and cooldown. Its firing-line query is reused to evaluate candidate positions. Movement stops during the existing attack wind-up/release; positioning can resume during cooldown.

## Tuning and scope

On **Enemy Graph Controller**, **Use Ranged Positioning** defaults on for Crossbow and Heavy Ranged only. **Ranged Planning Interval** defaults to 0.35 seconds; **Ranged Spacing** defaults to 3 units. Minimum Range and Enemy Agent's Attack Range remain the distance limits. Green selected-enemy Gizmos show a current suggested destination.

Basic melee keeps continuous pressure. [M4](SpecialCoordination.md) adds optional arena coordination around the existing spinner/rope attacks. The orbiting rope attack damages its entire active spin circle, once per target per attack; see [EnemyBehavior.md](EnemyBehavior.md). Production wave weights, player combat and the empty pre-mission gameplay hook are preserved.

## Verification

`EnemyBehaviorChecks.RunRangedBatch` runs in a disposable Unity project. It exercises actual keyboard events, repeatable encounter composition, immediate aiming, unchanged weapon data/telegraphs, distance recovery, crossbow spacing, wall/body obstruction, heavy position holding, target cleanup and a twenty-second mixed encounter.

Headless Unity 6000.5.8f1 passed **30 M3 checks** and **18 melee-pressure checks** using the working project's current enemy prefabs. The nearby-crossbow fixture spread from 1.5 to 3.21 units while both fired. All four ranged actors fired in the mixed encounter. The pressure comparison retained 97 starts and seven simultaneous attackers over twenty seconds, versus 20 starts and two in the reserved comparison.

The full roster regression passed **135 checks**, including the filled spin area, both rope variants, navigation, projectile/melee damage, wave limits and the mixed roster. Its setup regenerates prototype roster/wave assets inside the temporary project only. The earlier focused rope run passed **77 checks**, including center damage, entering the active circle, cover, per-attack deduplication, charge damage and cancellation. These are automated correctness checks; use F6–F8 to judge combat feel while moving and fighting.

## Next

M4 special-enemy coordination is implemented in [SpecialCoordination.md](SpecialCoordination.md), and [M5 encounter testing](EncounterTesting.md) adds configurable named presets. M6 evaluates combat feel, readability and performance. Production spawning integration and new player combat mechanics remain later work.

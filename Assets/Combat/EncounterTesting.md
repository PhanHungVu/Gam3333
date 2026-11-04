# M5 — Controlled encounter testing

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode and click the Game view.

| Key | Action |
| --- | --- |
| Page Down / Page Up | Select the next / previous preset; selection is logged and shown in the Inspector |
| F11 | Start the selected preset using its current asset settings |
| F12 | Replay the last M5 preset snapshot, including its original composition, offsets and starting pose |
| F9 | Stop spawning and clear arena enemies and their projectiles |
| Home | Compare M6 compact melee pursuit with earlier wide approach spacing |
| End | Record role contribution and local planning diagnostics |

The existing F5–F10 controls are preserved. F12 remembers the most recent **M5 preset**; using the earlier test shortcuts does not replace that snapshot. Selection alone never starts an encounter. A fresh F11 start uses asset edits; F12 retains the old snapshot even if the asset or selection changes. Replay still validates the current environment, reporting placements that have become blocked.

## Included presets

Editable assets are under `Assets/AI/Behavior/Encounters` and are assigned to the saved arena in this order:

| Preset | Default composition |
| --- | --- |
| A — Basic swarm | 10 swords |
| B — Melee and crossbows | 10 swords, 2 crossbows |
| C — Spinner pressure | 10 swords, 2 crossbows, 1 spinner |
| D — Heavy ranged | 8 swords, 2 crossbows, 1 heavy ranged |
| E — Large mixed horde | 10 swords, 4 maces, 2 hammers, 2 overhead axes, 4 crossbows, 4 spinners, 2 heavies and both rope elites: 30 actors |
| F — Orbit elite | 8 swords, 2 crossbows, 1 orbit elite |
| G — Throw and pull elite | 8 swords, 2 crossbows, 1 throw/pull elite |

A–D reproduce the planned named compositions. E provides all nine roles. Both rope variants receive their own comparison. These are adjustable tests, not final production-wave compositions.

## Editing a test

Select an asset and edit each group's **Role**, **Count** and optional **Distance** override. A zero group distance uses the preset distance. **Seeded Arc** uses the saved seed, distance and arc; roles are interleaved through the arc so low-count shooters are not placed together at one end. Each point has the existing small seeded position variation. E uses a 24-unit full circle; its slower hammers start at 18 units so they can enter combat alongside faster melee.

Choose **Explicit Positions** to author individual role/offset entries. Offsets use **X = right, Y = forward** from the starting player's pose. Height comes from the floor rather than the offset. Select **Enemy Behavior Playtest** with Scene Gizmos enabled to preview green spawn markers and role labels. Duplicate an asset or use **Create > Lithomachia > Enemy Encounter Preset** for another test, then add it to the arena's **Encounter Presets** array.

The Inspector also provides a named preset dropdown, Start/Replay buttons, the last spawn report and skipped-spawn reasons. Invalid roster entries, blocked/off-grid points, unsupported ground, static body obstructions, overlapping planned enemies and overlap with the player start are skipped without moving the requested point elsewhere. A safety limit of 128 actors bounds accidental huge counts; the report retains the original requested count.

## Reset and scope

Preset starts replace arena enemies, stop its manual wave and remove enemy projectiles in the same scene. Projectile ownership is cached so orphaned shots can be cleared after their shooter has been destroyed. Player projectiles and other scenes are preserved. Controlled preset enemies have loot drops disabled by default; **Disable Preset Drops** can restore drops for that test without editing prefabs.

F11 respects the existing player-pose reset setting. F12 returns to the captured pose for repeatability. Health, energy, equipment, ammo and movement state are not refilled or reset; restart Play Mode for a fresh player after death.

Existing enemy prefabs, pressure-first melee, slower crossbows, special admission and authored attack execution remain in use. `SpawnDirector`, production wave data, other gameplay scenes and the empty pre-mission mission-start hook are unchanged. `EnemyAgent.SetSpawnGroundFallback` only supplies the sampled physical floor before Start so raised-floor spawns keep their height. `MechProjectile.OwnerTeam` is read-only cleanup information and does not change projectile damage rules.

## Verification and combat refinement

`EnemyBehaviorChecks.RunEncounterBatch` runs the saved scene and actual prefabs in a disposable Unity project. It checks the new keys, deterministic layout, snapshot replay after asset edits/player movement, exact A–G compositions, original graph ownership, disabled test loot, placement failures, raised floors, projectile cleanup scope and the thirty-actor horde.

Headless Unity 6000.5.8f1 passed **119 checks**: **35 M5**, **26 special-coordination**, **30 ranged/shortcut**, **18 melee-pressure** and **10 saved-arena** checks, using the working project's current enemy prefabs. All thirty graphs stayed active and finite through a 25-second stationary encounter and a further 15-second repeatable moving-player leg. Spinner, orbit and throw/pull attacks contributed while their special limits held. The original pressure comparison retained 97 attack starts and seven simultaneous attackers in twenty seconds.

**Finding from M5:** the stationary large horde crowded both overhead axes out of attack range; their logged distances were roughly 3.8–4.1 units and their action remained Close to attack. Several individual swords/maces also had no release. During the moving-player leg, every ordinary weapon role attacked, including three axe releases. This exposed the crowd/approach bottleneck addressed in M6.

**M6 — Combat evaluation and refinement** is implemented in [CombatRefinement.md](CombatRefinement.md): compact pursuit, existing-reach axe engagement, stationary ranged turning, and Home/End comparison/report controls. Direct approaches still do not assign a full surround. Use the Game view to judge pressure, readable warnings and escape routes; headless correctness checks do not establish visual readability or a rendered frame-rate target. Production spawning integration and new player mechanics remain later work.

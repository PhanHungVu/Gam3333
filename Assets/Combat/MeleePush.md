# Upright melee push

This experimental branch applies the same reduced upright push to ordinary player melee and sword/mace F-return damage. Remote retrieval and its weapon-specific bonuses remain active; see `RemoteWeaponRetrieval.md`.

## Tuning

`MechCombat` exposes **Melee push on enemies** on the existing player:

- Melee Push: enabled by default; disable to retain damage without the new reaction.
- Melee Impulse Speed: **6.666667m/s**, retaining the previous branch's reduced horizontal force.
- Melee Push Seconds: **0.9s**, with linear deceleration. An unobstructed isolated hit covers approximately 3m.
- Melee Kill Impulse Speed / Melee Kill Lift Speed: **18m/s horizontal / 7m/s upward**, retaining the stronger killing launch.

Living enemies stay upright and grounded. Their original model, hitboxes, health, targetability and behavior graph remain in place. `EnemyAgent` moves the whole actor along the captured strike's forward direction, temporarily overriding steering without imposing a knockdown animation or stopping the attack graph. There is no temporary ragdoll, added Rigidbody, torque or surviving-hit lift. Repeated pushes refresh the timer and combine velocity up to a 22m/s cap. Normal movement resumes when the push ends. Actor disable/pooling clears the impulse.

Forced motion sweeps the existing body radius against scenery, ignores the actor, damageable actors, corpses and loose pickups, and stops before a wall. It uses the existing ground following and observes combat hitstop. A terminal mission outcome clears the push.

Killing melee hits use the existing death snapshot and cleanup budget, with **one body and locked rotation** driving the entire model. This preserves the kill pose and launch direction without joints or tumbling. Other deaths keep their existing presentation. The override is scoped around the damage call and cleared even if damage is rejected, so a nonlethal melee hit cannot change a later unrelated death.

## Damage and loot

Box swings and swept F-return capsules share `CombatBlast.ApplyMeleeContact`. The effect applies only for a melee source owned by a player `MechCombat`, against an `EnemyAgent`, after ordinary damage acceptance. Swings use the captured attack forward; retrieval uses the actual physical travel segment toward the player. One accepted hit applies one push, with no second legacy knockdown call. Wall checks, team rules, compound-collider deduplication, damage, reach, cooldowns and existing hit feedback retain their current owners. Enemy attacks and other box/blast damage do not receive the player shove.

The death handler resolves configured loot first, once per life, regardless of death-listener ordering. Successful loot spawns unparented with only its own upward pop. The matching carried weapon's visual duplicate is hidden on the corpse. Corpse colliders exclude the loose-part layer, so bodies cannot knock pickups away. The configured drop chance/list remain authoritative; pooling resets the per-life guard.

## Files and verification

- `Scripts/Combat/MechCombat.cs`: shared push and killing-launch tuning.
- `Scripts/Damage/CombatBlast.cs`: accepted melee-contact dispatch and scoped death preparation.
- `Scripts/AI/Behaviour/EnemyAgent.cs`: bounded, collision-aware upright push using the existing motor.
- `Scripts/Presentation/CombatDeathRagdoll.cs` / `CombatRagdoll.cs`: pose-locked whole-model killing launch; ordinary death behavior remains available.
- `Scripts/AI/Drops/Enemypartdrop.cs`: independent loot before launch, once per life.
- `Tests/Editor/MeleePushChecks.cs`: isolated Unity tests using real player, sword/mace and enemy prefabs.

Merge resolution: the merge retained both melee implementations, duplicating `meleeImpulseSpeed`, `meleeKillImpulseSpeed` and `meleeKillLiftSpeed`, and calling the obsolete knockdown on top of ordinary pushes. The duplicate/old settings and unused `EnemyMeleeReaction` component were removed. No scene or prefab referenced that component. Both melee routes now use the upright system; the old `meleePhysics` toggle migrates to `meleePush`. The obsolete ragdoll-specific test entry point forwards to the current push suite, which also covers actual F-return sword/mace hits, live enemy attack/graph ownership, killing launches and independent loot.

Merge validation: **1,449 checks passed**, all exit 0 in isolated Unity 6000.5.8f1: 49 upright-melee/F-return checks, 37 real-input pickup checks, 62 retrieval-travel checks, 65 retrieval-combat/cooldown checks, 72 anticipation/cannon checks, 43 axe-barrage checks, 76 speed-buff checks, 48 piercing-bolt checks and 997 core gameplay checks. The F-return fixture preserves the player's motor-grounded pivot height; spawning that pivot at Y .1 incorrectly buries its sockets and rightly blocks the pickup against the floor. No production targeting/travel change was needed. Logs: `.quest-review/Results/RetrievalMergeRepair`.

Validation in isolated Unity 6000.5.8f1: **35 focused push checks and 997 core gameplay checks passed**, exit 0. These cover actual sword/mace attacks, initial strength, real displacement without rotation, targeting/hitbox preservation, recovery, rotated strikes, bounded repeats, pooling, scenery blocking, team rejection, the Inspector toggle, killing velocity/pose, independent loot, ordinary later deaths, multiple enemy models and the actual behavior graph.

The legacy death/mission integration suite also passed **112 checks**, exit 0, with one review-fixture compatibility adjustment: its old head-input test assumes no gameplay ability listener, whereas this branch implements `MechAbilities`. The unmodified suite stops on that obsolete assumption. Only the disposable test actor's ability driver was disabled for the legacy hook/cooldown assertions; production abilities, HUD and tests were left unchanged. The remaining actual death, corpse cleanup/collision, loot, mission failure/success, extraction, retry and menu callbacks ran normally. The modified fixture and initial failure log are archived alongside the passing logs in `.quest-review/Results/UprightMeleePush`; the private test source is restored afterward.

Manual check: restart Play Mode on this branch. Hit an enemy with a sword or mace: it should slide in the strike direction while staying upright. Kill it: the whole body should fly farther without rotating, and a successful loot drop should stay behind. Repeat near a wall and with several enemies. Settings apply through the shared scripts; no scene-specific force controller is required. HUD, audio, input and part swapping are unchanged.

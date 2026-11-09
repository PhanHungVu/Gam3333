# Melee timing, small-enemy bodies and rope-pull contact

Melee Fire Rate now controls the playback speed of the full swing: `swing seconds = 1 / FireRate`. Sword and mace Fire clips fill that interval, including the return movement, rather than finishing at authored speed and waiting in Neutral. There is no additional recovery cooldown after that swing. Stats, damage and range remain on the existing PartData assets; ranged fire/reload rules are unchanged.

`MechWeaponAnimator` applies this to every equipped Melee subtype. Player admission still uses the physical Part's state and cannot interrupt a visual swing extended by actor hitstop. Player melee retains its existing immediate/reach-assisted hit rule and current weaker upright push; this change does not add stun or tumbling.

`EnemyAttack` records each lane's full swing duration. Its telegraph, release fraction, weapon playback and attack completion use that same duration. Enemy cooldown multipliers therefore slow/speed the swing itself. The throwing-axe enemy's existing overhead melee override follows this rule, while the player's projectile axe remains ranged. Enemy AI still decides when to request attacks; no second attack controller was introduced.

All small-enemy prefabs now use the imported Fodder body; see [FodderModelIntegration.md](FodderModelIntegration.md) for mounts, presentation and authoring tools. Existing prefab references, enemy roles, weapons, stats and drops are retained.

## Throw-and-pull damage

`EnemyRopeAttack` previously damaged only on the rock's landing. Its ThrowAndPull variant now also sweeps the enemy's actual motor displacement during Stage.Pull. Targets in the body path take the existing attack Damage once per pull. Landing damage is still a separate attack contact.

The Inspector exposes `Pull Contact Radius` (1.1 m half-width) and `Pull Contact Half Height` (1.4 m). The sweep covers travel between frames, uses shared CombatBlast team/wall rules and a per-pull target history, and never applies damage during pause, recovery or cancellation. It follows actual movement after collision handling, rather than extending a damage lane through a blocking wall. The orbiting variant's damage rules are unchanged.

The pull now clears its directed velocity at the existing two-metre stopping clearance through `EnemyAgent.StopDirectedMovement`, preventing residual braking motion from sliding it past the rock. Losing the live target cancels an executing rope attack, and the tether hides while no target exists, including idle/recovery transitions.

## Verification

Final validation: **1,190 checks passed** (124 focused saved-asset checks, 1,010 foundation checks, 35 existing melee force/death/loot checks and 21 existing throw/pull checks), all exit 0. The small-enemy model previews were visually reviewed. Production runtime scripts, saved enemy prefabs and source graphs/mission data were restored into the isolated project before the final focused and foundation runs.

Isolated Unity 6000.5.8f1 checks cover slow/fast player swings, hitstop, all six roster roles, actual enemy release timing, live graphs, pooling, spinner presentation, real graph-driven pulling, fast movement sweeps, repeated contacts, wall/team/height rules and pause/cancellation. Existing clip-pose and enemy-hit tests now sample the new normalized swing time instead of assuming an authored-speed swing. `MeleeRosterAndPullChecks.RunBatch` also checks repeated authoring; `RunSavedBatch` verifies the saved production assets without changing them. `EnemyBehaviorChecks.RunPullOnlyBatch` runs the existing throw/pull collision, recovery, cancellation, death, raised-floor and initial-overlap cases independently.

Logs are in `.quest-review/ReadabilityProject/`: `melee-roster-pull-checks.log`, `melee-fodder-foundation.log`, `melee-fodder-push-checks.log` and `melee-fodder-pull-regression.log`. Model previews are in `MeleeRosterCaptures`. Production scenes, HUD, input bindings, audio assignments, damage numbers and spawn weights are unchanged.

A separate legacy orbit-charge braking fixture still fails: when its player moves four metres in front of the deployed enemy, the fixture observes zero final-approach displacement. This also reproduced with the original branch's `EnemyRopeAttack`, before this task's changes (`unchanged-orbit-baseline.log`). A proposed final-approach fallback did not resolve the fixture and was removed. Active orbit movement/damage logic and its production tuning are retained. The baseline fixture used actual observed revolutions rather than assuming every editor callback advances an attack frame, allowing the later braking assertion to be evaluated. This separate issue needs follow-up; the combined legacy rope/orbit suite is not recorded as passing.

Manual check: restart Play Mode. Hold either melee attack and compare low/high Fire Rate; the entire swing should change speed with no idle wait afterward. Spawn the small roster in Zoo/EnemyBehaviorPlaytest to review each Fodder weapon mount. Let the pull elite throw, then step into the lane between it and the landed rock; its travelling body should hit once on that pull.

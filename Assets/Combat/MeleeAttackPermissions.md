# M2 — Coordinated melee attack permissions

**This is now an optional comparison mode.** The current default is [Continuous melee pressure](MeleePressure.md), which lets ready melee enemies attack without shared permissions or waiting positions. Use **Start Pressure Swarm** for current playtesting; the older modes below are under the Inspector's **Earlier behavior comparisons** foldout.

Open `Assets/Scenes/EnemyBehaviorPlaytest.unity`, enter Play Mode, and select **Enemy Behavior Playtest**.

| Inspector button | Comparison |
| --- | --- |
| Baseline | Original approach and independent attacks |
| Reserved Positions | M1 local positions, independent attacks |
| Reserved Positions + Attack Budget | M1 positions plus M2 attack permissions |

All three use the same count, seed and spawn layout. Player health/energy are not reset by these buttons; restart Play Mode for a fresh player. No combat HUD has been added.

## Attack rules

In Reserved Positions mode, `EnemyHordeCoordinator` can use a basic-melee permission pool with separate budgets for each target. That optional comparison uses **two simultaneous commitments**. A commitment includes the existing weapon wind-up, hit release and attack animation; the reservation ends before the rest of the weapon cooldown. Existing damage, cooldown, animation timing and hit checks remain in `EnemyAttack`.

Ready attackers are selected in request order. Repeated polling does not create extra entries or move an actor to the front. A new attack goes behind existing waiters. Older actors that cannot currently reach their position/target do not block ready attackers. This permits overlapping threats without forcing enemies to attack before their cooldowns finish.

Denied actors keep approaching their assigned positions. Inner occupants can make small, validated lateral movements while waiting; outer actors continue positioning. The Behavior Graph still owns all behavior decisions and the agent still owns movement.

Sword, Mace, Hammer and the overhead Throwing Axe variant can participate when an `EnemyCoordinationMember` is explicitly bound to the coordinator. Spinner, Crossbow, Heavy Ranged and both rope variants never consume this basic-melee pool. Their separate coordination is later work.

## Tuning

Select **Enemy Horde Coordinator** on the arena controls:

- **Use Attack Permissions:** switch between M1 and M2 during a fight.
- **Maximum Melee Attacks:** default 2, adjustable from 1 to 8. Try 3 for comparison. Lowering the limit blocks new grants while existing commitments finish.
- **Melee Start Spacing:** default 0.18 seconds between grants against the same target. This staggers wind-ups while allowing overlap.
- **Unstarted Grant Lifetime:** default 0.5 seconds; a grant that is not consumed returns to the queue behind existing waiters.
- **Maximum Reservation Hold:** default 4 seconds, extended to at least the authored attack duration plus 0.5 seconds. A stuck commitment is cancelled without refunding its weapon cooldown.

Inactive requests expire after a short heartbeat timeout. Completion, failed starts, explicit cancellation, death, member removal, graph shutdown, target changes and unavailable targets return capacity. Coordinator shutdown and attack-mode changes cancel owned unreleased hits. Enabling M2 during an existing M1 fight cancels ongoing unreserved melee swings before adopting the fight; spent cooldowns remain spent.

The arena Inspector shows committed attacks and queued requests. Scene Gizmos show orange inner positions, cyan outer positions, yellow grants and red commitments. Individual members expose **Attack Permission Status**. Reservation maintenance runs independently of new attack requests, so cleanup still works when the last target disappears.

## Scope and validation

The feature remains opt-in to the combat comparison arena. Enemy prefabs, production wave presets, spawn costs, objectives, player controls and the pre-mission mission-start hook are unchanged. Budgets are separated by target; networking and multiple navigation goals remain future work.

`EnemyBehaviorChecks.RunAttackCoordinationBatch` checks real graph-driven ten-sword encounters at limits 1, 2 and 3, movement while waiting, cooldown preservation, reservation ownership, cancellation/death/graph/target cleanup, live mode switching and role exclusions. Deterministic clock checks cover queue order, duplicate requests, readiness, per-target budgets, start spacing, grant expiry, abandoned requests and watchdog cancellation.

Final validation in headless Unity 6000.5.8f1 passed **41 M2 checks**, **28 M1 compatibility checks** and **123 full-roster regression checks**. The default 25-second ten-sword encounter peaked at two simultaneous attacks; nine different swords attacked from all four directions, and waiting movement was observed. Limits 1 and 3 were also exercised; capacity is an upper bound, not a requirement to fill every attack slot. Duplicate attack polling preserves an active swing, and repeated commitment acknowledgement cannot extend its watchdog deadline.

## Next milestone

**M3 — Ranged firing positions** is implemented in [RangedPositioning.md](RangedPositioning.md), including keyboard test encounters. **M4 — Special-enemy coordination** is next. The full encounter suite and combat refinement follow; production wave integration and player combat changes remain later work.

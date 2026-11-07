# Directional melee force and knockdown

Player melee weapons now apply physical force after an accepted damage hit. This covers ordinary sword/mace swings and their damaging F-retrieval paths. Damage, range, cooldowns, team checks and wall rejection stay with the existing combat systems. Rejected damage, ordinary non-damaging pickup and ranged attacks grant no melee impulse.

## Direction and current tuning

- Ordinary swings push along the attack box's captured forward direction, including a rotated player.
- F-returning swords and maces push along the accepted physical flight segment toward the player. They can knock an enemy into the player rather than always pushing away from the player's position.
- Surviving enemies receive a **6.667m/s planar impulse plus 2m/s lift** (two-thirds of the original 10/3 tuning), with a **0.9s minimum knockdown** before grounded recovery. An airborne body gets up to another 1.5s to settle before recovery.
- Killing blows use a separate **18m/s planar launch plus 7m/s lift**. This overrides the old generic death kick so an enemy's chase velocity cannot cancel the melee launch.
- Repeated hits reuse the same physical rig and refresh the knockdown. Combined body velocities are bounded at 22m/s, with bounded tumble speed. Damage and force are deduplicated by the same existing per-hit/per-return-path health history.

Tune these settings on the player's **Mech Combat → Melee impact on enemies**: Melee Physics, Melee Impulse Speed, Melee Lift Speed, Melee Knockdown Seconds, Melee Kill Impulse Speed and Melee Kill Lift Speed. Defaults apply through the shared script on existing player prefabs/scenes; no scene-specific controller is installed. Turning Melee Physics off preserves normal damage.

The surviving-hit push/lift were reduced by one-third at the user's request: 10 → 6.667m/s push and 3 → 2m/s lift. This applies to both ordinary melee and F-returning swords/maces. The values are saved explicitly on the normal golem, Heavy demo and Specialist player prefabs so imported/cached defaults cannot retain the old strength. Killing-blow launch (18/7), knockdown timing, damage and cooldowns are unchanged. Restart Play Mode to spawn the updated player configuration.

## Living enemy behavior

`EnemyMeleeReaction` is attached to an enemy on its first accepted player-melee hit. It reuses `CombatRagdoll`'s renderer-only snapshot, connected joints and uniform world-space physics proxies. The original actor stays alive: it keeps its existing CombatHealth, loot/death listeners and wave bookkeeping. The physical copy creates no additional health, Parts, pickup scripts or loot entities.

The original model and collision hitboxes are temporarily hidden/disabled. The physical hitboxes are children of the original actor on the EnemyBody layer, so damage queries still resolve to that same living health. Lock-on aims at the physical core's centre, and the logical actor follows its centre of mass. Simulated bodies are temporarily detached while the logical root catches up; temporary proxies use native physics poses without Rigidbody interpolation, avoiding interpolation/transform-sync feedback that reduced knockback travel.

Only existing control/animation components pause: movement, graph/attack execution, coordination, weapon posing/telegraphs, authored Animators and native navigation. Pending attacks are cancelled through their normal disable hooks. Already-fired projectiles remain independent. Health, drops, death effects and mission/wave bookkeeping keep running.

After the minimum interval and landing (or the bounded extra wait), the actor stands on terrain near the physical body's final location. A capsule clearance query checks scenery, searches nearby alternatives when needed and falls back to the original location if no nearby standing position is clear. Original renderers, collider states and previously enabled controls are restored. Previously disabled controls remain disabled. Collision exclusions against existing corpses are restored too. A terminal mission outcome cannot re-enable enemy movement/attacks/graphs.

## Death, pooling and physics limits

If an enemy dies while knocked down, `CombatDeathRagdoll` promotes the **same physical rig** to an ordinary corpse, detaches it from the dead actor and restores the corpse query layer/lifetime. It does not spawn an upright duplicate. The original health still raises one death event and the normal part-drop code runs once. The accepted melee killing hit then applies its stronger directional launch through `LastRagdoll`.

Loot is resolved **before corpse construction/promotion and the killing launch**, independent of death-listener subscription order. `EnemyPartDrop.ResolveDeathDrop()` rolls/spawns at most once per life; its own death callback can safely run afterward. Re-enabling a pooled enemy resets that guard. The configured chance and weighted prefab list still determine whether and which part drops.

A successful pickup spawns unparented at the death location with its configured upward pop only, without inheriting enemy or corpse momentum. A knocked-down enemy first synchronizes its logical position to its physical body. Every ragdoll proxy excludes the `MechPart` collision layer, so existing and future drops still fall onto terrain but cannot be batted away by live knockdown rigs or launched corpses. If the selected loot matches a carried part, only that part's renderer copy and associated collider in the dead snapshot are hidden; the original pooled enemy remains intact. Failed rolls leave the corpse's carried weapon visible.

Disabling or pooling a living enemy disposes its temporary body and restores its authored states. Disabling the reaction alone recovers the living actor safely. Corpse behavior remains the existing bounded cleanup system; ordinary non-melee deaths keep their existing kick.

Temporary articulated knockdowns have their own budget, separate from corpses: initially 12 simultaneous full rigs with at most 14 bodies each. Additional knockdowns can use a single box/body driving the **entire** visual copy, up to 24 temporary entities. If no visual rig is available or that budget is full, the existing actor receives bounded swept root knockback and then resumes its controls. These fallbacks keep the hit responsive without allowing unlimited full ragdolls. Tune Impulse Multiplier and physics limits by adding/configuring Enemy Melee Reaction on an enemy prefab; otherwise its defaults are installed on first hit.

## Returning-weapon anchor correction

The damage path now reports `RemotePartRetrieval.ActivePickupPoint` before and after each accepted movement segment, including rotation/scale changes, rather than the imported weapon's root pivot. The departure overlap uses that same physical point. The mace's root and visible/collider anchor are offset: in the grounded test its root was Y 1.643 while its pickup point was Y 0.837. Following the root could miss a ground enemy despite the visible mace travelling through it. The physical anchor fixes that damage/force mismatch without changing socket poses, visual scale, hit radius or damage values.

## Files and verification

Loot-before-launch validation: **61 focused melee-physics checks, 982 gameplay regressions and 151 death/mission integration checks passed (1,194 total)**, all exit 0 in isolated Unity 6000.5.8f1. Runtime checks cover loot resolving before the launch even when its own death listener runs later, no inherited horizontal velocity, real physics leaving the pickup at the kill location, ragdoll/loot collision exclusions, hiding the matching corpse weapon, no duplicate rolls, failed rolls, pooled reuse, already-knocked-down deaths, and sword/mace F-return kills. The graphics capture was inspected. Logs/captures: `.quest-review/Results/MeleeLootBeforeLaunch`. Drop chance, melee damage/force tuning, cooldowns, HUD and audio were not changed for this fix.

- New: `Scripts/AI/Behaviour/EnemyMeleeReaction.cs` and `Tests/Editor/MeleePhysicsChecks.cs`, with metadata.
- `MechCombat`: shared tuning and accepted player-melee impulse dispatch.
- `CombatBlast`: accepted box/path hits forward their actual incoming direction after damage succeeds.
- `CombatRagdoll`: bounded temporary mode, core/aim point, bounded impulses, one-body whole-model fallback and corpse promotion.
- `CombatDeathRagdoll`: exposes the current corpse and promotes a knocked-down body on death.
- `EnemyPartDrop`: per-life death-drop resolution before corpse creation/launch, independent pickup velocity and matching carried-part identification. `CombatRagdoll` removes that duplicate from the corpse and excludes loose-loot contacts; `EnemyMeleeReaction` synchronizes the downed death location first.
- `MechTargetable`: temporary physics aim override, restoring its authored aim target on recovery.
- `RemotePartRetrieval` / `RetrievalCombatEffects`: physical-anchor return-path segments and departure overlap.

Two-thirds tuning validation: **52 focused melee-physics checks passed**, exit 0 in isolated Unity 6000.5.8f1. Normal sword strikes and both F-returning melee weapons use the reduced 6.667/2 impulse; the same suite confirms unchanged stronger killing launches, recovery, targeting, drops and physics cleanup. Log: `.quest-review/Results/MeleeForceTuning/melee-force-tuning.log`.

Original integration validation: **52 focused melee-physics checks, 982 gameplay regressions, 65 retrieval-combat checks, 62 ordinary retrieval checks, 72 anticipation/cannon checks and 151 death/mission integration checks passed (1,384 total)**, all exit 0 in isolated Unity 6000.5.8f1. The focused suite uses the real Specialist/sword/mace and actual Sword, Crossbow, Mace, Fodder and RockRope enemy prefabs. It verifies strike rotation, surviving force and follow-up damage, bounded repeat impulses, targeting/position following, renderer/collider restoration, corpse collision exclusions after recovery, stronger killing launches, one death/guaranteed loot event, in-place corpse promotion, both F-return directions and killing blows, wall/team rejection, the Inspector toggle, pooled disable/re-enable, the single-body whole-model fallback, rendererless root knockback, real behavior-graph restart and terminal mission cleanup. Focused graphics captures were inspected. The ordinary retrieval suite was rerun with graphics enabled after its screenshot request crashed the null-graphics renderer; the graphics run completed successfully. Logs/captures: `.quest-review/Results/MeleePhysics`. Live horde balance and force tuning remain a playtest.

Manual test: restart Play Mode, hit an enemy with a sword/mace while facing different directions, then kill it and compare the stronger launch. F-retrieve a grounded sword and mace with enemies between the drop and player: the enemies should move along the returning weapon's direction. A surviving enemy should remain targetable, take follow-up damage and resume its existing AI after recovery. Kill one while it is down and confirm a single corpse and ordinary loot. The current temporary retrieval-bonus cooldown bypass remains enabled for these repeated F tests; the 20m retrieval range and normal weapon cooldowns are unchanged.

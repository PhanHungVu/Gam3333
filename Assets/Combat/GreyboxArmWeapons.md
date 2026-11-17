# Greybox arm weapons 5–13

All nine prototypes use ordinary `PartData`, `Part`, arm sockets, `MechCombat`, shared damage rules, magazines/reloads and the existing HUD. Original arm weapons 1–4, their definitions/prefabs, enemy AI/waves and production drop tables are unchanged. These prototypes have one Cube model each and ordinary Sphere projectiles, with simple colored materials and generated flat-color HUD icons. They have no authored animations, sounds or new polished effects. Existing shared combat feedback can still react to their hits.

## Exact design stats

Ammo includes loaded rounds, matching the existing system. Fire Rate means attacks/second. Range converts using the equipped rules config (currently 4 metres per golem). N/A ammo/magazine/reload entries are omitted from melee definitions.

| ID | Weapon | Damage | Splash | Ammo | Magazine | Fire Rate | Reload | Range (golems) | Splash radius (m) | Speed % |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 5 | Minigun | 9 | 0 | 400 | 50 | 10 | 2 | 4.5 | — | -5 |
| 6 | Hook | 30 | 0 | 30 | 1 | 0.2 | 0.7 | 8 | — | -3 |
| 7 | Hammer | 100 | 50 | — | — | 0.7 | — | 1 | 3 | -3.5 |
| 8 | Spear | 90 | 90 | — | — | 1 | — | 3 | 0.5 | -4 |
| 9 | Shotgun | 150 | 0 | 50 | 5 | 0.8 | 3 | 6 | — | -3 |
| 10 | Supersword | 250 | 250 | — | — | 0.35 | — | 2.5 | 2.5 | -7 |
| 11 | Scythe | 100 | 100 | — | — | 0.65 | — | 1.5 | 1.5 | -4 |
| 12 | Punchy Arm | 30 | 30 | — | — | 2 | — | 0.7 | 0.5 | -1.5 |
| 13 | Metal Blades | 60 | 30 | 40 | 3 | 1 | 2 | 8 | 0.5 | -3 |

## Assets

Definitions are under **`PartCatalogue/GreyboxArms`**, prefabs under **`Prefabs/Mech/GreyboxArms`**:

| Weapon | PartData file | Prefab file |
|---|---|---|
| Minigun | PartData_Arm_Minigun.asset | arm_Minigun.prefab |
| Hook | PartData_Arm_Hook.asset | arm_Hook.prefab |
| Hammer | PartData_Arm_Hammer.asset | arm_Hammer.prefab |
| Spear | PartData_Arm_Spear.asset | arm_Spear.prefab |
| Shotgun | PartData_Arm_Shotgun.asset | arm_Shotgun.prefab |
| Supersword | PartData_Arm_Supersword.asset | arm_Supersword.prefab |
| Scythe | PartData_Arm_Scythe.asset | arm_Scythe.prefab |
| Punchy Arm | PartData_Arm_PunchyArm.asset | arm_PunchyArm.prefab |
| Metal Blades | PartData_Arm_MetalBlades.asset | arm_MetalBlades.prefab |

`ArmWeapons5-13.asset` in the data folder is the bench's catalogue. `Combat/GreyboxArms` contains primitive materials and tiny generated color-square icons; no imported artwork or custom shader was added. Every definition sets `PartType.Arm` and `Restrict To Part Type`, so even a mixed-type test rules config rejects shoulders and other non-arm sockets. Ordinary existing mixed-type behavior remains available to older parts.

Cube geometry is offset beyond the socket so it stays visible outside the golem's arm. Authored Scale 2 compensates the existing players' Part Scale 0.5, keeping primitive dimensions readable both equipped and loose. Muzzles sit just beyond the cube's tip. These visual choices do not scale weapon damage or reach.

## Attack implementation and temporary choices

**Minigun** follows the existing small-crossbow ranged path. Held LMB/RMB uses the existing repeated-input handling. A 50-round magazine and reserve ammo were already supported; no new magazine framework was required. The firing interval is 0.1s. Reloads preserve the current convention: the magazine count refills when reloading starts, while attacks remain locked until completion.

**Shotgun** extends the same ranged path to launch **6 pellets sharing 150 total damage** (25 each at base stats). One trigger consumes one round, one cooldown cycle and one weapon-attack event; each pellet remains an ordinary swept projectile. `GreyboxArmWeapon.pelletSpreadAngle` is an independent **8-degree physical cone half-angle**. Head Accuracy shifts the central aim point once per blast, then all six pellets spread independently around that shared direction. The intrinsic cone receives neither the quarter-radius multiplier nor distance tightening. Perfect head Accuracy centers the blast but leaves the full pellet cone intact. The existing crosshair still displays a conservative reference envelope including intrinsic spread.

Ordinary shots use the larger 10-degree head crosshair reference with **quarter-radius** shot error; the Minigun uses **half-radius**. Both tighten with muzzle-to-contact distance divided by weapon Range. For Shotgun, those rules affect only the shared blast-center error; the independent pellet cone remains 8 degrees at every distance. See [Accuracy and range tuning](AccuracySpread.md) and [balance/piercing/hitbox review](WeaponBalanceAndHitboxes.md).

**Hammer, Spear and Supersword** reuse `MeleeAttack` with per-prototype width/height and exact stat reach. Initial widths are **1.8m, 0.7m and 4.5m** respectively; height is 3m. The direct box starts at the player and extends forward by `Range × metresPerGolem`. A secondary sphere at the tip applies Splash Damage inside the specified radius. Targets already damaged by the direct box are excluded from that strike's splash; compound colliders never multiply hits. This avoids silently turning equal Damage/Splash values into double damage for every melee hit. Both regions share existing walls, teams, melee push, death-launch and hit-feedback rules. No lunge, charge or animation was added. Supersword's updated 0.35 Fire Rate means approximately 2.857 seconds between activations.

**Scythe** uses the same box/sphere path with a 3.5m-wide sweep and **10% lifesteal**, configurable on its component. Healing uses confirmed enemy HP removed, excluding overkill, rejected damage, whiffs, friendlies and neutral props. It is capped by the attacker's existing `CombatHealth.Heal`; dead/disabled players cannot heal. Test against actual enemies or Enemy-team dummies.

**Punchy Arm** uses the same melee damage path for **three 30-damage hits at 0s, 0.2s and 0.4s**. One activation consumes the existing 0.5s attack interval. Each punch gets a fresh victim history; a target can be hit once per punch. Sequences cannot overlap, and swapping/disabling cancels unfinished punches. Spacing is configurable and bounded to fit the physical part's current attack interval, including stat rolls. Its direct width starts at 1m. Melee hitstop pauses the sequence through the existing combat tick.

The existing shared melee sequencer admits one combo at a time per player. Other ranged weapons can still fire during a combo; independent simultaneous melee combo tracks are outside this greybox pass.

**Hook** follows the existing ranged path. A confirmed solid-surface or valid-target impact requests motion from `MechMovementController`; it never directly changes the player transform. Temporary settings are **24m/s**, **2m stopping distance**, **2s timeout**, exposed on the weapon. The existing CharacterController resolves walls, ceilings and ground. Reaching the stop distance, being blocked, timeout, unequipping, disabling, knockback or a dash command ends the pull. A launched Hook cannot start pulling after its source has been dropped/swapped. Current dash behavior is retained; no Assault Boost binding or mechanic was added. Fire Rate 0.2 means the existing cooldown waits five seconds even though the one-round reload lasts only 0.7s.

**Metal Blades** uses the existing Splash projectile mechanism as the permitted shrapnel greybox fallback: **one 0.5m AoE**, without separate shard projectiles. The directly struck target takes **60 + 30 = 90**, following existing ranged-splash conventions; other enemies in range take 30. Surface impacts can also splash. There is no second explosion callback to duplicate the damage.

Only unspecified projectile tuning uses placeholders:

| Weapon | Speed (m/s) | Collision radius (m) |
|---|---:|---:|
| Minigun | 80 | 0.06 |
| Hook | 60 | 0.15 |
| Shotgun | 75 | 0.09 |
| Metal Blades | 65 | 0.12 |

These values live in each definition's Projectile Speed/Projectile Size stats. Colors and cube proportions are temporary. They are not new balance specifications.

## Quick testing

### Zoo: individual ground pickups

`Scenes/ZooScene.unity` now places **nine independent prefab instances** under **parts → Arm Weapons 5-13 - Ground Pickups**, to the right of the original Zoo weapons. They form a labelled 3×3 grid (visible centers X 25/31/37, Z -52/-45/-38), with room between items. Each is its own physical `Part`, Rigidbody, collider, data definition and cube model. The parent only organizes the scene; it is not a combined weapon or runtime spawner.

Walk up to an item and use the existing **1 / 2** controls to equip it in the left/right arm. Use the existing **V** selection cycling if another dropped item is also nearby. The new ground items have the scene-only **Keep Authored Stats** override so Zoo tests retain the exact design table after physics settles and after pickup/drop. Other prefab instances retain ordinary loot-roll behavior. All existing Zoo weapon/body-part pickups remain available.

The Zoo's old F-key pickup caption now shows the actual 1/2 controls. Each new item has a dark ground label for readability on the light floor.

The optional authoring tool **Tools → Mech → Greybox Arm Weapons → Place Individual Pickups in Zoo** refreshes these placements without duplicating them, with ZooScene open outside Play Mode. The inspector test bench remains available separately in the combat arena; it is not required in the Zoo.

### Combat arena: optional inspector bench

1. Open **`Scenes/EnemyBehaviorPlaytest.unity`** and enter Play Mode.
2. Select **Greybox Arm Weapons 5-13** in the Hierarchy. Its Inspector has Left, Right and Drop buttons for every weapon.
3. In the Game view, **[ / ]** selects the previous/next prototype, **comma** equips left, **period** equips right, and **backslash** spawns the selected pickup. Selection is logged to the Console. LMB/RMB fire normally; number-key swapping and reload remain their existing systems.
4. The existing arena enemy controls remain: **F5** selected enemy, **F6** melee pressure, **F7** ranged, **F8** mixed, **F9** clear, **F10** specials, **F11/F12** selected/repeated encounter. These were not changed.
5. Use **Clear Loose Bench Weapons** to clear test copies left on the floor. It leaves equipped parts alone.

Bench spawns call `Part.KeepAuthoredStats()` before the new item's first Start so the first balance test uses exactly the table above, without the global ±10% loot variation. This cannot reset an already prepared item's stats, ammo or timers. Ordinary world/enemy loot retains its existing roll behavior. Starting/bench equipment and new weapons are not added to production drops.

To regenerate assets, use **Tools → Mech → Greybox Arm Weapons → Create or Update Weapons 5-13** outside Play Mode. This writes only the owned prototype definitions/prefabs/materials/icons/catalogue and restores the numeric design table; existing prototype special-mechanic tuning is retained. **Connect Bench in Current Test Scene** adds/reconnects the opt-in testing component in another test scene. No debug overlay or production HUD layout was added.

This checkout has nearby 1/2 part swaps and no remote F-retrieval system. The prototypes therefore have no retrieval-specific attacks. Their ordinary Part identity/physics and compatibility rules remain suitable for later retrieval integration.

## Code and verification

New runtime code: `GreyboxArmWeapon`, `GreyboxArmWeaponCatalogue`, `GreyboxArmWeaponBench`. New editor code: `GreyboxArmWeaponSetup`, `GreyboxArmWeaponBenchEditor`.

Extensions: `MechCombat` (pellet fan-out, Hook impact routing, spread display), `RangedAttack` (optional no-extra-ammo pellet launches), `MechProjectileImpact` (hit collider), `MeleeAttack` (prototype geometry/AoE/lifesteal/triple strike), `CombatBlast` (shared melee sphere and confirmed actual damage), `MechMovementController` (collision-aware Hook request), `Part` (exact-stat grants), and `PartData`/`MechRulesConfig` (strict prototype slot compatibility). Older call sites keep their existing defaults. `MechProjectileModelChecks` explicitly disables head spread in its exact-model/damage fixture; Accuracy has its own randomized gameplay tests.

Historical initial greybox validation in isolated **Unity 6000.5.8f1**, before the subsequent balance/piercing pass below. Current balance and final checks are documented in [WeaponBalanceAndHitboxes.md](WeaponBalanceAndHitboxes.md):

- `GreyboxArmWeaponChecks.RunBatch`: **152 checks**, covering all definitions, primitive assets, both-arm/shoulder rejection, exact test spawns, speed penalties, 50-round automatic reload, manual reload and swap persistence, shotgun pellet count/60 total damage, melee dimensions/AoE/walls, actual-damage lifesteal, punch timing/cancellation, one Metal Blades splash, and Hook surface activation/wall stopping/unequip cancellation.
- `GreyboxArmWeaponArenaChecks.RunBatch`: **45 checks**, using the real existing scene/player, all nine weapons' HUD icons and cooldowns, real keyboard bench shortcuts, physical exact-stat pickups, sustained normal LMB firing and release. Its Direct3D 11 player-attachment preview was visually reviewed (`.quest-review/ReadabilityProject/greybox-arms-equipped.png`). Simulated input is routed to the disposable Game view; no project input settings or production actions are modified.
- `MechFoundationChecks.RunBatch`: **1,012 existing combat, equipment, movement and HUD checks**. The model/damage fixture explicitly disables randomized aim for deterministic hit verification.
- `MeleePushChecks.RunBatch`: **35 existing melee-force/death/drop checks**.
- `AccuracySpreadChecks.RunBatch`: **43 existing spread/cursor checks**.

All five final runs exited with code 0: **1,287 checks**. Source hashes of the 18 relevant code/scene/test files and all 74 generated asset files matched the passing review project. No full standalone player build was run.

The subsequent Zoo placement is covered by `GreyboxZooPickupChecks.RunBatch`: **60 checks** against the real Zoo scene, including nine independent definitions, exact stats, normal loose-body physics, floor settling, physical nearby selection, real alternating 1/2 keyboard equips, carried collider disabling, the corrected instruction and preservation of the original pickups. A Direct3D 11 capture is saved as `.quest-review/ReadabilityProject/zoo-individual-arm-pickups.png`. Scene changes add only the new display and its parent link, plus the pickup caption update; existing Zoo objects and stations are retained.

The Zoo pickup-only test disables unrelated legacy audio/toggle pad readers in its unsaved, disposable scene because those readers assume a native keyboard during batch startup. Their production scripts and saved scene enable states are unchanged. The final 60-check run exited 0 with no runtime exceptions; the saved source scene and relevant scripts match that fixture.

Remaining art work is deliberate: final models, authored attack animations, audio, icons, readable attack telegraphs and polished effects. Placeholder dimensions and unique-mechanic tuning need gameplay review. No production wave/loot integration or new retrieval bonuses are included in this prototype pass.

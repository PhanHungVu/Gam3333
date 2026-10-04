# Enemy attacks and animation

The existing `Enemy_mace` and `Enemy_crossbow` prefabs now carry `EnemyAttack` and `EnemyAnimation`. Merge Test's existing spawners use these prefabs automatically; AI flow fields, zigzag movement, steering, ground following, and spawning remain their existing systems.

| Enemy | Stop range | Damage | Attack cooldown | Fire duration | Hit/release frame |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mace | 2.2 m | 20 | 1.8 s | 0.7 s | 60% of Fire |
| Arm crossbow | 9 m | 12 | 2 s | 0.7 s | 60% of Fire |

Both enemies alternate their two existing weapon arms. `MechWeaponAnimator` supports permanently equipped enemy weapons as well as the player's modular loadout. Mace uses MaceArm Fire; crossbow uses CrossBowArm Fire and Reload. The shared saved biped clips animate the legs, body, and head while the existing AI moves the gameplay root. There is no animation root motion or player controller installed on an enemy. After three stationary seconds, only the current enemy weapon can play Neutral.

Melee checks facing, vertical reach, distance, and obstruction again at the hit frame. A player who moves out of reach or behind a wall during windup is not hit. Each swing resolves once. Killing or disabling an enemy cancels its pending attack.

Crossbows snapshot their aim during windup and release a visible placeholder bolt at the release frame. Bolts travel using the existing swept `MechProjectile` collision queries, hit walls, and can be dodged. Their optional damage-target filter restricts damage to the selected player; other actors still block the bolt. Launched projectiles continue travelling after the shooter dies. Actual authored bolt assets can replace these placeholder visuals in the later projectile step.

Tune attack damage, cooldown, Fire Duration, Release Frame, facing angle, and projectile speed on **Enemy Attack**. Tune approach/stop range on **Enemy Agent**. The earlier cube test enemy retains its existing behavior; the two weapon-bearing spawner families provide the animated combat showcase.

The player prefab has **Combat Health → Use Equipment Stats** enabled. Maximum health comes from `EquipManager.Health`; current health starts full and accepts damage. `MechHealthHud` displays the supplied HP artwork above the movement meter, preserving its sprite colours. Part swaps update capacity and its readout without healing. Zero health deactivates the player, stopping input, movement, combat, and player HUDs. Respawn remains separate future work.

The health UI remains the separate `Prefabs/UI/Player Health HUD.prefab`, outside the player prefab. In A33, Merge Test, JhMapScene and ZooScene it is parented beside Movement HUD under the same existing Canvas. The fallback `Mech Cursor Canvas.prefab` carries both meters too. Each meter has three children: Glow, BarBack and BarFront. HP uses `Sprites/HpBarGlow.png`, `HpBarBack.png` and `HpBar.png`; movement uses the corresponding EnergyBar sprites. Glow scales only on X, BarFront uses a Horizontal Filled Image with Left origin to crop its artwork, and BarBack stays full-sized. Designers can move/resize the meter roots and edit sprite references directly. MechHealthHud binds the scene player or auto-finds MechPlayerRig; a CanvasGroup hides just the HP meter when its player is disabled/dead, leaving the shared Canvas active. Optional legacy Text/TMP readouts and configurable tinting remain supported; the artwork setup disables automatic tinting and removes prototype labels.

Validation covers windup timing, cooldowns, alternating arms, range/obstruction checks, dodging, swept bolt collisions, friendly blocking, enemy death cancellation, equipment-based health, and player death. Live Merge Test checks exercise actual AI approach, animation, attack, projectile travel, and damage, with rendered poses for visual review.

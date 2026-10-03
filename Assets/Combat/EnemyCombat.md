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

The player prefab has **Combat Health → Use Equipment Stats** enabled. Maximum health comes from `EquipManager.Health`; current health starts full and accepts damage. `MechHealthHud` displays an upper-left health bar and current/max numbers, with a red fill at 25% health or below. Part swaps update capacity and its readout without healing. Zero health deactivates the player, stopping input, movement, combat, and player HUDs. Respawn remains separate future work.

The health UI is the standalone `Prefabs/UI/Player Health HUD.prefab`, placed as a separate scene root in A33, Merge Test, and JhMapScene. It is not part of the player prefab. Designers can edit its Canvas, move/resize Health Panel, and replace the Source Images on Health Track/Health Fill without entering Play mode. MechHealthHud lives on the UI prefab and references the scene player's CombatHealth plus its own Canvas, Fill Image, and optional health text. An empty Health reference with Auto Find Player enabled binds to MechPlayerRig, never an enemy; code can call Bind(newPlayerHealth) for a replacement player. The HUD hides when the bound player is disabled or dead and resumes when enabled. A regular or Sliced fill shrinks horizontally through its anchors; a Filled Image uses fillAmount (choose Horizontal, Left origin). Legacy Text and TextMeshPro labels are supported. Turn off Tint Fill By Health to preserve artwork colours, or edit Healthy/Low Health Color and the threshold. Runtime code updates existing references without creating or rebuilding UI objects.

Validation covers windup timing, cooldowns, alternating arms, range/obstruction checks, dodging, swept bolt collisions, friendly blocking, enemy death cancellation, equipment-based health, and player death. Live Merge Test checks exercise actual AI approach, animation, attack, projectile travel, and damage, with rendered poses for visual review.

# Swappable mech combat and abilities

**Current input revision:** [Player controls](ControlScheme.md) supersedes older binding/ability notes below. The current branch has no head or torso ability-effect implementation; Tab/Ctrl now expose equipped-part requests for gameplay to connect. Space retains the locomotion module's existing jump/hover lift.

Lower-body swapping and movement are covered in [modular locomotion milestones 1-3](../GroundMovement/Locomotion.md). Legs use the same equipment system as weapons.

## Player setup

Use `Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab`. A33 and Merge Test now use this integrated player. The existing `Prefab/Mech.prefab` remains a legacy prototype for regression checks; do not place both players in one scene.

The integrated prefab contains the existing movement, energy, boost presentation, targeting, combat, feedback and weapon HUD components. It replaces SimpleMove and the dynamic root Rigidbody with the existing CharacterController. VisualMech handles boost lean; Model Orientation converts the teammate's +X-facing model to gameplay +Z. The player capsule remains upright. Equipped part colliders are disabled, and dropped part colliders/physics are restored. All loose collider objects use **MechPart (user layer 8)**, including children originally authored on custom layers. Players and enemies exclude that layer from their solid contacts and movement/ground probes; pickup triggers still detect it and parts still settle on terrain. The camera also ignores it. Attachment restores authored layers; dropping restores authored collision exclusions, so character-only filters do not leak into loose physics. See the [camera correction and verification](PartSwapUI.md#equip-camera-correction--2026-10-30).

MechPlayerRig reuses an existing third-person camera or creates the configured Main Camera prefab. It binds the existing cursor and energy UI, with Mech Cursor Canvas as a fallback. Camera orbit, soft/hard/free-aim behavior, melee framing and FOV settings are preserved. This is the existing single local-player setup, not a multiplayer camera manager.

## Existing weapons reused

No duplicate sword, throwing axe, cannon or crossbow data assets were created. The teammate's four PartCatalogue assets now identify their combat behavior with PartData.Weapon. Damage, ammo, reload and effective range come from those existing stat lists. Range is converted at 4 metres per golem length; maximum range defaults to 1.5 times effective range. Cannon blast radius remains 6 metres. Projectile speed, thickness and color retain the existing combat controller tuning.

| Input | Part/socket | Weapon | Damage | Ammo | Reload | Effective / maximum range |
| --- | --- | --- | ---: | ---: | ---: | --- |
| LMB | ArmL | Sword | 100 | unlimited | 0.7 s | Existing 4 m forward melee box |
| RMB | ArmR | Throwing axe | 50 | 30 | 0.5 s | 12 / 18 m |
| Q | ShoulderL | Cannon | 100 | 20 | 3 s | 20 / 30 m |
| E | ShoulderR | Large crossbow | 30 | 50 | 1 s | 40 / 60 m |
| 1 / 2 | Highlighted matching part | Left / right swap (1 for a single socket) | | | | |

The current input path uses **1 / 2** for the highlighted part's left/right arm or shoulder, and **1** for a head, legs or torso. V cycles distinct nearby parts, including parts with multiple colliders. The [world-space part swap UI](PartSwapUI.md) displays the active part and actual bindings without opening a modal menu or changing the cursor/camera. Empty sockets are supported. The optional leg-debug swaps use F1–F4 so number keys do not conflict. Startup only installs this player's own child parts, including the preinstalled torso; it no longer searches the scene for other players' equipment. Torso replacement transfers equipped socket occupants when the replacement supports all required sockets.

Ammo and reload deadlines belong to each physical Part instance. Dropping and re-equipping never refills it or clears its reload. PartData assets do not contain mutable ammunition. A missing weapon disables its attack and is reported in the HUD. Ranged parts provide their own optional muzzle Transform; the cannon's mesh-only recoil child follows swaps. The legacy prototype still uses its original built-in loadout without MechEquipmentCombat.

`EquipManager.Health` exposes the sum of positive Health stats on the currently equipped parts. Dropping or swapping a part changes the total automatically; grounded pickups contribute nothing. Edit Health entries on the existing PartCatalogue assets. The player has `CombatHealth` with **Use Equipment Stats** enabled: it starts at this maximum and accepts damage. Capacity changes clamp current health and never refill lost health. `MechHealthHud` shows the supplied three-layer HP artwork above the movement bar on their shared Canvas; its glow squeezes horizontally, its front crops with HP, and its backing stays full-sized. Its own CanvasGroup follows the player enable/disable lifecycle without hiding other UI. Zero health disables the player; respawn is not implemented. Movement and energy retain their existing tuning.

## Part swap presentation

The current checkout uses `Prefabs/UI/PartSwapWorldUI.prefab`, a single non-interactive World Space Canvas beside the active dropped part. It shows the incoming name, existing socket occupants and a few qualitative stat changes such as Damage -- or Fire rate ++. Raw/unchanged stat lists are hidden. It has no gameplay input handlers, raycasters or swap calls. Appearance, target changes, successful swaps and removal receive restrained feedback. The previous modal `PartSwapSelectPod` model-preview implementation is not present in this checkout; this UI uses placeholder panels/text.

Use **Tools > Mech > UI > Set Up World Part Swap UI** for explicit setup. See [PartSwapUI.md](PartSwapUI.md) for data ownership, tuning, missing bindings and disposable-project verification.

## Three independently equippable ability modules

`Assets/Prefabs/Abilities` contains ResonancePulse Part, ZigzagBarrage Part and BloomMissile Part. These are distinct abilities, each with its own PartData and Part component. Their default category is ShoulderL. Drag a prefab into a scene, approach it and press F; Q then activates the equipped ability instead of firing an unequipped cannon. Re-equip the dropped cannon to restore normal firing. No test key or automatic weapon-impact hook is enabled by default.

To use an ability on another socket, change its Part Category to ArmL, ArmR or ShoulderR; the socket's normal button activates it. For a custom part, add one pattern component plus MechAbilityDriver, assign Driver.Ability and use PartData.Weapon=None. The pattern classes themselves know nothing about keys or weapon slots. An ability can instead augment a weapon with the driver's optional Activate On Weapon Impact setting and Source Weapon. Secondary barrage impacts and confirmation-only events are excluded to prevent recursive activation.

Code can call `driver.TryActivate()` or `pattern.TryActivate(new MechAbilityContext(owner, origin, direction, target, stats, layers, feedback))`. When manually attaching a driver, call BindOwner; EquipManager does this automatically. Dropping a part cancels its pending damage and removes its old-owner subscriptions. Disabling/re-enabling a part or player releases active wave/projectile visuals and permits later activation; cooldown deadlines persist.

- **Resonance Pulse:** configurable expanding horizontal rings. Damage occurs only as the swept visible wavefront reaches a collider. An enemy can be hit once per ring; later rings can hit it again. Ring width, radius, count, speed, spacing in time, vertical tolerance, damage and collision-limited horizontal knockback are exposed. Knockback is measured in metres and affects surviving targets only.
- **Zigzag Barrage:** configurable alternating left/right lanes along the captured horizontal aim. It checks ground support/slope and both the previous-node and origin paths. A blocked or unsupported node ends the barrage; subsequent nodes never jump past the obstruction. Each distinct explosion may hit the same enemy once.
- **Bloom Missile:** a travelling carrier bursts into radial bomblets. Carrier burst is visual only. Bomblets move with swept collision and explode at collision or lifetime expiry. Each detonation may hit an enemy once; separate bomblets may re-hit within the shared budget. The default spread is horizontal for ground enemies. Launch origin is elevated to the existing muzzle offset, with optional explicit origin Transform and configurable local offset.

All patterns snapshot their configuration at activation, exclude the owner, use the shared CombatBlast collision layers and world obstruction checks, deduplicate CombatHealth rather than colliders, and aggregate one kill confirmation per complete activation. Damage is applied in the frame of its visible wave/detonation. Each module limits total actual health damage, distinct targets, detonation/projectile count and total duration. They do not add camera impulses or change camera targets/FOV.

## Pooling and optional assets

Patterns reuse CombatFeedback materials, explosion geometry, audio voices and confirmation UI. Persistent rings and bomblets lease pooled visuals; leases cannot be stolen by short explosions. Activation checks visual capacity first, and stale leases cannot modify a reused effect. Default effect capacity is 32, configurable up to 64. One sequence runs per module; an active module rejects another activation. Ring storage is bounded to 8 and Bloom storage to 25 flights. Repeated activations reuse that storage.

Missing material/audio slots are supported. Presentation toggles can mute effects without changing damage rules. AudioClip slots remain intentionally empty; no external assets or packages are required.

## Optional Spiral Barrage (recovered)

MechCombatController > Optional Cannon Pattern > Spiral Barrage remains off by default. Enabling it preserves the initial cannon collision and immediate AoE, then schedules secondary explosions around that exact point. Tune node count, angular increment, radial spacing, start/step delay, blast radius, damage scale, total secondary damage and distinct-target cap. The default is 8 nodes, 60 degrees, 6 m starting radius, 0.8 m radial growth, 0.25 s start delay, 0.18 s spacing, 2 m blasts, 35% damage, 200 secondary damage and 5 secondary targets. The initial blast is not applied a second time. Each secondary blast has independent collider deduplication and world obstruction checks; one confirmation aggregates the entire barrage. Simultaneous sequences are capped at 8; a saturated runner retains the normal initial cannon blast.

## Validation

`Tests/Editor/MechFoundationChecks.RunBatch` runs real Unity physics checks in an isolated Unity 6000.5.8f1 project, including movement, camera/targeting, melee, original weapon stats, direct/AoE deduplication, obstruction, Spiral Barrage, all three abilities, bounded cleanup, equipment swaps, physical-part ammo/reload persistence, torso replacement and ability activation during actual quick boost.

`MechAbilitySceneChecks.RunBatch` and `RunMergeBatch` run the A33 and Merge Test scenes, render the effects, and assert single-player camera binding, visible damage, one completion per activation, pool cleanup and unchanged cannon ammo. They do not save test enemy positions. `MechPartsIntegration` is the explicit batch asset migration helper, not an automatic import hook; the assets are already integrated and do not need migration on project load.

Final manual checks: pickup with 1/2 near compound-collider drops; aim and fire each socket while moving; soft/hard target switching; close-range sword/camera comfort; visible cannon recoil; swap all four weapon parts and the torso; equip each ability and judge readability during boost. Optional sound levels can only be tuned after clips are supplied.

### Results from this integration

- Unity 6000.5.8f1 compilation succeeded.
- Final foundation/regression run: **400 checks passed**, exit code 0.
- A33 and Merge Test graphical scene runs: **both passed**, exit code 0; screenshots were rendered and the integrated mech/ability presentation was inspected.
- No gameplay NullReference/MissingReference/MissingComponent exceptions or input action/binding-count assertions appeared in these runs.
- Both graphical scene logs contain an existing `UnityEditor.Search.SearchDatabase.EnumerateAll` startup `ArgumentOutOfRangeException`. It did not prevent the gameplay checks; this Editor Search index issue has not been repaired by this task.
- These are automated Unity play-mode/render checks, not a manual keyboard/mouse playtest. Pickup feel, all camera modes during live combat, and final recoil/ability readability still need a human playtest.

### Files changed

- Recovered/new ability and shared gameplay scripts: `AbilityPhysics`, `BloomMissileAbility`, `CannonSpiralBarrage`, `CombatBlast`, `MechAbilityContext`, `MechAbilityDriver`, `MechPartAbility`, `ResonancePulseAbility`, `ZigzagBarrageAbility` in `Scripts`.
- New integration scripts: `Scripts/MechEquipmentCombat.cs`, `Scripts/MechPlayerRig.cs`.
- Updated existing scripts: `MechCombatController`, `MechProjectile`, `CombatFeedback`, `CombatFeedbackEffect`, `CameraControlThirdPerson`, `MechMovementController`, `MechTargetLockController`, `MechWeaponHud`, `MechEnergyHud`; and `Part System/Part`, `PartData`, `EquipManager`, `PickupDetector`.
- Updated assets: FullPlayer_mech_base and its existing part prefabs, the four existing weapon PartData assets, A33 and Merge Test scene bindings.
- New assets: three ability PartData assets, three ability part prefabs under `Prefab/Abilities`, and `Prefab/Mech Cursor Canvas.prefab`. Script/asset `.meta` files accompany them.
- Checks: updated `MechFoundationChecks`; recovered/new `MechAbilityChecks`, `MechAbilitySceneChecks`, `SpiralBarrageChecks`, `SpiralBarrageSceneChecks`, `MechPartsChecks`, and the explicit `MechPartsIntegration` asset helper in `Tests/Editor`.
- Documentation: this file and the link in `Combat/README.md`.

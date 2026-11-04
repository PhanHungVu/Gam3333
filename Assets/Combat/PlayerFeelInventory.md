# Player movement and weapon inventory

This is the **2026-11-02 snapshot**. The later [control revision](ControlScheme.md) replaces manual assault-boost input with torso-ability requests, adds automatic running/manual reload, changes camera lock to F and retires movement energy costs. Refer to that document for current controls.

Audit: 2026-11-02. Based on current runtime code, `InputSystem_Actions.inputactions`, `FullPlayer_mech_base.prefab`, its equipped part data, locomotion prefabs and the fallback `Main Camera.prefab`. A scene can override those prefab settings or use its own camera. This describes implemented behavior, rather than promises from older planning documents.

## Current movement

| Feature | What is implemented |
| --- | --- |
| Camera-relative locomotion | WASD/arrows or left stick; diagonal input is normalized. Acceleration and deceleration are separate. Movement uses a CharacterController, including its collision, slope and step handling. |
| Facing | Normal movement faces the camera's horizontal direction. Hard lock faces the selected target while movement remains camera-relative. |
| Speed from equipment | Legs supply base metres/second. Other parts apply multiplicative speed percentages. Fast legs have base speed 10; heavy legs 5. With the shared prefab's sword, axe, cannon and large crossbow, the multiplier is about 0.8714: roughly 8.71 fast / 4.36 heavy walking speed. These modifiers do not currently scale quick-boost or assault-boost speeds. |
| Quick boost / dash | Press Left Shift. Direction is captured from movement input, or camera-forward with no movement input. A curve shapes speed; direction is committed during the dash. Energy cost and cooldown limit repetition. Standard biped dash requires ground contact. |
| Sustained assault boost | Hold Left Ctrl. Accelerates toward a sustained high speed, uses energy continuously and has limited turning. Starts in the requested movement direction, falling back to camera-forward. Hitting an obstruction, losing ground, jumping on biped legs, or running out of energy can end it. Restart after a forced stop requires releasing the button. |
| Jump and air steering | Space jumps for biped legs. Gravity and ground-stick velocity are implemented. Horizontal movement remains controllable in air through the ordinary acceleration/deceleration path. A ground-started dash can carry into a jump; starting a biped dash after leaving the ground is not allowed. There is no separately tuned biped air-control model. |
| Braking after boost | A stronger stop rate brings boost momentum back to the requested walking velocity, including while steering or continuing to walk. |
| Equipment changes | Swapping legs changes the locomotion strategy and animations without resetting energy, velocity or boost timers. |

**Shift is not a separate sprint.** The old sprint fields/state still exist for compatibility, but `MechMovementController.Tick` explicitly disables sprint input. Quick boost and assault boost are the active speed actions.

### Current shared-prefab tuning

| Setting | Value |
| --- | ---: |
| Normal acceleration / deceleration | 20 / 32 units/s² |
| Turn response | 12 |
| Quick-boost authored peak × distance multiplier | 24 × 1.75 = 42 units/s peak |
| Quick-boost authored duration × duration multiplier | 0.22 × 1.35 = 0.297 seconds |
| Recovery after active dash | 0.35 seconds; next dash is gated for about 0.647 seconds from the previous start |
| Dash energy cost | 20 |
| Assault speed / acceleration | 28 units/s / 45 units/s² |
| Assault steering limit | 90 degrees/s |
| Assault energy drain / minimum start energy | 18/s / 10 |
| Post-boost braking | 64 units/s² |
| Gravity / jump height | -25 units/s² / 1.5 units |
| Energy capacity / regeneration | 100 / 25 per second |
| Energy recovery delay | 0.6 seconds; no regeneration during active boosts |

The dash's curve varies its speed; 42 is a peak, not a constant speed. Collision can shorten its actual travel.

### Available leg modules

| Module | Existing behavior |
| --- | --- |
| Fast biped | Current default; jump, directional ground dash, ground assault boost, Fast animation set. |
| Heavy biped | Same underlying biped movement rules with slower part Speed and a different Heavy animation set. It does not have a separate mass/inertia simulation. |
| Hover prototype | Lower deceleration and surface-following height control. Hold Space to raise its clearance. It can dash/assault boost above the ground; deep gaps still apply gravity. This is not unrestricted flight. |
| Tank prototype | No jump; quick boost is forced forward. Separate acceleration/deceleration settings. |

On the shared development prefab, **F1/F2/F3/F4** select Fast / Heavy / Hover / Tank, and **F8** toggles the locomotion overlay. These are development controls, not the normal pickup flow. Hover/tank part assets currently have empty stat lists and use module speed defaults rather than a configured legs Speed stat.

## Camera, animation and movement feedback

- Cinemachine third-person follow, damping, mouse look, lateral framing and near-plane-aware camera collision are implemented. Loose dropped parts are excluded from normal camera obstruction.
- The fallback camera has base FOV 60, a quick-boost FOV kick up to 7, assault FOV kick up to 10, and assault follow-distance multiplier 1.12. Quick boost also uses tighter follow damping. Scenes can supply a different camera.
- The visual body leans/banks with velocity and acceleration. Thruster particles and trails are enabled on the shared prefab.
- Fast/Heavy leg animation includes idle, walk, boost, stopping and jump states. Upper-body/head motion follows the leg animation, and swaps blend poses. Animations do not move the gameplay root.
- A walking loop follows resolved motion and changes pitch with speed. Boost ignition has an exposed audio slot, currently empty.
- Energy and health HUDs exist. Health is the sum of equipped part Health stats; the default shared loadout contributes 250. Swaps change maximum health without healing lost health.
- A `Landed` event reports downward impact speed, but no runtime subscriber currently turns it into dedicated landing dust, a landing sound or a camera impact.

## Aiming and targeting

- Mouse look and screen-center free aiming are implemented.
- Middle mouse toggles soft/hard lock on a short release. Holding it for 1.2 seconds enters free aim; pressing from free aim restores targeting.
- Soft selection follows screen-center aim, retains a valid selection over empty space and can switch as another enemy crosses the aim.
- Hard lock tracks a target, turns the mech toward it and supports directional mouse-flick target switching. Range is 60 units on the shared prefab.
- Projectiles aim at the current target's aim point when targeting is active; free aim raycasts from the screen center. Their flight direction is captured at launch. There is no player projectile homing or target-velocity lead prediction.
- Close melee camera framing exists, including pitch/shoulder adjustments and a brief framing hold. It does not switch into a first-person head view or pump distance/FOV as an enemy gets closer.

## Current weapon loop

| Control | Behavior |
| --- | --- |
| Hold LMB / RMB | Repeat the equipped left/right arm attack as soon as it is ready. Both buttons can be used together. |
| Press Q / E | Use the equipped left/right shoulder weapon once per press. |
| V, then 1 / 2 | Cycle a nearby dropped part and equip it into the matching left/right socket. Single-slot parts use 1. |

The socket selects the part; the part's subtype selects melee, projectile or splash behavior. Attack timing is driven by `FireRate` through `ShotInterval = 1 / FireRate`. Total ammo, magazine rounds and readiness/reload deadlines belong to each physical Part, so dropping/re-equipping does not refill it or clear its wait.

Magazines reload automatically. An accepted shot spends one round; emptying the magazine starts the Reload timer when ammo remains. The next shot waits for both the shot interval and reload. Normal weapon attacks do not spend boost energy.

### Current authored weapons

| Part | Damage | FireRate | Magazine / total ammo | Reload | Practical repeat interval |
| --- | ---: | ---: | --- | ---: | ---: |
| Sword | 100 | 0.7/s | Melee | — | 1.43 s |
| Mace pickup | 70 | 1/s | Melee | — | 1 s |
| Throwing axe | 50 | 0.5/s | 1 / 50 | 1 s | 2 s |
| Small arm crossbow pickup | 70 | 0.6/s | 1 / 80 | 1.2 s | 1.67 s |
| Shoulder cannon | 100 direct + 50 splash | 1/s | 1 / 30 | 3 s | 3 s |
| Large shoulder crossbow | 100 direct + 10 splash | 1/s | 1 / 30 | 4 s | 4 s |

Intervals assume enough ammunition and an uninterrupted action. The current ranged magazines all contain one round, so reload behaves much like another per-shot cooldown. A direct splash-weapon hit receives both the direct and splash portions; other targets in the radius receive splash only. Cannon radius is 3 units; the large crossbow is currently also a Splash subtype, with radius 0.5. Range is authored in golem lengths and converted at 4 units per golem. Projectiles retain full damage until their range expires.

### Attack timing and impact

- **Player projectiles launch immediately when an attack is accepted.** Authored Fire animation is presentation, not a delay before launch.
- **Player melee normally resolves on acceptance too.** Range scales the melee box. For a just-out-of-reach locked target, an assist can extend the hitbox over 0.12 seconds, up to 1 unit within a 30-degree cone. The player body is not lunged, moved or turned by that assist. Boosting/leaving the ground can cancel a pending assisted hit.
- Enemies have their own animation release timing; that rule is not currently the player's melee/projectile release rule.
- Weapons have separate visual Fire/Reload/rest playback and authored attack audio. The loaded axe/shoulder bolt hides while unavailable and returns when ready. After standing still for three seconds, one randomly selected equipped weapon plays its idle animation.
- Shots use swept collision; damage rules handle owner/team filtering, compound colliders and walls. Melee is a shared overlap box; straight projectiles and splash damage are implemented.
- Shared splash feedback includes muzzle flash, mesh recoil, explosions, shockwave rings, embers, camera impulse and splash-kill confirmation. It also applies to the large crossbow because that weapon is currently Splash. Ordinary axe/small-crossbow hits do not use this splash-feedback path.
- Enemy hit flashes and death effects exist separately. Boost ignition, splash-impact and multikill sound slots are currently empty; weapon attack sounds and walking audio are assigned.
- `MeleePerformed` is exposed, but no runtime listener currently adds a dedicated melee-impact effect, impact sound or camera impulse through that event. A separate `MeleeBoxPreview` debug view exists and is enabled on the shared prefab.

## Mechanics not currently implemented on the active player

These are absent from the current movement/combat paths, rather than things that need to be enabled in an Inspector:

- Jump buffering, coyote time, variable jump height/jump-cut, double jump, wall movement, biped air-dash initiation, or a separately tuned biped air-control model.
- Dash invulnerability frames, dodge rewards/perfect-dodge timing, or a player stagger/poise/guard/parry system.
- Melee combo chains, heavy/light branches, charge attacks, weapon-specific player release/hit windows, or a general attack-cancel/combo input buffer.
- Manual/active reload, weapon heat, firing energy costs, accuracy bloom/spread/recoil accumulation, or resource interactions between firing and movement.
- Active head/torso/legs abilities driven by the new ability cooldown/duration stats. Those fields and Accuracy are stored data; the current player does not execute ability actions or consume Accuracy.
- A general hitstop/slow-motion or knockback system. Health currently applies damage, raises events and deactivates the character at death.

## Existing hooks for a follow-up plan

| Area | Current extension points |
| --- | --- |
| Input | `MechPlayerInput`, shared Input System asset. |
| Motor | `MechMovementController`, `LocomotionModule`, `MechEnergyController`; `QuickBoostStarted/Ended`, `AssaultBoostStarted/Ended`, `Landed`, `StateChanged`, `AnimationDataUpdated`. |
| Camera/body feedback | `CameraControlThirdPerson`, `MechBoostPresentation`, `MechLegAnimator`, `MechUpperBodyAnimation`, `MechMovementAudio`. |
| Weapon behavior | `MechCombat`, `MeleeAttack`, `RangedAttack`, `Part`, `PartData`; `WeaponAttackStarted`, `ShotFired`, `ProjectileImpact`, `MeleePerformed`. |
| Impact and health | `CombatFeedback`, `EnemyCombatFeedback`, `CombatHealth.Damaged/Died`. |
| Equipment | `EquipManager.EquipmentChanged`, `MechEquipmentLocomotion`, existing world-space part swap UI. |

For planning, distinguish tuning an existing action from adding a new decision: dash speed/curve/braking, camera response and lean already have controls; jump forgiveness, timed melee releases, combat resources and weapon-specific timing would require new gameplay behavior. This audit does not change the player's movement or attacks.

## Source map

- [Movement controller](../Scripts/Movement/MechMovementController.cs), [energy](../Scripts/Movement/MechEnergyController.cs), [modules](../Scripts/Movement/LocomotionModule.cs).
- [Player input](../Scripts/Input/MechPlayerInput.cs), [input actions](../InputSystem_Actions.inputactions), [shared player prefab](../Prefabs/Mech/Base/FullPlayer_mech_base.prefab).
- [Camera](../Scripts/Camera/CameraControlThirdPerson.cs), [targeting](../Scripts/Targeting/MechTargetLockController.cs), [boost presentation](../Scripts/Presentation/MechBoostPresentation.cs).
- [Combat](../Scripts/Combat/MechCombat.cs), [melee](../Scripts/Combat/MeleeAttack.cs), [ranged](../Scripts/Combat/RangedAttack.cs), [physical part state](../Scripts/Part%20System/Part.cs), [part stats](../Scripts/Part%20System/PartData.cs).
- [Weapon presentation](../Scripts/Presentation/MechWeaponAnimator.cs), [splash feedback](../Scripts/Presentation/CombatFeedback.cs), [health](../Scripts/Damage/CombatHealth.cs), [authored part values](../PartCatalogue).

# Modular locomotion and body animation

Open **A33** or **Merge Test** and enter Play mode. Both use `FullPlayer_mech_base.prefab`, which now includes the locomotion equipment bridge and a development debug panel. The older `Prefab/Mech.prefab` keeps its original standalone movement.

| Control | Action |
| --- | --- |
| 1 / 2 | Swap Fast Legs / Heavy Legs, with their own animations |
| 3 / 4 | Swap Hover / Tank placeholder lower bodies |
| WASD | Camera-relative movement |
| Space | Biped jump; hold for hover lift; ignored by tank |
| Shift | Sustained movement boost |
| Alt | Quick Boost; tank bursts along its current forward direction |
| Ctrl | Assault Boost; hover also supports this in the air |
| F8 | Show/hide locomotion data |
| F | Existing nearby-part pickup, including locomotion parts |

The panel and shortcuts are development tools, disabled in non-development players. Remove or disable `MechLocomotionDebug` to use only normal pickups. Its ordered `modulePrefabs` array is editable (up to four keyboard shortcuts). Swapped-out legs are cached inactive; repeated shortcuts reuse them without littering the scene. Returning to Fast Legs restores the original starting part. Normal F pickups retain their existing drop behavior. A **Heavy Legs Pickup** is placed near the player in both scenes.

## Structure and behavior

`EquipManager` owns attachment to the existing Legs socket, which lives with the torso. There is no second module manager or second player hierarchy. `MechEquipmentLocomotion` listens to equipment changes and binds the equipped part's `LocomotionModule` to the existing root `MechMovementController`.

The root retains one `CharacterController`, input instance, collision resolution, velocity, facing, boost timers, energy, camera and combat. Parts contain a `Part`, a locomotion strategy, and visuals/pickup colliders. Equipped pickup colliders are disabled by the existing part system. The root capsule does not resize for different module silhouettes.

The motor reads devices once into `MechLocomotionInput` and passes commands to the strategy. `Tick(input, cameraTransform, deltaTime)` is also available for deterministic checks. Automatic player input normally calls it from Update; external drivers set `ReadPlayerInput = false` to prevent double ticks. Modules never read input devices or move the root directly. Camera/targeting continue to own aim and hard-lock facing.

| Module | Travel / Shift speed | Acceleration / braking | Vertical behavior |
| --- | --- | --- | --- |
| Fast biped (default) | Existing 6 / 12 m/s | Existing 20 / 32 m/s² | Existing jump and gravity; ground-only boosts |
| Heavy biped | 3 / 6 m/s | Existing 20 / 32 m/s² | Same jump and gravity as fast legs |
| Hover | 12 / 18 m/s | 20 / 6 m/s² | Follows surfaces at 0.7 m clearance; Space raises clearance to 3 m |
| Tank | 4.5 / 8 m/s | 15 / 18 m/s² | Gravity, no jump; forward Quick Boost |

Biped inherits the motor's serialized settings so existing prefab tuning survives. The animated biped prefabs enable `usePartSpeedRating`: their existing catalogue MoveSpeed ratings (50 fast / 25 heavy) scale travel and sprint relative to a reference rating of 50. Legacy biped parts leave this disabled. Hover/tank tuning lives on their module components. Quick/Assault Boost speed, energy costs and cooldowns remain shared motor settings. Tank travel stays camera-relative in this prototype; track steering and animation are later work. Hover probes below the root capsule, ignores self/trigger colliders, and falls under gravity over gaps deeper than its probe range. Releasing Space returns to normal hover clearance. Grounded means actual capsule contact, so a stable hover is airborne.

Swapping does not teleport, zero velocity, restart an active Quick Boost, refill energy or rebuild input. An active Quick Boost completes with its captured direction; new commands use the replacement module. Assault Boost cancels on the next tick if the new module cannot sustain it at the current ground contact. Modules missing from old legs, disabled/destroyed modules, and an empty Legs slot use legacy biped movement as a deliberate prototype fallback.

The animated fast/heavy prefabs and earlier placeholder modules are under `Prefabs/Mech/Locomotion`. They can also be placed in a scene and equipped through F. Add a new `LocomotionModule` subclass to future Legs prefabs to implement another style; the motor and equipment manager do not switch on module type. Debug swapping compares `PartData` identity so two different biped parts remain independently selectable.

## Real fast/heavy leg animation

`FullPlayer_mech_base.prefab` starts with `BipedFast_Module` under the existing Legs socket. `BipedHeavy_Module` uses the same mesh and attachment transform. Each part owns its `golem2.glb` skeleton and Animator; the persistent player, torso, head, arms and weapons keep their existing hierarchy. The imported torso/head **renderers** are disabled to avoid duplicates, while their bones stay intact. Joint VFX follow the leg bones.

The source clips are `Biped_Run` and `Heavy_Walk.001` from `Anim/golem2.glb`. The animated export omits textures, so both modules reuse the textured leg material from `Models/Golem1Models/Golem1Legs.glb`. Each module has its own controller in `Anim/Locomotion`, blending between a generated static `Biped_Stand` pose and its looping movement clip. `MechLegAnimator` reads the motor's resolved speed after movement, changes cadence relative to 6 m/s (fast) or 3 m/s (heavy), and blends to standing when stopped, airborne or in Quick/Assault Boost. No separate jump, boost, strafe or reverse clips are supplied yet; movement uses the available forward gait. The same authored clips now drive the visible torso and head through the connection described below.

`MechEquipmentLocomotion` binds the equipped rig, carries its normalized gait phase into every layer of a replacement, and applies the current motion state immediately. Unequipped rigs stop animating; ordinary F pickup and development swaps use the same binding path. Root motion is disabled, so clips cannot move the gameplay root or leg socket.

The existing `VisualMech` is already authored at 0.5 scale. The integrated prefab's `EquipManager.partScale` now matches that value; its old value of 1.5 enlarged every part during Awake and put the legs below the ground. The new modules reuse the old model's attachment offset without an extra half-scale wrapper. Authored socket transforms and the CharacterController dimensions are retained; upper sockets receive presentation offsets at runtime.

## Body/head showcase and future rig families

Both biped controllers now have three Override layers, each with its own idle/move blend tree and transform mask:

| Layer | Mask | Animated transforms |
| --- | --- | --- |
| Base Layer | Mask_Legs | Leg.L / Foot.L and Leg.R / Foor.R (the latter is the source bone's actual spelling) |
| Torso | Mask_Torso | Body only |
| Head | Mask_Head | Head only |

All masks exclude Root. Each layer defaults to the equipped module's matching full clip, so the current showcase reproduces the authored fast or heavy animation. No clips were split, no Blender re-export was needed, and no runtime Animator Controller replacement was added. Separate blend trees allow a different compatible clip on Torso or Head later. They currently share movement blend/cadence parameters; independently timed actions, a clip-selection UI and per-part animation-set assets are not implemented yet. Layers filter transforms; they do not retarget a clip to a different skeleton or guarantee that it is evaluated only once.

The important project-specific complication is that `Golem1Torso` is both a visible mesh and the Part containing **all** sockets, including the leg rig. Moving that Part to follow Body would also move the source Animator and working feet. The connection therefore keeps the Part and Legs socket stable:

- `MechAnimationPoseSource` lives on each animated leg module. It reads Body/Head deltas relative to serialized bind poses, expressed in each bone's rest axes. Bone references live on the module; the player code has no hard-coded biped paths.
- `MechUpperBodyAnimation` on the player finds the equipped source and receivers through `EquipManager`. It applies their poses in LateUpdate after Animator evaluation and existing boost lean, and blends source changes over 0.12 seconds. Missing or disabled sources settle to neutral.
- `MechPartPoseReceiver` on the torso animates `Animated Torso Visual` plus the head, arm and shoulder sockets around the calibrated body pivot. The visible child reuses the existing torso mesh/material; the original Part-root MeshRenderer is disabled. Equipment roots, leg socket and hierarchy references are retained.
- The head receiver animates its existing mesh and joint VFX around the calibrated head pivot. It receives the local Head delta only; Body movement is inherited once through the head socket. No duplicate full-body mesh is enabled.

Each receiver has a **Weight** slider (0 = original pose, 1 = full authored motion). This permits separate body/head tuning without touching the legs. Dropping a receiving part or disabling presentation restores its local pose. Weapon sockets and muzzle transforms follow the body's visual motion, while weapon ownership, ammunition, targeting and gameplay root remain unchanged. Existing `MechBoostPresentation` still supplies movement lean; this change does not add a second procedural lean system.

The suggested hybrid direction fits, with these boundaries:

1. Keep authored motion and masks for the present showcase. Unity's [animation layers](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimationLayers.html) and [transform masks](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AvatarMask.html) support this separation.
2. Retain a rig/Animator per locomotion family. A future quadruped, hover or track module can supply its own compatible body/head pose channels or remain neutral. Do not assume biped bone paths, axes, units or proportions are universal; calibrate the adapter and receiver for a new family.
3. Add retargeting only when a second distinct compatible rig needs it. Fast and Heavy currently use the exact same rig. Unity's built-in [animation retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html) requires a configured Humanoid Avatar; this Generic floating mech does not qualify automatically.
4. Add terrain contact correction after authored animation when needed. The current per-leg chain is Leg -> Foot, not a conventional separate hip/knee/ankle chain. Unity's [Two Bone IK](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/constraints/TwoBoneIKConstraint.html) expects Root/Mid/Tip references. Use suitable per-limb bones or a floating-foot correction scheme instead of treating the shared Root as both hips. No IK package or solver has been added in this pass.
5. Later, layer head aiming/stabilization or body reactions onto the pose channels. Give each stage clear ownership so it composes with the authored pose and existing boost/recoil rather than overwriting the same transforms from multiple scripts.

## Animation contract

Read `MechMovementController.AnimationData` in a presentation component, or subscribe to `AnimationDataUpdated` on enable and unsubscribe on disable. Read the current snapshot when subscribing. Each snapshot is immutable and independent of skeletons and Animator controllers.

| Field | Meaning |
| --- | --- |
| Speed | Actual horizontal speed in m/s, after collision resolution |
| ForwardSpeed / SideSpeed | Signed m/s along the gameplay root's forward/right axes |
| VerticalSpeed | Signed world vertical m/s; downward ground-stick motion suppressed while grounded |
| IsGrounded | Actual CharacterController contact |
| IsBoosting | Shift movement boost, Quick Boost or Assault Boost, including hover air boosts |
| TurnRate / IsTurning | Actual signed yaw degrees/second; turning threshold is 1 degree/second |
| AimDirection | Normalized world direction to the selected target, or camera forward in free aim |
| LocomotionType / State | Selected movement style and the existing movement state |

No normalization against module speed is baked into the contract. Future blend trees can normalize m/s using their own clip speeds. Local axes use the gameplay root rather than the rotated/scaled imported model or boost-lean visual parent. Zero-time ticks retain the previous snapshot; disabling movement publishes zero motion/boost. The current module identity updates immediately on equipment changes.

The animation contract remains rig-independent. `MechLegAnimator` is its first concrete consumer; hover/tank still use placeholder visuals. Existing boost presentation remains independent.

## Validation

`MechFoundationChecks.RunBatch` includes `MechLocomotionChecks`, `MechAnimatedLegChecks` and `MechUpperBodyChecks`: real prefab equip/swap, movement and boost preservation, distinct speeds, hover lift/settling/gap fall, tank jump rejection, collision-blocked movement, real clip weights and moving bones, foot alignment, animation phase transfer, masked reconstruction against both original clips, stable leg socket/feet during upper-body motion, independent body/head weights, no accumulated drift, idle handling, drop/re-equip and lifecycle fallback. Existing combat, abilities, camera, targeting and input isolation checks remain in the same suite.

Run only in an isolated Unity 6000.5.8f1 project copy with matching packages:

```text
Unity.exe -batchmode -nographics -projectPath <copy> -executeMethod MechFoundationChecks.RunBatch -logFile <log>
```

Do not add `-quit`; the suite enters Play mode then exits itself. Generated assets are included and need no setup step to play. `MechLocomotionSetup.RunBatch` is the earlier placeholder-generation helper; it resets development module choices. To regenerate the animated setup in a disposable copy, run `MechAnimatedLegsSetup.BuildBatch` first, then `MechUpperBodySetup.BuildBatch` to add the masks, upper layers, pose sources and calibrated receivers.

`MechLocomotionSceneChecks.RunBatch` and `RunMergeBatch` exercise the actual keyboard/Update path in A33 and Merge Test. The harness routes its synthetic keyboard into the hidden batch Game view. Without `-nographics`, it also renders each module through the existing scene capture helper; camera renders exclude the IMGUI debug panel.

`MechAnimatedLegSceneChecks.RunBatch` / `RunMergeBatch` check real W movement, 1/2 swapping, continuing leg/body/head animation, the scene's heavy F pickup, return to idle, and preserved torso/camera. They also capture fast/heavy front and gameplay views when graphics are enabled.

For manual review, swap repeatedly while moving, jumping and boosting. Confirm the torso and camera remain attached. Walk into a wall and watch Speed drop while input is held. Strafe/backpedal to check signed speeds; use free/soft/hard aim to check aim direction and turn rate.

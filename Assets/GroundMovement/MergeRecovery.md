# Player animation recovery after teammate merge

Merge `e7e1b11` brought `David+Jinhao_branch` (`1aec4a3`) into `Hung-GreyBox` after backup commit `cd0c02c`. The main player prefab lost its animation components and reverted to static legs; the clips, controllers, modules and runtime scripts survived.

## Teammate changes retained

- New sword/throwing-axe starting arm prefabs and swapped arm sides, new shoulder prefabs and their authored socket positions.
- Crossbow and mace enemy prefabs, flow-field/zigzag AI, navigation grid, obstacle tags/layers, course layout and two staggered wave spawners.
- Moved `Prefab` assets to `Prefabs`, and legacy part prefabs to `Prefabs/Mech/Base/Parts/Old`, preserving their existing asset references.
- Updated catalogue descriptions, new mace/small-crossbow data and weapon input bindings.

## Recovery

`Prefabs/Mech/Base/FullPlayer_mech_base.prefab` keeps the merged gameplay components and weapon layout. Its existing Legs socket now starts with `BipedFast_Module`. Restored `MechEquipmentLocomotion`, `MechLocomotionDebug` and `MechUpperBodyAnimation` use the tested backup settings. The torso renderer moves into `Animated Torso Visual`; torso/head pose receivers retain the backup pivot calibration and target the merged hierarchy. Upper sockets follow body motion while the Legs socket stays stable.

The prefab GUID and existing scene references are preserved. Fast/Heavy controllers and saved animation assets are reused, including the surviving walking clips. Walk/run, dash/boost, stop and jump behavior stays on the existing v2 animation pipeline. The expanded v3 weapon animations are deferred.

The large prefab text diff replaces the unpacked static-leg mesh and particle hierarchy with the existing nested Fast module prefab. The merged torso, head, weapon parts and root gameplay components remain.

`Scenes/Merge Test.unity` reconnects both empty `enemyPrefab` fields to their matching teammate enemy prefabs and restores `Heavy Legs Pickup` at its backed-up position/scale. The backed-up player prefabs and scene are unchanged. The HUD labels and editor validation paths match the merged controls and moved folders.

| Control | Behavior |
| --- | --- |
| WASD / Shift | Walk / faster run cadence |
| Alt / held Ctrl | Short dash / sustained boost |
| Space | Jump |
| 1 / 2 | Development Fast / Heavy swap |
| 3 / 4 / F8 | Development Hover / Tank / debug overlay |
| F | Equip nearby part, including Heavy pickup |
| LMB / RMB | Sword / throwing axe |
| Q / E | Cannon / crossbow |

## Validation

Passed in an isolated Unity 6000.5.8f1 project:

- `MechFoundationChecks.RunBatch`: 647 gameplay, equipment, locomotion and animation checks.
- `MechMergeSceneChecks.RunBatch`: actual merged spawners, both pathing variants, enemy movement/target resolution, player/camera uniqueness, retained weapon layout/controls and weapon firing.
- `MechAnimationV2SceneChecks.RunMergeBatch` and `RunBatch`: actual keyboard walk/run/dash/boost/stop/jump and separate Fast/Heavy clips on all three Animator layers in Merge Test and A33.
- `MechAnimatedLegSceneChecks.RunMergeBatch`: actual moving swaps, body/head presentation and normal F pickup.

`MechMergeRecoverySetup.RecoverBatch` is an explicit recovery helper, not an automatic import hook. Do not rerun old full asset-generation helpers over the merged player: those recreate controllers or module data. Normal play and GLB imports do not require a setup command.

## Merge `8906938`: shared HUD and scene flow recovery

The player prefab, animation sets, weapon rigs, projectile models and muzzle markers, audio, camera collision, soft-lock selection and part preview UI survived this merge. The teammate's navigation prefab, map layout, enemy loot drops, objectives and menu controls are retained.

JhMapScene had reverted to the prototype movement bar. Its health meter was left at the scene root with zero scale and no Canvas reference after the health prefab upgrade. Reattached it beside Movement HUD under the existing Canvas, restored its player binding and placement, and restored the three sprite layers on movement. Both fronts crop horizontally, their glows scale on X, and their backs remain full-sized.

A33 still referenced the deleted legacy Enemy prefab. Reconnected that instance to the existing CubeTest_Enemy prefab at the original `(3.4, 1, 10.5)` position. Archived backup scenes were left unchanged.

SceneManagerScript now uses the runtime SceneManager and restores time scale when loading a scene. The map objective checks its return-to-menu key after winning, rather than returning before reading input; its optional tooltip is safe to leave empty. Menu button callbacks and scene names are preserved.

Validation: 927 foundation checks; merged enemy waves, weapons and part selection; live JhMapScene camera collision; map completion and F return, menu-to-zoo and zoo-to-menu, and starting a fresh map; reference audit of MainMenuScene, A33, Merge Test, JhMapScene and ZooScene with no missing scripts or broken references. The isolated attack-timing fixture disables random enemy loot so a dropped collider cannot obstruct a later scenario; gameplay loot settings are unchanged.

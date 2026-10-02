# Weapon animations

The player and the six weapon part prefabs use separate visual rigs from `Anim/golem2_v3.glb`. Original meshes, materials, mount transforms, colliders, and gameplay scripts remain on their existing parts. Locomotion continues using its saved Fast/Heavy clips.

| Part | Controls | Motion set |
| --- | --- | --- |
| Left-arm sword | LMB | SwordArm |
| Right-arm throwing axe | RMB | ThrowingAxe |
| Left-shoulder cannon | Q | Canon |
| Right-shoulder crossbow | E | CrossBow |
| Left-arm mace | LMB after pickup | MaceArm |
| Right-arm crossbow | RMB after pickup | CrossBowArm |

`Merge Test` includes **Mace Animation Pickup** and **Arm Crossbow Animation Pickup** near the player. Use the usual F pickup interaction. The mace and arm crossbow currently play animations only; their damage/projectile behavior is a later combat step. Sword damage and the existing ranged placeholder projectiles retain their existing behavior.

`MechWeaponAnimator` plays Fire → Reload → rest when an attack is accepted. Fire uses its authored duration when the gameplay cooldown permits; Reload fills the remaining cooldown. Rejected attacks do not start an animation. Each part owns its own playback, so shoulder weapons can reload while another weapon attacks, or while legs are swapped and boost is used. Ground pickups have disabled Animators and no running playback graph. Dropping during an action immediately stops it and restores the rest pose; re-equipping starts from rest.

After three seconds of grounded, stationary movement with no weapon action, `MechEquipmentCombat` randomly selects **one** equipped weapon to play its looping Neutral animation. Other weapons hold Neutral frame zero. The choice stays fixed for that idle period. Movement, boost, jumping, attacking, and equipment changes reset the wait. Adjust **Weapon Idle Delay** on the player's Mech Equipment Combat component.

This v3 export contains Reload for Canon, CrossBow, and CrossBowArm. SwordArm, MaceArm, and ThrowingAxe contain Neutral and Fire only, so they return directly to Neutral. Assign their Reload clips when those actions are exported.

The axe mesh is attached to `ThrowingAxeRig/WeapRoot/ThrowingAxeBody/Barrel`. `MechWeaponAnimator.Placement` exposes that bone for the later projectile step. It follows the animated arm; this setup does not detach or throw the displayed axe. Existing muzzle markers follow the appropriate weapon body.

## Updating exports

Select `Anim/Weapons/Editor/ImportSettings.asset` to assign the source GLB and each family's Neutral, Fire, and Reload clips explicitly. Initial setup uses the current action names, but refresh and runtime playback use asset references, not name searches. After a renamed action or a replacement GLB, reassign the affected selections in this asset.

Reimporting the selected source refreshes the saved clips automatically. **Mech → Animation → Refresh Weapon Clips** refreshes them manually. Saved `.anim` assets retain their GUIDs and survive missing source actions. Weapon refresh does not overwrite saved walking clips. Keep rig/bone paths stable; changing the skeleton requires rebuilding the corresponding visual rig and validating its attachment poses.

`Anim/Weapons/*.asset` contains each runtime animation set. The animation driver exposes `CurrentPose` and `NormalizedTime` for future hit/release timing. No new frame-timed melee damage or authored projectile release is implemented yet.

## Validation

Editor checks cover clip ownership, accepted/rejected attacks, independent playback, reload completion, the axe attachment, part swapping, input bindings, and ordinary F pickups. Batch input checks use the real input actions and supply cursor-capture policy explicitly because a hidden Unity process has no native captured cursor. Scene checks also render each weapon pose for inspection. Import checks verify missing and renamed actions preserve saved clip references and locomotion assets.

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

Hold **LMB** or **RMB** to repeat the corresponding arm's attack as its existing cooldown finishes. This follows the equipped part, supports both buttons together and stops on release. Ammunition and attack timing remain owned by combat/the part; shoulder Q/E controls remain press-triggered. The cursor-capture click is still suppressed, and pausing or releasing the cursor disarms attack input until the buttons are released.

`Merge Test` includes **Mace Animation Pickup** and **Arm Crossbow Animation Pickup** near the player. Use V to select a nearby part, F to open the swap menu, then choose its socket. The mace deals its existing 50 damage through the shared melee overlap query and uses its 0.7-second cooldown. The arm crossbow now fires the textured CrossbowBolt model using its existing PartData damage, ammunition, range and cooldown. It has separate state from the shoulder crossbow. Arm controls follow the selected socket: LMB for the left arm, RMB for the right arm.

`MechWeaponAnimator` plays Fire → Reload → rest when an attack is accepted. Fire uses its authored duration when the gameplay cooldown permits; Reload fills the remaining cooldown. Rejected attacks do not start an animation. Each part owns its own playback, so shoulder weapons can reload while another weapon attacks, or while legs are swapped and boost is used. Ground pickups have disabled Animators and no running playback graph. Dropping during an action immediately stops it and restores the rest pose; re-equipping starts from rest.

After three seconds of grounded, stationary movement with no weapon action, `MechEquipmentCombat` randomly selects **one** equipped weapon to play its looping Neutral animation. Other weapons hold Neutral frame zero. The choice stays fixed for that idle period. Movement, boost, jumping, attacking, and equipment changes reset the wait. Adjust **Weapon Idle Delay** on the player's Mech Equipment Combat component.

This v3 export contains Reload for Canon, CrossBow, and CrossBowArm. SwordArm, MaceArm, and ThrowingAxe contain Neutral and Fire only, so they return directly to Neutral. Assign their Reload clips when those actions are exported.

The loaded axe remains attached to `ThrowingAxeRig/WeapRoot/ThrowingAxeBody/Barrel`. A separate ThrowingAxe model launches from that placement bone and spins in flight. The displayed loaded axe and the shoulder crossbow's loaded bolt hide on an accepted shot, return when the cooldown completes with ammunition remaining, and become visible again when dropped. The arm crossbow's muzzle follows its animated weapon body. Its current authored model has no displayed loaded bolt.

Projectile prefabs are under `Prefabs/Combat/Projectiles`. Both crossbows, including enemy crossbow arms, launch the same textured CrossbowBolt prefab; the throwing axe uses ThrowingAxe. `PartData.projectilePrefab` selects each part's model. Visual orientation and axe spin are separate from the existing swept collision, damage, range and obstruction checks. Player shots still launch on attack admission; enemy shots retain their configured animation release frame. The cannon retains its existing projectile and effects.

## Updating exports

Select `Anim/Weapons/Editor/ImportSettings.asset` to assign the source GLB and each family's Neutral, Fire, and Reload clips explicitly. Initial setup uses the current action names, but refresh and runtime playback use asset references, not name searches. After a renamed action or a replacement GLB, reassign the affected selections in this asset.

Reimporting the selected source refreshes the saved clips automatically. **Mech → Animation → Refresh Weapon Clips** refreshes them manually. Saved `.anim` assets retain their GUIDs and survive missing source actions. Weapon refresh does not overwrite saved walking clips. Keep rig/bone paths stable; changing the skeleton requires rebuilding the corresponding visual rig and validating its attachment poses.

`Anim/Weapons/*.asset` contains each runtime animation set. The animation driver exposes `CurrentPose` and `NormalizedTime` for future player hit/release timing. Sword and mace currently use the existing attack admission/reach-assist damage timing. Frame-timed player melee damage and projectile release remain later work.

## Validation

Editor checks cover clip ownership, accepted/rejected attacks, independent playback, reload completion, the axe attachment, part swapping, input bindings, and ordinary F pickups. Batch input checks use the real input actions and supply cursor-capture policy explicitly because a hidden Unity process has no native captured cursor. Scene checks also render each weapon pose for inspection. Import checks verify missing and renamed actions preserve saved clip references and locomotion assets.

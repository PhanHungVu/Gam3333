# Zoo Fodder model and animation integration

The imported enemy is `Models/Specialist/ENEMY_Fodder.fbx`. The teammate's tested setup is `Prefabs/Enemies/Small Enemies/Enemy_Fodder.prefab`: a four-legged Fodder body carrying the grey `shoulder_cannon_specialist`. Zoo already spawned this through `ZooEnemyWaves`, `WaveData_Zoo` and the Small Enemies floor switch. This integration uses that actual loadout, including its existing cannon stats, projectile, audio, drop list and ranged behavior.

## Replacement and compatibility

Both `Enemy_Fodder.prefab` and the legacy **`Enemy_crossbow.prefab`** use this Fodder body/cannon setup. The legacy asset name/path remain for compatibility. Its GUID, root GameObject fileID and all 16 existing root component IDs are preserved. Existing Roaming, KOTH, Escort, Elimination, Training, Zoo and controlled-encounter references therefore resolve the replacement automatically. Wave data, spawn counts, weights and the Crossbow AI role are unchanged. Elite crossbow enemies and the player are not replaced.

The small sword, mace, hammer, throwing-axe and spinner variants now use the same body too. Sword/mace/hammer/axe keep both original weapon Parts and their independent visual rigs; their original world sizes are preserved and dedicated holders mount them above the Fodder body. Hammer retains the current mace model. The spinner uses the Fodder body with its existing spin/hop presentation. No cannon is added to these melee/special variants. All root prefab IDs, existing root component IDs, AI roles, tuning and loot tables are preserved; waves and scene references continue to use the same assets.

The Fodder remains a small ranged enemy with the demo's 80 HP, movement speed 5, range 8, minimum preferred distance 5, cost 2, 1-second attack and 70% release point. Its carried weapon is the demo's Specialist cannon, rather than the former two crossbow arms. This also means the replacement uses the demo cannon's splash shot and cannon drop. The normal player crossbow weapons/projectiles remain available and unchanged.

## Connected animations

`Anim/Specialist/Enemy_Fodder.controller` now contains:

- Directional locomotion blending idle, walk, jog, left/right strafe walk and left/right strafe jog using actual local motor velocity. There is no authored backpedal clip; backward movement reverses the walk cycle.
- Cannon charge start followed by charge loop during the existing attack telegraph.
- Cannon fire beginning at the actual `EnemyAttack.Releases` change; presentation never fires a projectile or changes damage/cooldown.
- Front, back, left and right hit reactions selected from the accepted damage source position relative to the actor.

`EnemyFodderAnimation` is presentation only. Existing `EnemyAnimation` and `EnemyAgent` retain locomotion/movement ownership. Root motion remains disabled; no model scale is modified at runtime. Cancelling a pending attack returns the charging pose to locomotion. Hitstop freezes presentation, and pooling resets it without replaying the previous shot. Hit reactions do not impose gameplay stun or ragdoll physics.

The imported body has no authored melee body attack clip. Melee variants therefore keep locomotion and directional hit reactions while their weapon rigs play the existing swing. They do not play the cannon charge/fire body poses. Spinner rotation and hopping still affect only `VisualMech`.

The imported body and cannon use the teammate's grey materials and attachment transforms. A fixed root hurtbox is fitted to the actual body geometry rather than inheriting the visual holder's offsets; the body is aligned to the floor and the targeting marker sits in the body. The cannon remains attached to its authored shoulder bone. Shared player weapon assets, audio assets, model files and FBX import settings are unchanged.

## Melee and loot compatibility

Both prefabs were tested with this branch's upright melee shove. Living enemies keep their logical root upright and targetable while the force moves them. Killing blows retain the pose-locked whole-model launch. Successfully rolled cannon loot spawns independently before the launch and its visual duplicate is hidden on the corpse.

## Authoring and verification

`Tools > Mech > Enemies > Connect Zoo Fodder Model and Animations` rebuilds the controller and updates the two existing prefabs through `FodderModelSetup`. It retains the legacy root IDs and remaps root/component, Animator, weapon, aim and Behavior Graph Self references into the replacement hierarchy. Rerun this tool after changing the canonical Zoo Fodder setup or regenerating the old enemy roster. No automatic asset mutation runs when scripts compile.

`Tools > Mech > Enemies > Use Fodder Body For All Small Enemies` applies the body replacement to the other five variants through `SmallEnemyFodderSetup`. It refits their body hurtbox and aim point and reconnects locomotion/spinner presentation. Repeated use retains the existing weapon mounts, avoiding duplicates or resetting manual mount adjustments. It does not rebuild graphs, rebalance enemies or change mission wave lists.

Isolated Unity 6000.5.8f1 validation: **54 focused model/runtime checks and 997 core gameplay checks passed**, exit 0. The old enemy combat regression now expects the actual Fodder cannon's audio, splash projectile and direct-plus-splash damage instead of the retired enemy crossbow loadout. Focused checks cover both prefabs, preserved legacy references across six wave lists, rig/weapon ownership, grounded hurtboxes/aim points, strafing, telegraph/fire synchronization, all hit directions, pooling, live graphs, melee push, killing launch and cannon loot. Idle, strafing, charging and firing captures were inspected. Logs/captures are archived under `.quest-review/Results/FodderModelIntegration`.

The existing ranged-encounter suite also passed **30 checks**, exit 0 (**1,081 total**). This covers repeatable ranged presets, immediate telegraphed attacks, retreating into range, crowd spacing, finding firing angles around scenery, sidestepping friendly bodies, sustained fire in a mixed melee/ranged encounter, target loss and encounter cleanup. The production encounter scene and its current dependencies were copied into the isolated project for this run; the production scene was not modified.

Manual check: restart Play Mode. In Zoo, enable **Small Enemies** using its floor switch to see the original demo Fodder. In EnemyBehaviorPlaytest, the existing ranged preset (F7) now spawns the same new model through the legacy small-crossbow references. Missions using that ranged prefab receive the replacement as well. The inherited grey material is temporary artist content; this task adds no new textures, sounds or HUD elements.

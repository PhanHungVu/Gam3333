# Specialist player model integration

The active cannon is now the grey Specialist model across players, enemies and pickups. The coloured model remains archived. See [GreyCannonReplacement.md](GreyCannonReplacement.md) for the subsequent global replacement; the coloured-cannon adjustments below describe the earlier integration.

The regular player now uses the teammate's Specialist torso, head and animated biped legs. `Prefabs/Mech/Base/FullPlayer_mech_golem.prefab` keeps its existing GUID and root component IDs so scenes that reference the regular golem receive the new body. `Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab` remains the default for SubLevel, Zoo and the regular pre-mission torso selection. Both use the current shared HUD, explosive dash and six spaced dust puffs.

The **Heavy Torso (Demo Only)** selection retains its old body and all-weapons loadout. On the experimental remote-retrieval branch, the regular starter keeps its sword and adds the existing grey Specialist cannon through the serialized starting loadout (right shoulder, E). The right arm and left shoulder remain empty. See [RemoteWeaponRetrieval.md](RemoteWeaponRetrieval.md) for the branch audit and staged plan. Source models, import settings, part stats, mission waves and the shared HUD artwork/layout are unchanged.

## Weapons and sockets

The merge contains two remodeled weapons:

| Prefab under `Prefabs/Mech/Specialist` | Slot | Current gameplay |
| --- | --- | --- |
| `shoulder_cannon_specialist` | Left or right shoulder | Existing cannon stats and sound |
| `arm_blaster_specialist` | Left or right arm | Equippable model/animations; its imported stats remain empty, so it cannot fire |

The teammate's seven socket transforms and their animated bone parents are preserved. The sword, mace, throwing axe and remodeled blaster use the authored wrist sockets directly. The throwing axe's placement is unchanged.

Compatibility poses on the new torso's `Socket` components handle the different weapon rig origins. The remodeled grey cannon uses the teammate's original merged shoulder position directly, with no added lift or outward offset. The large crossbow retains its 0.45-unit outward spacing and now sits 0.15 units lower than the previous setup (0.6 units above the socket at the regular player's 0.5 part scale). The archived coloured cannon's compatibility pose keeps its earlier 0.75-unit lift and 0.45-unit outward offset. It is turned 180 degrees relative to the first integration; its visual forward axis differs from the remodeled cannon's despite similarly named mount bones. Its muzzle is moved to the forward barrel tip. The small arm crossbow remains 0.2 units lower. These offsets are in authored part units and scale with the player.

`EquipManager` applies the pose when equipping/reparenting. Entries are keyed by weapon animation set, so the correction follows the actual model rig, including copies with different part data. Other sockets default to an identity pose. These attachment poses apply to the two regular player prefabs; the Heavy demo's sockets are unchanged.

The small crossbow had a nonuniform `Weapon Visual Rig` scale of approximately `(2.587, 2.568, 0.921)` above its rotating bones. That distorted its shape during idle/reload. Setup now uses a unit-scale animation rig and bakes the original neutral shape into `Anim/Weapons/ArmCrossbow_RigidMesh.asset`; the imported model and animation clips remain unchanged. The mesh's neutral shape/size is preserved. This fixes the standalone pickup wherever it is equipped, without per-frame rescaling or replacing its animation. The common fixed equip scale remains the existing `partScale`/`authoredScale` calculation.

The subsequent player-only crossbow facing correction turns both crossbows 180 degrees around their mount points on the Specialist/regular golem body. The lowered small-crossbow position and raised/outward shoulder position are retained. The enemy crossbows and shared weapon prefab poses remain unchanged. Optional muzzle poses on these player Socket entries put shots/effects at the forward bolt tip; `EquipManager` applies them to the equipped instance, and restores its authored marker on unequip or transfer to a socket without that override. Crossbow geometry, scale, animation clips, stats and sounds are unchanged by this facing correction.

This facing correction passed **238 focused checks** with graphical review of both crossbows, then **982 foundation checks**, both exit 0. Validation covers both sides, firing, animation proportions and muzzle restoration on drop/re-equip. Artifacts are under `.quest-review/Results/PlayerCrossbowFacing`. Existing deleted scenes were left deleted; only the two regular player prefab assets were exported.

The subsequent shoulder-height adjustment restores the grey cannon's original merged socket position and lowers the large crossbow by 0.15 world units. It passed **238 integration checks** with cannon/crossbow fit captures reviewed. Only four attachment positions changed in each regular player prefab; other weapon poses, socket transforms, rotations and scales were preserved. Artifacts are under `.quest-review/Results/ShoulderHeightAdjustment`.

The Blaster's incorrectly assigned arm-crossbow sound is now empty for the audio/gameplay owner to connect. No replacement sound or weapon stats were invented. The remodeled cannon's sound remains assigned.

## Scene and maintenance integration

Zoo's merged legacy stamina canvas is replaced by a linked instance of `Prefabs/Mech Cursor Canvas.prefab`. The player HUD fallback is repaired as well. Zoo, KOTH, Escort and Elimination start with one complete HUD bound to the actual player. Zoo retains the teammate's Fodder enemy, ten-entry `WaveData_Zoo`, switches and dropped-part displays. Existing mission/training rosters are preserved.

**Tools > Mech > Integrate Specialist Player Model** reapplies the integration in Edit Mode. The existing HUD repair and dash setup tools now include the Specialist. Reapplying the integration leaves already configured assets unchanged. Compatibility entries are editable on the arm/shoulder Socket components inside either regular player prefab.

## Verification

`SpecialistPlayerIntegrationChecks.RunBatch` uses a disposable Unity project and the real prefab, equipment, physics, animation and HUD systems. It checks both regular player prefabs, all eight weapon models on both applicable sockets, resolved animation bindings, firing and walking attachments, mount clearance/facing, forward barrel placement, dash dust, HUD uniqueness and real map startup. It also checks preserved socket transforms/root component IDs, idempotence, hashes of protected assets, preservation of the crossbow's neutral geometry, and constant mesh proportions across fire/reload/idle samples. Graphical front/rear weapon-fit captures are reviewed alongside the assertions.

Additional validation uses the existing `MechFoundationChecks.RunBatch` and `GameplaySceneMaintenanceChecks.RunBatch` suites. Results and weapon-fit images are stored under `.quest-review/Results/SpecialistPlayerIntegration`.

On 2026-11-07, Unity 6000.5.8f1 completed **135 focused integration checks, 980 foundation checks and 59 HUD/scene checks**, all with exit 0. The focused run used D3D11 graphics, and all sixteen front/rear weapon captures were inspected. The other two suites ran headless. No C# diagnostics or audited runtime exceptions were reported.

The subsequent weapon-placement/animation revision passed **230 integration checks** with D3D11 captures. The jump/dash revision passed **257 focused movement checks**, including simultaneous input, jump before dash, and dash before jump at 30/60/120 FPS on regular and Heavy players. The full foundation suite passed **980 checks** with the retired grounded-only assertion updated to the new requested behavior. All three runs exited 0, without C# diagnostics or audited runtime exceptions. The revised cannon and crossbow fit captures were visually inspected. Revision artifacts are under `.quest-review/Results/SpecialistWeaponRevision`.

This verifies the imported configuration and runtime integration; no standalone player build or human gameplay feel pass is included. The new body still uses the teammate's temporary grey material.

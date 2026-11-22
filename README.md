# Lithomachia

Lithomachia is a third-person golem horde shooter developed by **Team Medium-Well** for DigiPen Institute of Technology Singapore's **GAM300**. This repository contains the Unity prototype used to playtest missions, swappable golem parts, and combat alongside the team's Crucible Engine project.

> **Development target:** Windows x64 · **Editor:** Unity 6.5 (`6000.5.8f1`) · **Play mode:** Single-player prototype

## Features

| Area | What's in this prototype |
| --- | --- |
| Quest board | A 3D stone tabletop with randomized quests, animated briefings, and starting torso selection. Three successful side missions unlock the main quest. |
| Missions | King of the Hill, Escort, and Elimination, with generated terrain, enemy waves, and results that feed back into the quest board. |
| Golem loadouts | Swap nearby arms, shoulder weapons, and body parts. Equipment affects combat and movement, and ordinary loot can roll different stats. |
| Combat and movement | Ranged and melee weapons, ammunition and reloads, part abilities, dodging, and three camera modes with smooth target changes. |
| Enemies and encounters | Small and elite enemies use Unity Behavior graphs, navigation, and horde coordination. The playtest arena includes repeatable encounter presets and a weapon bench. |
| Presentation | Universal Render Pipeline, Cinemachine camera, a combat HUD, hit feedback, and menu, mission, and weapon audio. |

# chuuniby's Contributions to Gam3333

**Contributor:** chuuniby (Phan Hung Vu)
**Commits:** 89 (including merges), from **2026-09-07 to 2026-11-19**
**Main branches:** `Hung-GreyBox`, `meow-meow-test`, plus regular merges with `David`, `main`, and `Nelson-Asset-merge`

chuuniby set up the Unity project and built much of the core gameplay: the mech's camera, movement and combat, the part and ability system, animation, the enemy AI, the pre-mission quest UI, the HUD, and game-feel polish. Most features come with an editor setup script (`*Setup.cs`) and editor tests (`Assets/Tests/Editor/*Checks.cs`).

---

## 1. Project foundation (Sep 7–15)
- Created the initial Unity project and added **Cinemachine**. Removed Unity Version Control so the team could use Git.
- Third-person camera (`CameraControlThirdPerson.cs`), tuned over several commits.
- Mech movement and lock-on targeting: `MechMovementController`, `MechTargetLockController`, `MechTargetable`, `LockOnCursor`, `AutoMoveTarget`.
- Energy and boost: `MechEnergyController`, `MechEnergyHud`, `MechBoostPresentation`.
- Basic prefabs.

## 2. Combat system and weapons (Sep 17–19)
- Core combat: `MechCombatController`, `MechProjectile`, `CombatHealth`.
- "The 4 weapon things": `MechWeaponStats`, `MechWeaponHud`, `MechInputActions`.
- First combat-feel pass: `CombatFeedback`, `CombatFeedbackEffect`, `EnemyCombatFeedback`.
- Part abilities: `MechPartAbility`, `MechAbilityDriver`, `MechAbilityContext`, `AbilityPhysics`, plus specific abilities such as `BloomMissileAbility`, `CannonSpiralBarrage`, `ResonancePulseAbility`, `ZigzagBarrageAbility` and `CombatBlast`.
- `MechPlayerRig` and `MechEquipmentCombat` to connect equipped parts to combat.
- Fixed part pickup when parts are dropped.

## 3. Models, VFX and animation (Sep 22 – Oct 4)
- Added **GLB model support** and brought in new models.
- Golem joint glow VFX and particle emitter (`GolemJointGlow`, `GolemJointEffectFactory`).
- **Locomotion and animation system:** `LocomotionModule` with `Biped`, `Hover` and `Tank` variants, `MechLegAnimator`, `MechUpperBodyAnimation`, `MechAnimationPoseSource`, `MechPartPoseReceiver`, `MechEquipmentLocomotion`.
- Animation import tools: `MechBipedAnimationImport`, `MechWeaponAnimationImport`, `MechWeaponAnimator`, `MechWeaponAnimationSet`.
- Enemy animation and attacks: `EnemyAnimation`, `EnemyAttack`.
- **SFX:** `MechMovementAudio`.
- Projectile models, fixed muzzle position, removed sprinting, and fixed camera clipping (`MechCameraCollisionSetup`).
- **HP bar / health HUD:** `MechHealthHud`.
- Visual preview of a part before pickup (`PartModelPreview`).
- Fixed UI scaling across resolutions.
- Handled several merge recoveries between branches.

## 4. Pre-mission / quest selection UI (Oct 25 – Nov 4)
A full quest board shown before each mission, in the `Assets/Scripts/PreMission/` folder:
- Flow and data: `PreMissionFlowController`, `QuestDefinition`, `QuestBoardCatalogue`, `QuestSelectionSession`, `MissionStartHook`.
- Quest progression: `QuestProgressionRuntime`, `QuestProgressionSession`, `PreMissionProgressTrack`, `PreMissionObjectiveResultBridge`.
- Presentation: paper and notice layout, a stone board with physical motion (`PreMissionStoneBoard`, `PreMissionStoneMotion`, `QuestStoneView`), mission miniatures, board terrain, a tabletop shockwave, contact shadows, and impact feedback.
- Cinematic camera transitions and animation polish for mission select and quest details (`PreMissionQuestDetailsSlab`).
- Starter torso preview (`StarterTorsoPreview`), portrait slabs, readable labels, and mission logos.

## 5. Part swap UI (Oct 30)
- In-world part swap display: `PartSwapDisplay`, `PartSwapWorldView`, `PartSwapWorldHudPresenter`.

## 6. Enemy AI (Oct 30 – Nov 9)
- **Behavior Graph enemies:** `EnemyGraphController`, `EnemyGraphActions`, `EnemyGraphAttack`, special actions, rope attack, spinner attack, hop presentation, threat markers, and weapon telegraphs.
- **Horde coordination:** `EnemyHordeCoordinator`, `EnemyHordePressure`, `EnemyMeleeAttackPool`, `EnemyRangedPositioning`, `EnemySpecialCoordinator`, `EnemyLocalPositionQuery`.
- Encounter presets and a playtest arena (`EnemyEncounterPreset`, `EnemyBehaviorPlaytest`, roster setup).
- Zoo enemy waves (`ZooEnemyWaves`), enemy orbit fix, escort aggro.
- New cannon enemy model, fixed melee enemy model, fodder enemy animation, melee reaction and push physics.
- Camera obstacle handling (`MechCameraObstacles`).

## 7. Game feel, HUD and VFX (Nov 5–12)
- **Hit stop**, ragdoll death, projectile trails, combat visual events, and a player damage vignette.
- **Mission results screen** and mission outcome (`MissionResultsView`, `MissionOutcomeController`, `MissionParticipant`).
- Ability cooldowns with a HUD cooldown sweep, and an "ability ready" flash effect inspired by Risk of Rain 2.
- **Directional damage indicator** HUD (`DirectionalDamageHud`, `CombatDamageInfo`).
- Ground dust VFX when dashing (`QuickDashFeedback`).
- Pickup highlight and shine effect (`PickupHighlight`).
- **Remote part retrieval** with combat bonuses: axe barrage, piercing bolt, speed buff, bonus cooldowns, and retrieval feedback.
- Mission HUD feedback.
- **Pause menu** on Esc (`MissionPauseMenu`).
- **Main menu audio** and button sounds, plus level and quest audio.
- 2x walking speed (automatic run).
- Fixed enemy attack audio, random part stat rolls (`PartStatRoll`), and a **5% part drop rate**.

## 8. Weapons and level management (Nov 17–19)
- **Arm weapons:** `GreyboxArmWeapon`, `GreyboxArmWeaponCatalogue`, a weapon test bench (`GreyboxArmWeaponBench`), and accuracy spread (`AccuracySpread`). Added weapon balance tests.
- **Shoulder weapons:** `GreyboxShoulderWeapon`, `GreyboxShoulderWeaponCatalogue`.
- **Camera switch options** and lock modes in `CameraControlThirdPerson` and `MechTargetLockController`.
- **LevelManager** with a custom editor (`LevelManager`, `LevelManagerEditor`).
- Updates to the spawn director, enemy part drops, and `PartData` / `MechRulesConfig`.

---

## Testing and tooling
chuuniby wrote a large set of **editor test scripts** in `Assets/Tests/Editor/`, covering locomotion, animation, combat, abilities, enemies, quest UI, HUD, audio, weapons and level management. Most features also have an editor setup script, so a scene can be rebuilt or reconfigured without manual work.


## Requirements

- A Windows x64 development machine.
- Unity Hub and Unity Editor **6000.5.8f1**, as pinned in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
- Git to clone the repository. Install the Windows build module for the matching editor if you plan to make a standalone build.

Unity Package Manager resolves packages from [Packages/manifest.json](Packages/manifest.json) on import.

## Getting started

```powershell
git clone https://github.com/PhanHungVu/Gam3333.git
cd Gam3333
```

1. In Unity Hub, add the `Gam3333` folder. It contains `Assets`, `Packages`, and `ProjectSettings`.
2. Open it with the pinned Unity version and let the first import finish.
3. In the Project window, open `Assets/Scenes/MainMenuScene.unity` and enter Play Mode.
4. Choose **Quest Board** for the mission flow, **Gym** for the combat practice scene, or **Zoo** to browse parts and test enemies.

### Scenes for playtesting

| Scene | Use it to |
| --- | --- |
| `MainMenuScene` | Begin from the main menu. |
| `PreMissionScene` | Inspect quests, choose a starting torso, and launch a mission. |
| `KOTHTest`, `EscortTest`, `EliminationTest` | Start one mission mode directly in the editor. |
| `ZooScene` | Try physical weapon and body part pickups and the enemy showcase. |
| `JhCombatUIRevampScene` | Test combat and the HUD via **Gym** in the menu. |
| `EnemyBehaviorPlaytest` | Test enemy presets and compare Arm and Shoulder weapons. Open this scene directly in the editor. |

### Controls

| Input | Action |
| --- | --- |
| WASD / Mouse | Move / look |
| LMB / RMB | Left / right arm weapon. Hold to repeat when ready. |
| Q / E | Left / right shoulder weapon. Press for a Katyusha salvo and hold for Tesla pulses. |
| F | Cycle no lock, free camera with auto aim, and camera lock. |
| R / Shift | Reload / dodge |
| Tab / Space / Ctrl | Head / legs / torso ability |
| V / 1 or 2 | Cycle nearby parts / equip the selected part in the matching slot |
| Escape | Open or close the mission pause menu |

See the [control scheme](Assets/Combat/ControlScheme.md) for aiming, reloads, and socket selection.

## Building for Windows

1. Open **File > Build Profiles** and select **Windows** with the **Intel 64-bit** architecture. See the [Unity Build Profiles manual](https://docs.unity.com/en-us/engine/6000.5/manual/building-and-publishing/build-settings/build-profiles).
2. Use the project's [scene list](ProjectSettings/EditorBuildSettings.asset), with `MainMenuScene` first. Keep the quest board, its three mission scenes, Gym, and Zoo enabled for in-game navigation.
3. Choose **Build and Run** for a local playtest, or **Build** and select an output directory such as `Builds/Windows/`. Keep the executable and its generated data files together.

## Repository layout

```text
Gam3333/
├── Assets/
│   ├── Scenes/           Menu, quest board, missions, and test scenes
│   ├── Scripts/          C# gameplay, UI, and editor tools
│   ├── Prefabs/          Reusable player, enemy, level, and UI objects
│   ├── PartCatalogue/    ScriptableObject definitions for equipment
│   ├── LevelCatalogue/   Mission and level configuration assets
│   ├── PreMission/       Quest board data, models, and documentation
│   ├── AI/               Behavior graphs and encounter presets
│   ├── Combat/           Combat documentation and prototype assets
│   └── Tests/            Editor and gameplay checks
├── Packages/             Package manifest and lock file
└── ProjectSettings/      Editor version, scene list, and project settings
```

## Documentation

| Topic | Project notes |
| --- | --- |
| Quest board and mission flow | [Pre-mission setup](Assets/PreMission/README.md) |
| Player movement and controls | [Control scheme](Assets/Combat/ControlScheme.md) · [Locomotion](Assets/GroundMovement/Locomotion.md) |
| Equipment and combat testing | [Part swapping](Assets/Combat/PartSwapUI.md) · [Arm weapons](Assets/Combat/GreyboxArmWeapons.md) · [Shoulder weapons](Assets/Combat/GreyboxShoulderWeapons.md) |
| Enemy AI and missions | [Enemy behavior](Assets/Combat/EnemyBehavior.md) · [Encounter testing](Assets/Combat/EncounterTesting.md) · [Level manager](Assets/Combat/LevelManager.md) |
| HUD and audio | [Shared gameplay HUD](Assets/Combat/SharedGameplayHud.md) · [Audio](Assets/Combat/Audio.md) |

## Dependencies and asset credits

| Package | Used for |
| --- | --- |
| Universal Render Pipeline (URP) | Rendering and materials |
| Cinemachine | Third-person camera and lock-on framing |
| Input System | Gameplay and UI input |
| Unity Behavior and AI Navigation | Enemy decision graphs and navigation |
| Unity glTFast | glTF and GLB model import |
| Unity UI (uGUI) and TextMesh Pro | Menus, HUD, and text |

The [package manifest](Packages/manifest.json) and [lock file](Packages/packages-lock.json) record the project dependencies. Refer to each installed package's license and third-party notices.

| Bundled asset | Credit and license |
| --- | --- |
| Mini Forest 1.0 | Kenney, CC0 1.0. [Asset license](Assets/Models/MAP%20PLACEHOLDERS/kenney_mini-forest_1.0/License.txt) |
| MedievalSharp | wmk69, SIL Open Font License 1.1. [Font log](Assets/Fonts/medieval_sharp/FONTLOG.txt) · [Font license](Assets/Fonts/medieval_sharp/SIL%20-%20Open%20Font%20License.txt) |
| Liberation Sans | Google Corporation and Red Hat, Inc., SIL Open Font License 1.1. [Copyright and license](Assets/TextMesh%20Pro/Fonts/LiberationSans%20-%20OFL.txt) |

## Team Medium-Well

- Abhishek Akilandeswari Patchamuthu
- Chewn Thing Kwan
- Hugo Low Ren Hao
- Jean David Vidal Cruzado
- Tan Chun Kwang Xavier
- Tok Duan Jie Nelson
- Vu Phan Hung

Third-party packages and assets retain their respective copyright and license notices.

*All content © 2026 DigiPen Institute of Technology Singapore. All rights reserved.*

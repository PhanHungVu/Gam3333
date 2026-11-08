# Mech sound effects

## Main menu audio

`Scenes/MainMenuScene.unity` contains `Prefabs/Audio/MainMenuAudio.prefab`. Its **Main Menu Music** source automatically loops `Audio/main_menu.mp3` in 2D at volume **1**, pitch **1**, through `Audio/MenuAudio.mixer`'s **Music** group. The source belongs to the scene, so leaving the menu stops the music. Showing or hiding menu/options panels does not restart it.

`MainMenuAudio` connects all Unity `Button` components in that scene, including inactive nested options buttons and buttons created later. `button_hover.mp3` plays on pointer entry, and `button_click.mp3` plays through the button's successful `onClick` event. Cancelled presses, disabled buttons, and slider value changes are silent. A pointer already resting on a button when it appears does not produce an initial hover sound; leaving and re-entering does. Releasing a click does not replay hover audio. The current scene has Gym, Zoo, Quest Board, and Exit buttons; it does not yet contain an Options canvas.

Both UI clips use the prefab's shared **Menu UI One Shots** source: volume **0.155**, pitch **1**, 2D, **SFX** mixer group, with **Hover Volume** and **Click Volume** multipliers defaulting to **1** on `MainMenuAudio`. This source briefly survives a scene change to finish the last click, then destroys itself. Only the UI voice persists; menu music does not. Existing button navigation remains unchanged.

`Residual_Light.mp3` and `water_flow.wav` remain unassigned pending their intended use.

`MainMenuAudioChecks.RunBatch` verifies the scene/prefab wiring, native mouse interactions, inactive/runtime-created options buttons, silent sliders, looping, pause behavior, and scene-unload cleanup in an isolated Unity project.

## Level music and quest details

`Prefabs/Audio/LevelMusic.prefab` plays `Audio/combat.wav` continuously throughout a gameplay level, including time outside active combat. It is placed once in KOTHTest, EscortTest, EliminationTest, JhMapScene (gym), ZooScene, EnemyBehaviorPlaytest, and JhCombatUIRevampScene. Its source starts automatically, loops in 2D at volume **1** / pitch **1**, and routes to the same **Music** mixer group as the menu. It belongs to its level scene, so leaving or reloading that scene stops the old source. Add this prefab once to future gameplay scenes. It is absent from MainMenuScene and PreMissionScene.

`Prefabs/UI/PreMission/QuestDescSlabP.prefab` has a **Quest Details Arrival Audio** source routed to **SFX**. `PreMissionQuestDetailsSlab.Enter()` plays `Audio/start_quest_click.wav` once when selection starts the details slab's falling animation, alongside its title, mission type, description, Accept and Decline controls. Hover, content refresh, presentation recovery, and returning from torso selection do not replay the arrival sound. Declining and selecting a quest again starts a new drop and plays it again. The source is 2D, does not loop or play on awake, and has volume **1** / pitch **1**; **Appearance Volume** on the slab presenter defaults to **1** and is editable in the Inspector. Disabling the slab stops its pending sound.

`LevelAndQuestAudioChecks.RunBatch` verifies all gameplay scene placements, music playback/looping/lifetime, and single-play briefing arrival behavior in an isolated Unity project.

## Weapon and movement audio

| Gameplay action | SFX file |
| --- | --- |
| Sword attack | StoneSword_attack.wav |
| Throwing axe attack | ThrowingAxe_attack.wav |
| Cannon fire | Cannon_attack.wav |
| Shoulder crossbow fire | Crossbow_attack.wav |
| Arm crossbow fire | CrossbowArm_attack.wav |
| Mace attack | Mace_attack.wav |
| Walking / running | Golem1Legs_Move.wav |

Each `Anim/Weapons/*.asset` has **Attack Sound** and **Attack Volume**. `MechWeaponAnimator` plays that sound once when an equipped weapon accepts its Fire action. Rejected attacks, Neutral, Reload, and ground pickups are silent. Dropping or disabling a weapon stops its voice. Each physical weapon has a separate reusable AudioSource. Enemy attacks use the same animation sets and spatial sound, starting at the windup; their existing damage/release timing remains unchanged.

`MechMovementAudio` on the player and the two animated enemy prefabs owns the walking loop. **Movement Sound**, **Volume**, and **Reference Speed** are editable in the Inspector. Player playback follows resolved grounded movement, stopping during idle, jumping, boost, pause, or disable/death. Cadence follows resolved speed; sprinting is removed and Shift now dashes. Nearby enemy movement uses full spatial attenuation; player sounds use partial spatial attenuation so they remain audible from the third-person camera.

These short SFX are imported as mono, preloaded clips. The source WAV files remain unchanged. Weapon animation reimports preserve their audio references. Cannon attack audio is owned by its weapon rig; CombatFeedback's separate fire audio is disabled on this player to prevent duplicates. Impact and kill-confirmation settings are preserved.

`MechSfxChecks` verifies playable clips, accepted/rejected attacks, ground/drop ownership, independent voices, actual grounded locomotion, unchanged cadence for removed sprint commands, and silent boost/airborne states as part of `MechFoundationChecks.RunBatch` in an isolated Unity project.

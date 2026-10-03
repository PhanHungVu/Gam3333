# Mech sound effects

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

`MechMovementAudio` on the player and the two animated enemy prefabs owns the walking loop. **Movement Sound**, **Volume**, and **Reference Speed** are editable in the Inspector. Player playback follows resolved grounded movement, stopping during idle, jumping, boost, pause, or disable/death. Running increases its pitch/cadence. Nearby enemy movement uses full spatial attenuation; player sounds use partial spatial attenuation so they remain audible from the third-person camera.

These short SFX are imported as mono, preloaded clips. The source WAV files remain unchanged. Weapon animation reimports preserve their audio references. Cannon attack audio is owned by its weapon rig; CombatFeedback's separate fire audio is disabled on this player to prevent duplicates. Impact and kill-confirmation settings are preserved.

`MechSfxChecks` verifies playable clips, accepted/rejected attacks, ground/drop ownership, independent voices, actual grounded locomotion, faster running cadence, and silent boost/airborne states as part of `MechFoundationChecks.RunBatch` in an isolated Unity project.

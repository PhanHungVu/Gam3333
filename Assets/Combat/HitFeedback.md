# Hit shake and actor-local hitstop

Confirmed hits now produce their own camera jolt, and incoming damage uses a stronger existing shake. Brief hitstop holds the involved actors' movement and animation. The world clock, camera, already-fired projectiles, particles and unrelated actors continue running. No `Time.timeScale` or physics-timestep changes are made by hitstop.

## Default tuning

These settings live on **CombatFeedback** on `Prefabs/Mech/Base/FullPlayer_mech_base.prefab`:

| Feedback | Strength | Duration |
| --- | --- | --- |
| Accepted incoming damage shake | 0.16 (previously 0.08) | 0.24s (previously 0.20s) |
| Confirmed outgoing hit shake | 0.12 | 0.14s |
| Melee hitstop | — | 0.070s |
| Ordinary projectile hitstop | — | 0.035s |
| Blast/special-contact hitstop | — | 0.085s |
| Incoming damage hitstop | — | 0.080s |

Kills multiply the hit-confirm jolt by 1.15. Each impulse remains capped at 0.2; the existing Cinemachine listener uses the strongest active signal, preserving base camera position, targets and FOV. Outgoing hit shakes have a 0.06s minimum interval so an AoE/compound target burst does not flood the camera. Existing firing recoil and cannon world-impact feedback retain their own cues.

**Hit Camera Impulse** controls outgoing confirmations, **Damage Camera Impulse** controls taking-damage shake, and **Hit Stop** controls the holds. The presentation master toggle clears active holds owned by that feedback component as well as its VFX/shake. Strengths, durations and toggles are editable independently.

## Hit admission

`CombatBlast` emits `CombatVisualEvents.HitConfirmed(owner, victim, kind)` only after health accepts damage. Projectile, splash, melee and special-area attacks share that path. Friendly/blocked hits, misses, zero/invalid damage and duplicate colliders do not produce additional confirmations. The existing melee/projectile presentation events continue to own dust and projectile effects, so those effects are not duplicated.

Owned hits pause the player and the struck living actor. Receiving damage pauses the player; a validated incoming melee/special contact also pauses its attacker. A dead actor deactivates normally, preserving kill/drop events and ragdoll creation. Hitstop does not delay death or freeze a corpse.

## Actor behavior and recovery

`CombatHitStop` is created on an involved health root when first needed. It records/restores the original Animator speeds and holds the existing player/enemy movement and attack/animation updates. Manual weapon PlayableGraphs observe the same hold. It does not disable/restart the Behavior Graph or replace a CharacterController with a Rigidbody.

Actor defaults cap a pause at **0.10s**, followed by a **0.08s recovery gap**. Repeated requests during a pause cannot extend it or accumulate time. Add/configure the component on a particular actor prefab if it needs different bounds. Animator lists are reused across pauses and refreshed to include swapped parts. Disable/death, scene unload and feedback mute restore held animation speeds and remove the active registration.

Same-frame simultaneous weapon releases retain independent attack admission. Subsequent new attack requests wait until the hold ends. Already accepted windups/approaches and movement resume afterward. Existing absolute ammo/reload/cooldown clocks keep their normal rules. Jump/dodge presses received during the hold are buffered until movement resumes; leaving gameplay or disabling movement clears the buffer. Camera look remains available.

This is a local gameplay/presentation feature. It does not introduce or claim multiplayer synchronization; a future authoritative session implementation must integrate hitstop requests with its combat/movement ownership.

## Verification

`CombatHitFeelChecks` exercises real accepted damage, compound targets, real authored weapon poses, normal projectile confirmations, incoming damage, actor recovery, buffered dodge, original animation speeds, repeated-hit bounds, unrelated physics, camera framing and mute/death cleanup. The existing enemy combat unit checks isolate their synthetic-time attack tests from hitstop; the new checks advance real frames for hitstop itself. Camera signal checks use Cinemachine's strongest-signal API to match the actual listener configuration.

Verified **2026-11-05** in isolated Unity **6000.5.8f1 / Direct3D 11**: **222 checks passed** (149 original combat, 55 remaining-milestone checks and 18 new hit feedback checks), with no C# warnings/errors or unexpected runtime errors. Existing knockback, drops, ragdolls, extraction/results and Retry/Main Menu navigation remain passing. Artifacts are retained in `.quest-review/Results/HitShakeAndHitStop`. No manual real-input playtest or player build was performed; final shake strength and hitstop feel still need live review.

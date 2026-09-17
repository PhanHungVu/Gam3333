# Mech controller foundation

The game starts in free aim: the existing cursor stays at screen center and no enemy is acquired automatically, including when aiming at one. Press middle mouse to resume targeting in the last used soft/hard mode (soft on the first use). While targeting is active, hold middle mouse for 1.2 seconds to return to free aim. Releasing that hold does not turn targeting back on. Mode memory lasts for the current controller instance; it is not saved between game sessions.

In soft lock, mouse input controls camera rotation independently of enemy selection. The visible cursor initially acquires the nearest visible enemy by world distance from the mech. It stays with that enemy until the player actively aims the hidden screen-center cursor at another enemy, or the selected enemy becomes invalid. Enemies getting closer or crossing a stationary center aim cannot steal the selection. A lost, disabled, destroyed, out-of-range, or offscreen target is replaced by the nearest remaining visible enemy even without mouse input. This automatic selection is enabled only after the player explicitly leaves free aim.

A short middle-mouse click while targeting switches between soft and hard lock on release, without reselecting. Delaying the tap until release prevents a long hold from briefly changing modes first. In hard lock, the camera looks down from above and behind the mech, aims at the enemy's aim point, and the whole mech turns to face it, including while idle or boosting. The hidden center aim stays invisible. A deliberate mouse flick switches to the nearest visible enemy in that screen direction. Small mouse movements do not switch or rotate the locked view. Losing an active hard-lock target selects the nearest eligible visible replacement while preserving hard mode. With no replacement, hard mode waits for another enemy. Restoring a remembered hard mode with no eligible enemy waits safely until one becomes available; the camera remains under mouse control while waiting.

The first press from free aim restores immediately and consumes that whole button gesture, so holding or releasing that same press cannot immediately exit or toggle again. Pause, cursor release, and camera disable cancel unfinished gestures. `Free Aim Hold Duration` on `MechTargetLockController` sets the hold time. `IsFreeAim` reports whether targeting is explicitly off; `IsHardLocked` reports actual camera/body tracking of a valid enemy. `EnterFreeAim` and `ResumeTargeting` provide the same mode transitions for other callers.

WASD movement is always camera-relative. Hard lock rotates that camera, so the movement axes follow the view, but there is no enemy-relative approach/orbit movement. An active Quick Boost retains its captured world direction even when targets switch. With hard lock off, body facing retains the previous camera-relative movement and dodge behavior; idle facing does not turn toward enemies.

## Responsibilities

- `MechMovementController`: reads movement input once per frame, calculates direction, advances the motor, resolves CharacterController movement, and publishes state/telemetry. Separate methods handle planar motion, vertical motion, and facing.
- `CameraControlThirdPerson`: owns the player anchor and Cinemachine rig, mouse look, lock input, framing, and Quick Boost follow damping. Both lock modes share orbit framing and centered composition. Hard lock normally looks at the selected aim point; committed close melee briefly biases the look-at point toward the space between player and enemy.
- `MechTargetLockController`: owns selection, optional hard-lock state, and directional flick recognition; publishes target and mode changes. Collider storage grows to accommodate crowds and is reused after that. It does not create or modify UI.
- `LockOnCursor`: projects the selected aim point through the gameplay camera into its actual UI parent. `A33` explicitly assigns its cursor, canvas, camera, and targeting controller. Dynamically spawned HUDs can call `Bind`.
- `MechTargetable`: supplies an enemy aim position. `AutoMoveTarget` remains an active test-scene helper.
- `MechEnergyController`: boost costs, delayed recovery, and the energy change signal. The motor advances its recovery exactly once per movement tick.
- `MechBoostPresentation`: leans only `VisualMech` and manages exhaust/trails. The ignition audio slot is intentionally empty, so dashing is silent. Assign a clip later to enable boost audio; there is no generated fallback sound.
- `MechEnergyHud`: displays remaining energy on the scene Canvas.

Update order is camera input/toggle/flick in Update (-100), movement in Update, camera rig in LateUpdate (-100), Cinemachine at its default order, selection/lock validation (100), then cursor projection (200). Hard lock suppresses automatic center-crossing switches and retains offscreen targets while the camera catches up. Disabled/destroyed targets, range loss, and loss of eligible colliders replace the selection without changing the selected camera mode.

The camera reports actual free-look input through `ReportAimInput` before soft selection. That intent is consumed each selection frame; follow motion and enemy movement do not count as player aim. Both lock modes default to a 20-degree downward orbit pitch with a 1.5-unit raised pivot. Close or low enemies steepen the angle to keep the sightline above the mech. `Hard Lock Pitch`, `Hard Lock Height`, and `Hard Lock Orbit Response` on the camera tune this framing; these existing serialized settings now apply to soft lock too. Soft lock keeps manual yaw and hard lock tracks the enemy.

While targeting is enabled, soft-lock mouse pitch has a shared 20-degree baseline plus/minus 3 degrees at 25% vertical sensitivity. Close-range body clearance can raise the camera further in either mode. Set Lock On Vertical Range to zero for a fixed manual pitch. Hard mode ignores vertical mouse input; free aim retains its -18 to 30 degree range. The shared settings retain their original Hard Lock field names to preserve prefab tuning.

During a committed melee swing against a selected enemy within 6 m ahead and near the player's height, both modes blend in 7 degrees of extra downward pitch and reduce the lateral shoulder offset by 75%. Hard lock briefly frames a point weighted 65% toward the enemy and 35% toward the raised player focus. Soft lock retains manual yaw. The effect holds for 0.45 seconds and blends out; it introduces no zoom, and movement FOV/distance feedback is held steady during the blend. The visible cursor and projectile aim continue to use the actual enemy aim point. These values are adjustable under Melee Framing.

## Extension points

Movement states describe implemented actions only: `Grounded` (idle/walking), `GroundBoost` (sprinting with movement input), `Airborne` (existing jump/fall), `QuickBoost`, and ground-only `AssaultBoost`. There is no flight, sustained ascent, or air dodge. Weapons run independently of the movement state machine.

| Signal | Meaning / intended consumer |
| --- | --- |
| `StateChanged(previous, next)` | Fires only on transitions; animation and sustained effects. |
| `QuickBoostStarted(direction)` | Captured world-space dodge direction; ignition audio, trails, and a future FOV pulse. |
| `QuickBoostEnded()` | Completion or controller-disable cancellation; shut down transient effects. |
| `Landed(impactSpeed)` | Positive downward speed before contact; landing animation, dust, audio, and optional impulse. |
| `TargetChanged(previous, next)` | Selection changed or cleared; weapons and HUD. A destroyed target compares equal to null under Unity equality. |
| `HardLockChanged(active)` | Player changed camera/body lock mode; target loss does not fire this event. `IsHardLockMode` includes waiting for a target; `IsHardLocked` requires a live target. |
| `AssaultBoostStarted()` / `AssaultBoostEnded()` | Continuous rush presentation; release, dodge, loss of ground, exhaustion, and a blocking wall can end it. |

`Velocity` is actual world displacement per second after collision resolution. `MoveDirection` and `FacingDirection` are separate intentions. `QuickBoostProgress` is normalized while boosting and returns zero when inactive. `QuickBoostCooldownRemaining` includes active boost time. Consumers should subscribe on enable and unsubscribe on disable, and read the current state on subscription.

Per-frame movement events are published after motion and state have been updated. A boost with zero recovery can end and restart in the same frame; listeners may receive ended then started while the final state is still QuickBoost.

## Preserved tuning and next work

The serialized `normalProfile` name and its members are retained to preserve existing scenes. It is the single active camera settings block; obsolete lock profiles, duplicate inactive fields, and redundant profile blending have been removed. Movement speeds, acceleration, boost curve, duration/recovery, multipliers, and free-look sensitivity are retained.

## Ground movement feature pass

`A33` includes a slalom west of spawn, a walled camera corridor east of spawn, a gentle ramp southwest, and a marked 100m Assault Boost runway farther east. Course objects live under `Ground Movement Course` and can be edited normally.

| Control | Action |
| --- | --- |
| WASD | Camera-relative movement |
| Left Shift | Ground boost (existing sprint) |
| Left Alt | Quick Boost, 20 energy |
| Hold Left Ctrl | Ground Assault Boost; release to cancel |
| Middle mouse from free aim | Restore last used soft/hard targeting mode |
| Short middle-mouse click while targeting | Switch soft/hard mode on release |
| Hold middle mouse for 1.2 seconds while targeting | Enter free aim and remember the current mode |
| Mouse flick while locked | Switch to the nearest visible enemy in that direction |
| Escape / left click | Release / recapture mouse |

Energy capacity is 100, recovery is 25/second after a 0.6-second delay, and Assault Boost drains 18/second. Normal movement remains available with no energy. A motor without an assigned energy component retains unlimited-energy behavior for standalone prototypes.

Assault Boost accelerates toward 28 units/second, turns at up to 90 degrees/second, and requires 10 energy to start. Quick Boost cancels the rush and uses the normal camera-relative dodge direction. Exhaustion, collision cancellation, and dodge cancellation require releasing Ctrl before starting another rush; holding the key cannot produce automatic restart loops. Enemy data affects body facing only when hard lock is active.

Hard-lock switching settings are on `MechTargetLockController`: default 80 pixels within 0.2 seconds, minimum movement speed 120 pixels/second, 0.1 seconds of quiet input to rearm, and 0.25 seconds between switches. Candidates must lie within a 60-degree half-angle of the flick direction, in range and on screen; closest screen-space distance from the current enemy wins. One uninterrupted swipe triggers at most one switch. Lower `Switch Flick Distance` for easier switching. `Hard Lock Orbit Response` on the camera controls orbit catch-up (default 0.12 seconds). Existing right-stick-click LockOn binding remains available; flick recognition currently uses mouse delta.

Camera obstacle handling uses the Default layer and ignores objects tagged Player. New environment colliders should be on an included obstacle layer. The player uses the Player tag. Camera radius and return damping are adjustable. Quick Boost adds a maximum 7-degree FOV pulse; Assault Boost adds up to 10 degrees and 12% follow distance. The Movement Feedback toggle disables these lens/framing effects while retaining free mouse look and collision handling.

The player root is unscaled and keeps the equivalent upright capsule dimensions. The visible mesh is under `VisualMech/Body` with its original dimensions. Presentation never changes the root transform or collider. The old duplicate BoxCollider has been removed.

The current combat prototype is described below. Enemy attacks, player damage/invulnerability, optimal-range bonuses, equipment, waves, objectives, resource budgets, and drops remain deferred. Aerial movement is outside the current design.

## Basic weapon prototype

The `Mech` prefab now has `MechCombatController`. Hold left mouse (`Player/Attack`) for projectile fire and press right mouse (`Player/Melee`) for one melee swing. Existing gamepad Attack is button West; Melee is left shoulder. Both weapons remain available while moving or boosting. There are no new animations or sounds. Clicking to recapture the cursor requires releasing the attack button before weapons arm, so it does not accidentally fire.

Projectiles aim at the selected enemy's current aim point in soft/hard lock. In free aim they converge from the muzzle onto the center camera ray's first surface, or its range endpoint. Each shot then travels straight, with no homing, prediction, automatic lead, spread, or distance damage bonuses. A moving enemy can leave the launch line before the projectile arrives. Swept sphere collision catches thin walls/targets between physics steps, ignores the shooter and triggers, and damages the first impacted enemy once. Shots cannot originate beyond a wall intersecting the muzzle and expire at maximum travel range.

Melee tests an oriented box directly in front of the mech's movement root, following its facing rather than the camera or target. The cyan wireframe shows that volume, with a ground-level outline to help judge distance. It turns orange during approach and yellow for 0.15 seconds on impact. Each damageable enemy inside is hit once per swing, even with multiple colliders. World obstacles block hits; other enemies do not block the melee area. Normal reach is 4 m. A selected enemy just beyond it can extend the box by up to 1 m over 0.12 seconds, with one damage check at the end. Assistance only admits targets within 30 degrees ahead and fades as the angle increases. It never moves or rotates the mech, turns sideways, or pursues a fleeing target. Walls prevent extension and damage; losing the selected enemy or starting a dodge cancels a pending approach. Free aim and enemies outside these limits use the ordinary immediate attack. There is no animation or persistent overlap damage. `Show Melee Box` can hide the preview later.

| Prototype setting | Default |
| --- | --- |
| Enemy health | 100 |
| Projectile damage / interval | 15 / 0.2 seconds |
| Projectile speed / range / radius | 30 units/sec / 60 units / 0.12 units |
| Melee damage / cooldown | 60 / 0.65 seconds |
| Melee local center / size | (0, 0, 2.25) / (3, 2.5, 3.5) |

All weapon values are editable on the mech prefab. `CombatHealth` on the enemy prefab and existing scene enemies displays a shrinking green health bar, raises `Damaged(amount)` and `Died`, and immediately deactivates the enemy on death. This also stops movement, removes collision, and lets targeting release it. Health starts full in `Awake`; this prototype has no respawn or reset loop. `ShotFired(projectile)` and `MeleePerformed(hitCount)` are presentation extension points. The shared `Combat/Prototype Combat.mat` is explicitly referenced by the prefab/scene so its unlit shader is included in builds.

The prototype uses runtime projectile objects with bounded lifetime, not a weapon inventory or projectile pool. Scale/pooling can be added after the basic weapon feel is settled. The current scene's enemy population and movements are preserved; no spawn system or on-screen enemy cap is introduced by this pass.

Targeting starts off in free aim. Target-relative locomotion and threat-based automatic switching remain outside the current design.

## Regression checks

`Tests/Editor/MechFoundationChecks.cs` contains Unity play-mode checks for targeting, UI projection, crowd-buffer growth, movement tuning, boost lifecycle, landing signals, camera rig lifecycle, hard-lock transitions, directional flicks, actual camera aim, and independent dodge direction while locked.

Run in an isolated Unity 6000.5.8f1 project containing the Scripts and Tests folders and the same Cinemachine/Input System/uGUI package versions:

```
Unity.exe -batchmode -nographics -projectPath <isolated-project> -executeMethod MechFoundationChecks.RunBatch -logFile <test-log>
```

Do not add `-quit`: the runner enters Play mode asynchronously and exits with code 0 or 1 when finished. The batch entry point intentionally rejects interactive use. Tests create their own objects and do not load or save the working scene.
